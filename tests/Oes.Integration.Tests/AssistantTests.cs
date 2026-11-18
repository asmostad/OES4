using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Crm;
using Oes.Infrastructure.Ai;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Work;
using static Oes.Integration.Tests.ScriptedAiClient;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class AssistantTests(MySqlFixture fx)
{
    private sealed record World(Guid Tenant, AppUser Admin, AppUser RepA, AppUser RepB, Guid OppA, Guid OppB);

    private async Task<World> WorldAsync()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var a = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var b = await fx.AddMemberAsync(t.Id, "Sales Rep");
        await using var s = fx.ScopeAs(t.Id, admin);
        async Task<Guid> Opp(AppUser owner, string last)
        {
            var c = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Ann", LastName = last, OwnerUserId = owner.Id });
            return await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = c, OwnerUserId = owner.Id, ExpectedValue = 15000 });
        }
        return new World(t.Id, admin, a, b, await Opp(a, "Visible"), await Opp(b, "Hidden"));
    }

    [Fact]
    public async Task Tools_run_as_the_user_and_the_transcript_replays_unchanged()
    {
        var w = await WorldAsync();
        fx.Ai.Reset(
            Calls("get_record", new { type = "opportunity", id = w.OppB }), // someone else's opportunity
            Calls("search_records", new { query = "Ann" }),
            Says("You have one: Ann Visible."));
        string answer;
        Guid convo;
        await using (var s = fx.ScopeAs(w.Tenant, w.RepA))
        {
            var assistant = s.Get<AssistantService>();
            convo = await assistant.StartAsync(null, null);
            answer = await assistant.SendAsync(convo, "Which Ann opportunities do I have?");
        }
        Assert.Equal("You have one: Ann Visible.", answer);
        Assert.Equal(3, fx.Ai.Requests.Count);

        // The hidden record came back as not found; search only found the visible one.
        var second = fx.Ai.Requests[1].Messages;
        var denied = second[^1].Blocks.OfType<AiToolResult>().Single();
        Assert.True(denied.IsError);
        Assert.Contains("doesn't have access", denied.Content);
        var found = fx.Ai.Requests[2].Messages[^1].Blocks.OfType<AiToolResult>().Single().Content;
        Assert.Contains("Ann Visible", found);
        Assert.DoesNotContain("Ann Hidden", found);

        // Earlier assistant turns go back exactly as received, thinking signature included.
        Assert.Equal("sig-tools", fx.Ai.Requests[2].Messages[1].Blocks.OfType<AiThinking>().Single().Signature);
        Assert.Contains("You're helping Member Sales Rep", fx.Ai.Requests[0].System);

        await using (var s = fx.ScopeAs(w.Tenant, w.RepA))
        {
            var db = s.Get<OesDbContext>();
            Assert.Equal(6, await db.AiMessages.CountAsync(m => m.ConversationId == convo)); // q, call, result, call, result, answer
            var calls = await db.AiToolCalls.Where(x => x.ConversationId == convo).OrderBy(x => x.CreatedAt).Select(x => x.Status).ToListAsync();
            Assert.Equal(["denied", "ok"], calls);
            var usage = await db.AiUsageDaily.SingleAsync(u => u.UserId == w.RepA.Id);
            Assert.Equal((3, 3000L, 600L, 0.024m), (usage.Requests, usage.InputTokens, usage.OutputTokens, usage.CostUsd)); // 3 x (1000 in at $4/M + 200 out at $20/M)

            var view = await s.Get<AssistantService>().GetAsync(convo);
            Assert.Equal(["user", "assistant"], view.Lines.Select(l => l.Role));
            Assert.Equal(["get_record", "search_records"], view.Lines[1].Tools);
        }

        // Conversations are private.
        await using (var s = fx.ScopeAs(w.Tenant, w.RepB))
            await Assert.ThrowsAsync<RecordNotFoundException>(() => s.Get<AssistantService>().GetAsync(convo));
    }

    [Fact]
    public async Task It_can_create_a_task_and_knows_the_record_it_was_opened_from()
    {
        var w = await WorldAsync();
        fx.Ai.Reset(
            Calls("create_task", new { title = "Call Ann about the estimate", due_date = "2026-12-04", record_type = "opportunity", record_id = w.OppA }),
            Says("Done: I added a task for Friday."));
        await using var s = fx.ScopeAs(w.Tenant, w.RepA);
        var assistant = s.Get<AssistantService>();
        await Assert.ThrowsAsync<RecordNotFoundException>(() => assistant.StartAsync(EntityTypes.Opportunity, w.OppB)); // can't open it from a hidden record
        var convo = await assistant.StartAsync(EntityTypes.Opportunity, w.OppA);
        await assistant.SendAsync(convo, "Remind me to call her Friday");
        Assert.Contains("opened this conversation from the opportunity", fx.Ai.Requests[0].System);
        var task = await s.Get<OesDbContext>().Tasks.SingleAsync(t => t.EntityId == w.OppA);
        Assert.Equal(("Call Ann about the estimate", w.RepA.Id), (task.Title, task.AssignedToUserId));
    }

    [Fact]
    public async Task Budgets_refusals_and_a_missing_key_stop_it_cleanly()
    {
        var w = await WorldAsync();
        await using var s = fx.ScopeAs(w.Tenant, w.RepA);
        var assistant = s.Get<AssistantService>();
        var convo = await assistant.StartAsync(null, null);

        fx.Ai.Reset(Says("", stop: "refusal"));
        Assert.Contains("can't help", await assistant.SendAsync(convo, "Something it won't do"));

        // Over the daily limit: refused before any model call.
        var db = s.Get<OesDbContext>();
        await db.AiUsageDaily.Where(u => u.UserId == w.RepA.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.CostUsd, 100m));
        fx.Ai.Reset();
        Assert.Contains("today's assistant limit", (await Assert.ThrowsAsync<CrmRuleException>(() => assistant.SendAsync(convo, "Hello?"))).Message);
        Assert.Empty(fx.Ai.Requests);

        fx.Ai.Reset();
        fx.Ai.IsConfigured = false;
        Assert.Contains("isn't connected", (await Assert.ThrowsAsync<CrmRuleException>(() => assistant.SendAsync(convo, "Hello?"))).Message);
        fx.Ai.IsConfigured = true;

        // Managers have the assistant too (reps and managers both get ai.assistant).
        var plain = await fx.AddMemberAsync(w.Tenant, "Production Manager");
        await using var p = fx.ScopeAs(w.Tenant, plain);
        Assert.NotNull(await p.Get<AssistantService>().ListAsync()); // managers have it
    }

    [Fact]
    public void Stored_blocks_round_trip_exactly()
    {
        var blocks = new List<AiBlock>
        {
            new AiThinking("", "EqQBCkYI-signature"), new AiText("Here's what I found."),
            new AiToolUse("toolu_1", "get_record", System.Text.Json.JsonSerializer.SerializeToElement(new { type = "opportunity", id = "abc" })),
            new AiToolResult("toolu_1", "{\"ok\":true}", false), new AiRedactedThinking("opaque"),
        };
        var back = BlockJson.Read(BlockJson.Write(blocks));
        Assert.Equal("EqQBCkYI-signature", ((AiThinking)back[0]).Signature);
        Assert.Equal("abc", ((AiToolUse)back[2]).Input.GetProperty("id").GetString());
        Assert.Equal(blocks[3], back[3]);
        Assert.Equal("tool_use", AnthropicAiClient.WireName("ToolUse"));
        Assert.Equal("end_turn", AnthropicAiClient.WireName("end_turn"));
    }
}
