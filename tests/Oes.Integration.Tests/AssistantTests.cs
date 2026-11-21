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

    [Fact]
    public async Task Answers_stream_text_and_lookups_and_are_stored_the_same_way()
    {
        var w = await WorldAsync();
        fx.Ai.Reset(
            _ => new AiResponse([new AiThinking("", "sig-a"), new AiText("Let me check."), new AiToolUse("toolu_s1", "search_records", System.Text.Json.JsonSerializer.SerializeToElement(new { query = "Ann" }))],
                "tool_use", SmallUsage, "claude-opus-5-5"),
            Says("You have Ann Visible."));
        var events = new List<AssistantEvent>();
        Guid convo;
        string answer;
        await using (var s = fx.ScopeAs(w.Tenant, w.RepA))
        {
            var assistant = s.Get<AssistantService>();
            convo = await assistant.StartAsync(null, null);
            answer = await assistant.SendAsync(convo, "Which Ann do I have?", e => { events.Add(e); return Task.CompletedTask; });
        }
        Assert.Equal("Let me check.\n\nYou have Ann Visible.", answer);
        Assert.Equal(["thinking", "text", "tool", "thinking", "text", "text"], events.Select(e => e.Type));
        Assert.Equal("search_records", events[2].Value);
        Assert.Equal(answer, string.Concat(events.Where(e => e.Type == AssistantEvent.Text).Select(e => e.Value))); // what streamed is what's kept
        await using (var s = fx.ScopeAs(w.Tenant, w.RepA))
            Assert.Equal(answer.Split("\n\n"), (await s.Get<AssistantService>().GetAsync(convo)).Lines.Where(l => l.Role == "assistant" && l.Text.Length > 0).Select(l => l.Text));

        // The page streams the same as server-sent events, starting a conversation when there isn't one.
        fx.Ai.Reset(Calls("search_knowledge", new { query = "warranty" }), Says("Nothing on file about that."));
        var web = fx.App.Browser();
        string Token(string html) => System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = w.RepA.Email!, ["Password"] = MySqlFixture.Password, ["__RequestVerificationToken"] = Token(await web.GetStringAsync("/Account/Login")),
        }));
        await web.GetAsync("/Account/SelectTenant");
        var page = await web.GetStringAsync("/Assistant");
        Assert.Contains("data-assistant-stream=\"/Assistant?handler=Stream\"", page);
        var res = await web.PostAsync("/Assistant?handler=Stream", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["message"] = "What's our warranty?", ["__RequestVerificationToken"] = Token(page),
        }));
        Assert.Equal("text/event-stream", res.Content.Headers.ContentType?.MediaType);
        var sse = await res.Content.ReadAsStringAsync();
        Assert.Matches("^event: start\ndata: [{]\"conversation\":\"[0-9a-f-]{36}\"[}]\n\n", sse);
        Assert.Contains("event: tool\ndata: {\"tool\":\"search_knowledge\",\"label\":\"Checking the knowledge base\"}", sse);
        Assert.Contains("event: text\ndata: {\"text\":\"Nothing on file about that.\"}", sse);
        Assert.Matches("event: done\ndata: [{]\"conversation\":\"[0-9a-f-]{36}\",\"url\":\"/Assistant[?]c=[0-9a-f-]{36}\"[}]\n\n$", sse);

        var empty = await (await web.PostAsync("/Assistant?handler=Stream", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["message"] = " ", ["__RequestVerificationToken"] = Token(page),
        }))).Content.ReadAsStringAsync();
        Assert.Contains("event: error\ndata: {\"message\":\"Type a message.\"}", empty);
    }

    [Fact]
    public async Task Field_app_chats_with_the_assistant_and_a_retried_send_asks_only_once()
    {
        var w = await WorldAsync();
        fx.Ai.Reset(Calls("search_records", new { query = "Ann" }), Says("You have one: Ann Visible."));
        var api = fx.App.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        var tok = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(api, "/api/v1/auth/token", new { email = w.RepA.Email, password = MySqlFixture.Password });
        var access = (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<System.Text.Json.JsonElement>(tok.Content)).GetProperty("accessToken").GetString();
        api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        async Task<System.Text.Json.JsonElement> Json(HttpResponseMessage r)
        {
            Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
            return await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<System.Text.Json.JsonElement>(r.Content);
        }
        Task<HttpResponseMessage> Post(string url, object body, string key)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = System.Net.Http.Json.JsonContent.Create(body) };
            req.Headers.Add("Idempotency-Key", key);
            return api.SendAsync(req);
        }

        Assert.True((await Json(await api.GetAsync("/api/v1/assistant"))).GetProperty("configured").GetBoolean());
        var id = (await Json(await Post("/api/v1/assistant/conversations", new { entityType = "opportunity", entityId = w.OppA }, Guid.NewGuid().ToString())))
            .GetProperty("id").GetGuid();
        var started = await Json(await api.GetAsync($"/api/v1/assistant/conversations/{id}"));
        Assert.Equal(("opportunity", w.OppA), (started.GetProperty("entityType").GetString(), started.GetProperty("entityId").GetGuid()));

        var key = Guid.NewGuid().ToString();
        var reply = await Json(await Post($"/api/v1/assistant/conversations/{id}/messages", new { text = "Which Ann leads do I have?" }, key));
        var lines = reply.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal("You have one: Ann Visible.", lines[^1].GetProperty("text").GetString());
        Assert.Equal("assistant", lines[^1].GetProperty("role").GetString());
        Assert.Equal(2, fx.Ai.Requests.Count);

        // The phone lost the answer and sends again with the same key: the saved answer comes back, nothing is asked again.
        var again = await Json(await Post($"/api/v1/assistant/conversations/{id}/messages", new { text = "Which Ann leads do I have?" }, key));
        Assert.Equal("You have one: Ann Visible.", again.GetProperty("lines").EnumerateArray().Last().GetProperty("text").GetString());
        Assert.Equal(2, fx.Ai.Requests.Count);

        Assert.Contains((await Json(await api.GetAsync("/api/v1/assistant"))).GetProperty("conversations").EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        var blank = await Post($"/api/v1/assistant/conversations/{id}/messages", new { text = " " }, Guid.NewGuid().ToString());
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, blank.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await api.DeleteAsync($"/api/v1/assistant/conversations/{id}")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await api.GetAsync($"/api/v1/assistant/conversations/{id}")).StatusCode);
    }
}
