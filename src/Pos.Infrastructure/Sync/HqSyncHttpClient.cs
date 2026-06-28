using System.Net.Http.Json;
using System.Text.Json;
using Pos.Application.Sync;

namespace Pos.Infrastructure.Sync;

/// <summary>
/// HTTP adapter for <see cref="IHqSyncClient"/>: POSTs a batch to {CloudBaseUrl}/hq/sync/ingest with the
/// store's sync token in the <c>X-Sync-Token</c> header. Throws on a non-2xx / transport failure so the
/// pusher acks nothing and retries.
/// </summary>
internal sealed class HqSyncHttpClient : IHqSyncClient, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HqSyncOptions _options;
    private readonly HttpClient _http;

    public HqSyncHttpClient(HqSyncOptions options)
    {
        _options = options;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds)) };
        if (!string.IsNullOrWhiteSpace(options.CloudBaseUrl))
            _http.BaseAddress = new Uri(options.CloudBaseUrl, UriKind.Absolute);
    }

    public async Task<SyncIngestResponse> PushAsync(SyncIngestRequest request, CancellationToken ct = default)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/hq/sync/ingest")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        msg.Headers.Add(SyncHeaders.Token, _options.SyncToken);

        using var response = await _http.SendAsync(msg, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SyncIngestResponse>(Json, ct)
            ?? new SyncIngestResponse([]);
    }

    public void Dispose() => _http.Dispose();
}
