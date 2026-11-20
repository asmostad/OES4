using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Oes.Application.Abstractions;

namespace Oes.Infrastructure.Work;

public sealed class PushOptions
{
    public const string Section = "Push";

    /// <summary>"Expo" to send through Expo's push service; anything else (default "None") means pushes are off.</summary>
    public string Provider { get; set; } = "None";

    /// <summary>
    /// Optional Expo access token. With "enhanced push security" on in the Expo dashboard, only requests with it are
    /// delivered. A secret: user-secrets or an environment variable, never a committed file.
    /// </summary>
    public string? ExpoAccessToken { get; set; }
}

/// <summary>
/// Sends pushes through Expo's push service (https://exp.host/--/api/v2/push/send), which hands them to Apple and
/// Google. Up to 100 messages per request. Titles and bodies pass through Expo, so they're kept short and factual.
/// </summary>
public sealed class ExpoPushSender(HttpClient http, IOptions<PushOptions> options) : IPushSender
{
    public const string Endpoint = "https://exp.host/--/api/v2/push/send";
    public const int BatchSize = 100;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public bool IsConfigured => string.Equals(options.Value.Provider, "Expo", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<PushTicket>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken ct = default)
    {
        var tickets = new List<PushTicket>(messages.Count);
        foreach (var batch in messages.Chunk(BatchSize))
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = JsonContent.Create(batch.Select(Wire).ToList(), options: Json),
            };
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(options.Value.ExpoAccessToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ExpoAccessToken);
            try
            {
                using var res = await http.SendAsync(req, ct);
                var body = res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<Response>(Json, ct) : null;
                if (body?.Data is { } data && data.Count == batch.Length)
                    tickets.AddRange(data.Select(t => new PushTicket(t.Status == "ok", t.Details?.Error == "DeviceNotRegistered", t.Status == "ok" ? null : t.Details?.Error ?? t.Message)));
                else tickets.AddRange(batch.Select(_ => new PushTicket(false, false, $"Push service answered {(int)res.StatusCode}.")));
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or JsonException) && !ct.IsCancellationRequested)
            {
                tickets.AddRange(batch.Select(_ => new PushTicket(false, false, "Push service unreachable.")));
            }
        }
        return tickets;
    }

    /// <summary>The Expo message shape: to, title, body, data, sound, badge, priority.</summary>
    public static object Wire(PushMessage m) => new WireMessage(m.To, m.Title, m.Body, m.Data, "default", m.Badge, "high");

    private sealed record WireMessage(string To, string Title, string? Body, IReadOnlyDictionary<string, string> Data, string Sound, int? Badge, string Priority);
    private sealed record Response(List<Ticket>? Data);
    private sealed record Ticket(string Status, string? Id, string? Message, TicketDetails? Details);
    private sealed record TicketDetails(string? Error);
}

/// <summary>Pushes are off (no provider configured): the push job marks notifications handled without sending.</summary>
public sealed class NoPushSender : IPushSender
{
    public bool IsConfigured => false;
    public Task<IReadOnlyList<PushTicket>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PushTicket>>([]);
}
