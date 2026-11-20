using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Oes.Application.Abstractions;
using Oes.Infrastructure.Work;

namespace Oes.UnitTests;

public class ExpoPushSenderTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            Calls.Add((request, body));
            return answer(request, body);
        }
    }

    private static HttpResponseMessage Tickets(params string[] tickets) =>
        new(HttpStatusCode.OK) { Content = new StringContent($"{{\"data\":[{string.Join(",", tickets)}]}}", Encoding.UTF8, "application/json") };

    private static PushMessage Msg(int i) => new($"ExponentPushToken[abcdefghij{i:000}]", $"Title {i}", "Body", new Dictionary<string, string> { ["url"] = "/Tasks" }, 3);

    private static ExpoPushSender Sender(FakeHandler h, string? token = null) =>
        new(new HttpClient(h), Options.Create(new PushOptions { Provider = "Expo", ExpoAccessToken = token }));

    [Fact]
    public async Task Sends_expo_messages_and_reads_tickets_in_order()
    {
        var h = new FakeHandler((_, _) => Tickets("""{"status":"ok","id":"a"}""",
            """{"status":"error","message":"gone","details":{"error":"DeviceNotRegistered"}}"""));
        var tickets = await Sender(h, "secret-token").SendAsync([Msg(1), Msg(2)]);

        Assert.Equal([new PushTicket(true, false, null), new PushTicket(false, true, "DeviceNotRegistered")], tickets);
        var (req, body) = Assert.Single(h.Calls);
        Assert.Equal(ExpoPushSender.Endpoint, req.RequestUri!.ToString());
        Assert.Equal("Bearer secret-token", req.Headers.Authorization!.ToString());
        var first = JsonDocument.Parse(body).RootElement[0];
        Assert.Equal(("ExponentPushToken[abcdefghij001]", "Title 1", "Body", "/Tasks", "default", 3, "high"),
            (first.GetProperty("to").GetString(), first.GetProperty("title").GetString(), first.GetProperty("body").GetString(),
             first.GetProperty("data").GetProperty("url").GetString(), first.GetProperty("sound").GetString(), first.GetProperty("badge").GetInt32(),
             first.GetProperty("priority").GetString()));
    }

    [Fact]
    public async Task Batches_of_100_and_failures_become_failed_tickets()
    {
        var h = new FakeHandler((_, body) => Tickets(Enumerable.Repeat("""{"status":"ok","id":"x"}""", JsonDocument.Parse(body).RootElement.GetArrayLength()).ToArray()));
        var tickets = await Sender(h).SendAsync(Enumerable.Range(1, 150).Select(Msg).ToList());
        Assert.Equal(2, h.Calls.Count);
        Assert.Equal(150, tickets.Count(t => t.Ok));
        Assert.Null(h.Calls[0].Request.Headers.Authorization); // no access token configured

        var down = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        Assert.All(await Sender(down).SendAsync([Msg(1)]), t => Assert.Equal((false, false, "Push service answered 503."), (t.Ok, t.DeviceGone, t.Error)));
        var garbled = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json") });
        Assert.All(await Sender(garbled).SendAsync([Msg(1)]), t => Assert.False(t.Ok));
        Assert.False(new NoPushSender().IsConfigured);
    }
}
