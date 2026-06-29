using System.Net.Http.Json;
using System.Text.Json;
using Pos.Application.Identity;
using Pos.Application.Sync;

namespace Pos.Infrastructure.Sync;

/// <summary>HTTP adapter for <see cref="IHqTransferPullClient"/> (M3): GET incoming transfers, POST receipt
/// acks, both with the store's sync token + its StoreId. Throws on non-2xx so the receiver retries.</summary>
internal sealed class HqTransferPullHttpClient : IHqTransferPullClient, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HqSyncOptions _options;
    private readonly StoreServerOptions _server;
    private readonly HttpClient _http;

    public HqTransferPullHttpClient(HqSyncOptions options, StoreServerOptions server)
    {
        _options = options;
        _server = server;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds)) };
        if (!string.IsNullOrWhiteSpace(options.CloudBaseUrl))
            _http.BaseAddress = new Uri(options.CloudBaseUrl, UriKind.Absolute);
    }

    public async Task<IReadOnlyList<TransferSnapshot>> IncomingAsync(CancellationToken ct = default)
    {
        var url = $"/hq/transfers/incoming?slug={Uri.EscapeDataString(_options.TenantSlug)}&storeId={_server.StoreId}";
        using var msg = new HttpRequestMessage(HttpMethod.Get, url);
        msg.Headers.Add(SyncHeaders.Token, _options.SyncToken);
        using var resp = await _http.SendAsync(msg, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<IncomingTransfersResponse>(Json, ct);
        return body?.Transfers ?? [];
    }

    public async Task AckReceivedAsync(Guid transferId, CancellationToken ct = default)
    {
        var url = $"/hq/transfers/{transferId}/received?slug={Uri.EscapeDataString(_options.TenantSlug)}&storeId={_server.StoreId}";
        using var msg = new HttpRequestMessage(HttpMethod.Post, url);
        msg.Headers.Add(SyncHeaders.Token, _options.SyncToken);
        using var resp = await _http.SendAsync(msg, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<BranchDto>> BranchesAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"/hq/branches?slug={Uri.EscapeDataString(_options.TenantSlug)}&storeId={_server.StoreId}";
            using var msg = new HttpRequestMessage(HttpMethod.Get, url);
            msg.Headers.Add(SyncHeaders.Token, _options.SyncToken);
            using var resp = await _http.SendAsync(msg, ct);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<BranchesResponse>(Json, ct);
            return body?.Branches ?? [];
        }
        catch { return []; } // the picker degrades gracefully if HQ is unreachable
    }

    public void Dispose() => _http.Dispose();
}
