using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pos.Till.Api;

public interface IPosApiClient
{
    Task<ApiResult<TokenDto>> PinLoginAsync(string staffCode, string pin, CancellationToken ct = default);
    void SetAccessToken(string token);
    void ClearAccessToken();
    Task<ApiResult<IReadOnlyList<ProductDto>>> ListProductsAsync(CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<CategoryDto>>> ListCategoriesAsync(CancellationToken ct = default);
    Task<ApiResult<ProductDto>> FindByBarcodeAsync(string barcode, CancellationToken ct = default);
    Task<ApiResult<CompleteSaleDto>> CheckoutAsync(CheckoutRequestDto request, CancellationToken ct = default);
    Task<ApiResult<SaleDto>> GetSaleAsync(Guid saleId, CancellationToken ct = default);
    Task<ApiResult<MpesaInitiateDto>> InitiateMpesaAsync(MpesaCheckoutRequestDto request, CancellationToken ct = default);
    Task<ApiResult<MpesaStatusDto>> GetMpesaStatusAsync(Guid saleId, CancellationToken ct = default);
    Task<ApiResult<ReceiptDto>> GetReceiptAsync(Guid saleId, CancellationToken ct = default);

    // Cash management / shifts. The gated actions accept a one-off bearer override so a Cashier can call
    // a Supervisor/Manager endpoint by entering that person's PIN, without disturbing the session token.
    Task<ApiResult<SessionDto>> GetCurrentSessionAsync(Guid registerId, CancellationToken ct = default);
    Task<ApiResult<SessionDto>> OpenSessionAsync(OpenSessionRequestDto request, CancellationToken ct = default);
    Task<ApiResult<ShiftReportDto>> GetSessionReportAsync(Guid sessionId, CancellationToken ct = default);
    Task<ApiResult<bool>> PrintSessionReportAsync(Guid sessionId, CancellationToken ct = default);
    Task<ApiResult<object>> RecordCashMovementAsync(CashMovementRequestDto request, string? bearerOverride = null, CancellationToken ct = default);
    Task<ApiResult<ShiftReportDto>> CloseSessionAsync(Guid sessionId, CloseSessionRequestDto request, string? bearerOverride = null, CancellationToken ct = default);
}

/// <summary>
/// Typed HttpClient over Pos.Api. Identity is a JWT bearer token obtained from /auth/pin-login and
/// held for the session (tenant/store/user/role/name all travel inside it); the register id is a
/// checkout-body field. Every method returns an ApiResult so transport/HTTP failures surface as a
/// message in the UI rather than an exception — a till must degrade gracefully when the server is down.
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
    }

    public Task<ApiResult<TokenDto>> PinLoginAsync(string staffCode, string pin, CancellationToken ct = default) =>
        SendAsync<TokenDto>(() => _http.PostAsJsonAsync($"{ApiBase}/auth/pin-login",
            new { staffCode, pin }, Json, ct), ct);

    /// <summary>Hold the session token; sent as the bearer on every subsequent call.</summary>
    public void SetAccessToken(string token) =>
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public void ClearAccessToken() => _http.DefaultRequestHeaders.Authorization = null;

    public Task<ApiResult<IReadOnlyList<ProductDto>>> ListProductsAsync(CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<ProductDto>>(() => _http.GetAsync($"{ApiBase}/products", ct), ct);

    public Task<ApiResult<IReadOnlyList<CategoryDto>>> ListCategoriesAsync(CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<CategoryDto>>(() => _http.GetAsync($"{ApiBase}/categories", ct), ct);

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

    public Task<ApiResult<ReceiptDto>> GetReceiptAsync(Guid saleId, CancellationToken ct = default) =>
        SendAsync<ReceiptDto>(() => _http.GetAsync($"{ApiBase}/sales/{saleId}/receipt", ct), ct);

    // ── Cash management / shifts ────────────────────────────────────────────────────────────────
    public Task<ApiResult<SessionDto>> GetCurrentSessionAsync(Guid registerId, CancellationToken ct = default) =>
        SendAsync<SessionDto>(() => _http.GetAsync($"{ApiBase}/sessions/current?registerId={registerId}", ct), ct);

    public Task<ApiResult<SessionDto>> OpenSessionAsync(OpenSessionRequestDto request, CancellationToken ct = default) =>
        SendAsync<SessionDto>(() => _http.PostAsJsonAsync($"{ApiBase}/sessions/open", request, Json, ct), ct);

    public Task<ApiResult<ShiftReportDto>> GetSessionReportAsync(Guid sessionId, CancellationToken ct = default) =>
        SendAsync<ShiftReportDto>(() => _http.GetAsync($"{ApiBase}/sessions/{sessionId}/report", ct), ct);

    public Task<ApiResult<bool>> PrintSessionReportAsync(Guid sessionId, CancellationToken ct = default) =>
        SendStatusAsync(() => _http.PostAsync($"{ApiBase}/sessions/{sessionId}/print", content: null, ct), ct);

    public Task<ApiResult<object>> RecordCashMovementAsync(CashMovementRequestDto request, string? bearerOverride = null, CancellationToken ct = default) =>
        SendAsync<object>(() => _http.SendAsync(Build(HttpMethod.Post, $"{ApiBase}/sessions/movements", request, bearerOverride), ct), ct);

    public Task<ApiResult<ShiftReportDto>> CloseSessionAsync(Guid sessionId, CloseSessionRequestDto request, string? bearerOverride = null, CancellationToken ct = default) =>
        SendAsync<ShiftReportDto>(() => _http.SendAsync(Build(HttpMethod.Post, $"{ApiBase}/sessions/{sessionId}/close", request, bearerOverride), ct), ct);

    /// <summary>Build a request, optionally with a one-off bearer (overrides the session token).</summary>
    private HttpRequestMessage Build(HttpMethod method, string url, object? body, string? bearerOverride)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body, mediaType: null, Json);
        if (bearerOverride is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerOverride);
        return req;
    }

    private async Task<ApiResult<bool>> SendStatusAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            using var resp = await send();
            return resp.IsSuccessStatusCode
                ? ApiResult<bool>.Success(true)
                : ApiResult<bool>.Failure((int)resp.StatusCode, await ReadProblemAsync(resp, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiResult<bool>.Failure(0, $"Cannot reach the store server at {_baseUrl}. ({ex.Message})");
        }
    }

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
