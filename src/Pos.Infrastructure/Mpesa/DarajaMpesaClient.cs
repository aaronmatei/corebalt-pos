using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Pos.Application.Payments;

namespace Pos.Infrastructure.Mpesa;

/// <summary>
/// Safaricom Daraja implementation of <see cref="IMpesaClient"/>: OAuth (token cached until just
/// before expiry), STK push initiate, and STK push query. Registered as a singleton so the cached
/// token survives across requests. Network/HTTP failures are mapped to result objects rather than
/// thrown, so the caller can mark the tender failed and let the till retry or switch to cash.
/// </summary>
public sealed class DarajaMpesaClient : IMpesaClient
{
    private readonly HttpClient _http;
    private readonly MpesaSettingsResolver _resolver;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiresAt;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Daraja is CASE-SENSITIVE and requires PascalCase field names (BusinessShortCode, Password, …).
    // JsonContent.Create() otherwise defaults to camelCase, which Daraja reads as a MISSING
    // BusinessShortCode → "400.002.02 Invalid BusinessShortCode". Serialize requests names-as-declared.
    private static readonly JsonSerializerOptions DarajaRequestJson = new() { PropertyNamingPolicy = null };

    // Scoped: credentials are loaded PER TENANT (per request) from the resolver, so the OAuth token
    // cache lives only for this request instance — fine given STK pushes are infrequent.
    public DarajaMpesaClient(HttpClient http, MpesaSettingsResolver resolver)
    {
        _http = http;
        _resolver = resolver;
    }

    public async Task<StkPushResult> StkPushAsync(StkPushRequest request, CancellationToken ct = default)
    {
        var _o = await _resolver.CurrentAsync(ct);
        if (_o is null) return new StkPushResult(false, null, null, null, null, "M-Pesa is not configured for this store.");
        try
        {
            var token = await GetTokenAsync(_o, ct);
            var (timestamp, password) = Stamp(_o);
            var msisdn = NormalizeMsisdn(request.PhoneNumber);
            var body = new
            {
                BusinessShortCode = _o.ShortCode,
                Password = password,
                Timestamp = timestamp,
                TransactionType = _o.TransactionType,
                Amount = (long)Math.Ceiling(request.Amount), // Daraja expects whole currency units
                PartyA = msisdn,
                PartyB = _o.ShortCode,
                PhoneNumber = msisdn,
                CallBackURL = _o.CallbackUrl,
                AccountReference = Trim(request.AccountReference, 12),
                TransactionDesc = Trim(request.Description, 13)
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_o.BaseUrl}/mpesa/stkpush/v1/processrequest")
            {
                Content = JsonContent.Create(body, options: DarajaRequestJson)
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.SendAsync(req, ct);
            var payload = await resp.Content.ReadFromJsonAsync<StkPushResponse>(Json, ct);

            if (resp.IsSuccessStatusCode && payload?.ResponseCode == "0")
                return new StkPushResult(true, payload.CheckoutRequestID, payload.MerchantRequestID,
                    payload.ResponseCode, payload.ResponseDescription, null);

            var error = payload?.errorMessage ?? payload?.ResponseDescription ?? $"STK push rejected (HTTP {(int)resp.StatusCode}).";
            return new StkPushResult(false, payload?.CheckoutRequestID, payload?.MerchantRequestID,
                payload?.ResponseCode, payload?.ResponseDescription, error);
        }
        catch (Exception ex)
        {
            return new StkPushResult(false, null, null, null, null, $"STK push error: {ex.Message}");
        }
    }

    public async Task<StkQueryResult> StkQueryAsync(string checkoutRequestId, CancellationToken ct = default)
    {
        var _o = await _resolver.CurrentAsync(ct);
        if (_o is null) return new StkQueryResult(MpesaQueryState.Failed, -1, "M-Pesa is not configured for this store.", null);
        try
        {
            var token = await GetTokenAsync(_o, ct);
            var (timestamp, password) = Stamp(_o);
            var body = new
            {
                BusinessShortCode = _o.ShortCode,
                Password = password,
                Timestamp = timestamp,
                CheckoutRequestID = checkoutRequestId
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_o.BaseUrl}/mpesa/stkpushquery/v1/query")
            {
                Content = JsonContent.Create(body, options: DarajaRequestJson)
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.SendAsync(req, ct);
            var payload = await resp.Content.ReadFromJsonAsync<StkQueryResponse>(Json, ct);

            if (!resp.IsSuccessStatusCode)
            {
                // Until the customer enters (or declines) their PIN, Daraja replies non-200 with
                // errorCode 500.001.1001 "The transaction is being processed".
                var processing = payload?.errorCode == "500.001.1001";
                return new StkQueryResult(
                    processing ? MpesaQueryState.Processing : MpesaQueryState.Failed,
                    -1, payload?.errorMessage ?? $"Query failed (HTTP {(int)resp.StatusCode}).", null);
            }

            var code = int.TryParse(payload?.ResultCode, out var rc) ? rc : -1;
            var state = code == 0 ? MpesaQueryState.Success : MpesaQueryState.Failed;
            return new StkQueryResult(state, code, payload?.ResultDesc, null);
        }
        catch (Exception ex)
        {
            // Treat transient errors as "still processing" so polling keeps trying.
            return new StkQueryResult(MpesaQueryState.Processing, -1, $"Query error: {ex.Message}", null);
        }
    }

    private async Task<string> GetTokenAsync(MpesaOptions _o, CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt) return _token;
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt) return _token;

            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_o.ConsumerKey}:{_o.ConsumerSecret}"));
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_o.BaseUrl}/oauth/v1/generate?grant_type=client_credentials");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var token = await resp.Content.ReadFromJsonAsync<OAuthResponse>(Json, ct)
                ?? throw new InvalidOperationException("Empty OAuth response from Daraja.");

            _token = token.access_token;
            var seconds = int.TryParse(token.expires_in, out var s) ? s : 3600;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, seconds) - 60); // refresh ~1 min early
            return _token!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static (string Timestamp, string Password) Stamp(MpesaOptions _o)
    {
        // Daraja stamps in EAT (UTC+3). Password is Base64(ShortCode + Passkey + Timestamp) using the
        // SAME timestamp string that goes in the request body — they must match exactly.
        var ts = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var pw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_o.ShortCode}{_o.Passkey}{ts}"));
        return (ts, pw);
    }

    /// <summary>07XXXXXXXX / +2547XXXXXXXX / 7XXXXXXXX → 2547XXXXXXXX (Daraja's MSISDN form).</summary>
    internal static string NormalizeMsisdn(string phone)
    {
        var d = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (d.StartsWith('0')) return "254" + d[1..];
        if (d.StartsWith("254")) return d;
        if (d.StartsWith('7') || d.StartsWith('1')) return "254" + d;
        return d;
    }

    private static string Trim(string s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);

    // Daraja JSON shapes (camelCase-insensitive matches PascalCase fields via Web defaults).
    private sealed record OAuthResponse(string access_token, string expires_in);
    private sealed record StkPushResponse(
        string? MerchantRequestID, string? CheckoutRequestID, string? ResponseCode,
        string? ResponseDescription, string? CustomerMessage, string? errorMessage, string? errorCode);
    private sealed record StkQueryResponse(
        string? ResponseCode, string? ResponseDescription, string? MerchantRequestID,
        string? CheckoutRequestID, string? ResultCode, string? ResultDesc,
        string? errorMessage, string? errorCode);
}
