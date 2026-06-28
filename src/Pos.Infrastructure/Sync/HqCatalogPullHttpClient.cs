using System.Net.Http.Json;
using System.Text.Json;
using Pos.Application.Sync;

namespace Pos.Infrastructure.Sync;

/// <summary>HTTP adapter for <see cref="ICatalogPullClient"/>: GETs {CloudBaseUrl}/hq/catalog/changes with
/// the store's sync token. Throws on non-2xx / transport failure so the puller retries next pass.</summary>
internal sealed class HqCatalogPullHttpClient : ICatalogPullClient, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HqSyncOptions _options;
    private readonly HttpClient _http;

    public HqCatalogPullHttpClient(HqSyncOptions options)
    {
        _options = options;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds)) };
        if (!string.IsNullOrWhiteSpace(options.CloudBaseUrl))
            _http.BaseAddress = new Uri(options.CloudBaseUrl, UriKind.Absolute);
    }

    public async Task<CatalogPullResponse> PullAsync(long since, int max, CancellationToken ct = default)
    {
        var url = $"/hq/catalog/changes?slug={Uri.EscapeDataString(_options.TenantSlug)}&since={since}&max={max}";
        using var msg = new HttpRequestMessage(HttpMethod.Get, url);
        msg.Headers.Add(SyncHeaders.Token, _options.SyncToken);
        using var resp = await _http.SendAsync(msg, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<CatalogPullResponse>(Json, ct)
            ?? new CatalogPullResponse([], since, false);
    }

    public void Dispose() => _http.Dispose();
}
