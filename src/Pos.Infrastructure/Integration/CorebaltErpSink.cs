using System.Net.Http.Json;
using System.Text.Json;
using Pos.Application.Integration;

namespace Pos.Infrastructure.Integration;

/// <summary>
/// Adapter that POSTs a completed sale to Corebalt's POS sale-webhook
/// (<c>POST {BaseUrl}/webhooks/pos/{TenantSlug}/sale</c>) with the shared service-token header.
/// Singleton: holds one long-lived <see cref="HttpClient"/> (one endpoint, low volume). camelCase JSON
/// to match the ERP's Jackson contract. Throws on non-success so the forwarder retries.
/// </summary>
internal sealed class CorebaltErpSink : IErpSaleSink, IDisposable
{
    public const string ServiceTokenHeader = "X-Corebalt-Service-Token";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly CorebaltErpOptions _options;
    private readonly HttpClient _http;

    public CorebaltErpSink(CorebaltErpOptions options)
    {
        _options = options;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds)) };
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            _http.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    }

    public async Task SendSaleAsync(ErpSaleDto sale, CancellationToken ct = default)
    {
        var path = $"/webhooks/pos/{_options.TenantSlug}/sale";
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(sale, options: Json),
        };
        request.Headers.Add(ServiceTokenHeader, _options.ServiceToken);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}
