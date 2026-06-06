using Microsoft.Extensions.Configuration;
using Pos.Application.Abstractions;
using Pos.Application.Tenancy;
using Pos.Domain.Tenancy;

namespace Pos.Infrastructure.Mpesa;

/// <summary>
/// Resolves the CURRENT TENANT's M-Pesa credentials (from DB, decrypted) into MpesaOptions for the
/// Daraja client. Returns null when M-Pesa isn't configured/enabled for the tenant. Callback URL +
/// transaction type are host config (not per-tenant secrets); the base URL follows the tenant's
/// Sandbox/Production environment.
/// </summary>
public sealed class MpesaSettingsResolver
{
    private const string SandboxUrl = "https://sandbox.safaricom.co.ke";
    private const string ProductionUrl = "https://api.safaricom.co.ke";

    private readonly ICurrentContext _ctx;
    private readonly IMpesaSettingsRepository _repo;
    private readonly IConfiguration _config;

    public MpesaSettingsResolver(ICurrentContext ctx, IMpesaSettingsRepository repo, IConfiguration config)
    {
        _ctx = ctx;
        _repo = repo;
        _config = config;
    }

    public async Task<MpesaOptions?> CurrentAsync(CancellationToken ct = default)
    {
        var s = await _repo.GetAsync(_ctx.TenantId, ct);
        if (s is null || !s.IsConfigured) return null;

        return new MpesaOptions
        {
            BaseUrl = s.Environment == MpesaEnvironment.Production ? ProductionUrl : SandboxUrl,
            ShortCode = s.ShortCode,
            ConsumerKey = s.ConsumerKey,
            ConsumerSecret = s.ConsumerSecret,
            Passkey = s.Passkey,
            CallbackUrl = _config["Mpesa:CallbackUrl"] ?? "https://example.com/mpesa/callback",
            TransactionType = _config["Mpesa:TransactionType"] ?? "CustomerPayBillOnline",
        };
    }
}
