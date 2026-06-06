using Pos.Application.Abstractions;

namespace Pos.Application.Tenancy;

/// <summary>Blocks transacting until the install has been set up (a MerchantProfile marked complete).</summary>
public interface ISetupGuard
{
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);
    Task EnsureConfiguredAsync(CancellationToken ct = default);
}

public sealed class SetupGuard : ISetupGuard
{
    private readonly ICurrentContext _ctx;
    private readonly IMerchantProfileRepository _merchants;

    public SetupGuard(ICurrentContext ctx, IMerchantProfileRepository merchants)
    {
        _ctx = ctx;
        _merchants = merchants;
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        (await _merchants.GetAsync(_ctx.TenantId, ct))?.SetupComplete == true;

    public async Task EnsureConfiguredAsync(CancellationToken ct = default)
    {
        if (!await IsConfiguredAsync(ct))
            throw new InvalidOperationException("This store is not set up yet. Complete first-run setup before transacting.");
    }
}
