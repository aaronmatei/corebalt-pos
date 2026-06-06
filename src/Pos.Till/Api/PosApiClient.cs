using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pos.Till.Api;

public interface IPosApiClient
{
    Task<ApiResult<IReadOnlyList<ProductDto>>> ListProductsAsync(CancellationToken ct = default);
    Task<ApiResult<ProductDto>> FindByBarcodeAsync(string barcode, CancellationToken ct = default);
    Task<ApiResult<CompleteSaleDto>> CheckoutAsync(CheckoutRequestDto request, CancellationToken ct = default);
    Task<ApiResult<SaleDto>> GetSaleAsync(Guid saleId, CancellationToken ct = default);
    Task<ApiResult<MpesaInitiateDto>> InitiateMpesaAsync(MpesaCheckoutRequestDto request, CancellationToken ct = default);
    Task<ApiResult<MpesaStatusDto>> GetMpesaStatusAsync(Guid saleId, CancellationToken ct = default);
}

/// <summary>
/// Typed HttpClient over Pos.Api. Identity travels as the three trusted headers the API
/// enforces (X-Tenant-Id / X-Store-Id / X-User-Id); the register id is a checkout-body field.
/// Every method returns an ApiResult so transport/HTTP failures surface as a message in the UI
/// rather than an exception — a till must degrade gracefully when the store server is down.
/// </summary>
public sealed class PosApiClient : IPosApiClient, IDisposable
{
    private const string ApiBase = "/api/v1";
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public PosApiClient(TillOptions options)
    {
        _baseUrl = options.BaseUrl.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(_baseUrl) };
        _http.DefaultRequestHeaders.Add("X-Tenant-Id", options.TenantId.ToString());
        _http.DefaultRequestHeaders.Add("X-Store-Id", options.StoreId.ToString());
        _http.DefaultRequestHeaders.Add("X-User-Id", options.UserId.ToString());
    }

    public Task<ApiResult<IReadOnlyList<ProductDto>>> ListProductsAsync(CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<ProductDto>>(() => _http.GetAsync($"{ApiBase}/products", ct), ct);

    public Task<ApiResult<ProductDto>> FindByBarcodeAsync(string barcode, CancellationToken ct = default) =>
        SendAsync<ProductDto>(() => _http.GetAsync($"{ApiBase}/products/barcode/{Uri.EscapeDataString(barcode)}", ct), ct);

    public Task<ApiResult<CompleteSaleDto>> CheckoutAsync(CheckoutRequestDto request, CancellationToken ct = default) =>
        SendAsync<CompleteSaleDto>(() => _http.PostAsJsonAsync($"{ApiBase}/sales/checkout", request, Json, ct), ct);

    public Task<ApiResult<SaleDto>> GetSaleAsync(Guid saleId, CancellationToken ct = default) =>
        SendAsync<SaleDto>(() => _http.GetAsync($"{ApiBase}/sales/{saleId}", ct), ct);

    public Task<ApiResult<MpesaInitiateDto>> InitiateMpesaAsync(MpesaCheckoutRequestDto request, CancellationToken ct = default) =>
        SendAsync<MpesaInitiateDto>(() => _http.PostAsJsonAsync($"{ApiBase}/sales/mpesa/checkout", request, Json, ct), ct);

    public Task<ApiResult<MpesaStatusDto>> GetMpesaStatusAsync(Guid saleId, CancellationToken ct = default) =>
        SendAsync<MpesaStatusDto>(() => _http.GetAsync($"{ApiBase}/sales/mpesa/{saleId}/status", ct), ct);

    private async Task<ApiResult<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await send();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiResult<T>.Failure(0, $"Cannot reach the store server at {_baseUrl}. Is Pos.Api running? ({ex.Message})");
        }

        using (resp)
        {
            if (resp.IsSuccessStatusCode)
            {
                var value = await resp.Content.ReadFromJsonAsync<T>(Json, ct);
                return value is null
                    ? ApiResult<T>.Failure((int)resp.StatusCode, "The server returned an empty response.")
                    : ApiResult<T>.Success(value);
            }

            if (resp.StatusCode == HttpStatusCode.NotFound)
                return ApiResult<T>.Failure(404, "Not found.");

            var message = await ReadProblemAsync(resp, ct);
            return ApiResult<T>.Failure((int)resp.StatusCode, message);
        }
    }

    private static async Task<string> ReadProblemAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var problem = await resp.Content.ReadFromJsonAsync<ProblemDto>(Json, ct);
            var text = problem?.Detail ?? problem?.Title;
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        catch
        {
            // Body wasn't ProblemDetails — fall back to the status line below.
        }
        return $"{(int)resp.StatusCode} {resp.ReasonPhrase}".Trim();
    }

    public void Dispose() => _http.Dispose();
}
