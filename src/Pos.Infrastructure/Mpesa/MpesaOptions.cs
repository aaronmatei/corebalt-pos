using Microsoft.Extensions.Configuration;

namespace Pos.Infrastructure.Mpesa;

/// <summary>
/// Daraja credentials + endpoints. Defaults target the Safaricom SANDBOX and a known public test
/// shortcode/passkey is NEVER baked in — keys come from the "Mpesa" config section (user-secrets in
/// dev) or POS_MPESA_* environment variables, and must not be committed. See README "M-Pesa".
/// </summary>
public sealed class MpesaOptions
{
    public string BaseUrl { get; set; } = "https://sandbox.safaricom.co.ke";
    public string ConsumerKey { get; set; } = "";
    public string ConsumerSecret { get; set; } = "";
    public string Passkey { get; set; } = "";
    public string ShortCode { get; set; } = "174379"; // Daraja sandbox test till; override for production
    public string CallbackUrl { get; set; } = "https://example.com/mpesa/callback"; // dev confirms via polling
    public string TransactionType { get; set; } = "CustomerPayBillOnline";

    /// <summary>True once the secrets needed to actually call Daraja are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ConsumerKey) &&
        !string.IsNullOrWhiteSpace(ConsumerSecret) &&
        !string.IsNullOrWhiteSpace(Passkey);

    public static MpesaOptions FromConfiguration(IConfiguration config)
    {
        var o = new MpesaOptions();
        var s = config.GetSection("Mpesa"); // appsettings / user-secrets

        // Precedence: POS_MPESA_* env var > "Mpesa" config section > built-in default.
        o.BaseUrl        = Env("POS_MPESA_BASEURL")        ?? s["BaseUrl"]        ?? o.BaseUrl;
        o.ConsumerKey    = Env("POS_MPESA_CONSUMERKEY")    ?? s["ConsumerKey"]    ?? o.ConsumerKey;
        o.ConsumerSecret = Env("POS_MPESA_CONSUMERSECRET") ?? s["ConsumerSecret"] ?? o.ConsumerSecret;
        o.Passkey        = Env("POS_MPESA_PASSKEY")        ?? s["Passkey"]        ?? o.Passkey;
        o.ShortCode      = Env("POS_MPESA_SHORTCODE")      ?? s["ShortCode"]      ?? o.ShortCode;
        o.CallbackUrl    = Env("POS_MPESA_CALLBACKURL")    ?? s["CallbackUrl"]    ?? o.CallbackUrl;
        o.TransactionType = s["TransactionType"] ?? o.TransactionType;
        return o;
    }

    private static string? Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}
