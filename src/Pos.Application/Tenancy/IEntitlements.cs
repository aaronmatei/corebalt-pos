using Pos.Application.Abstractions;
using Pos.Domain.Tenancy;

namespace Pos.Application.Tenancy;

/// <summary>Reads the current tenant's licence to gate optional modules/UI. Expiry is honoured.</summary>
public interface IEntitlements
{
    Task<bool> HasAsync(Feature feature, CancellationToken ct = default);
    Task<Entitlements?> CurrentAsync(CancellationToken ct = default);
}

public sealed class EntitlementsService : IEntitlements
{
    private readonly ICurrentContext _ctx;
    private readonly IEntitlementsRepository _repo;
    private readonly IClock _clock;

    public EntitlementsService(ICurrentContext ctx, IEntitlementsRepository repo, IClock clock)
    {
        _ctx = ctx;
        _repo = repo;
        _clock = clock;
    }

    public Task<Entitlements?> CurrentAsync(CancellationToken ct = default) => _repo.GetAsync(_ctx.TenantId, ct);

    public async Task<bool> HasAsync(Feature feature, CancellationToken ct = default)
    {
        var ent = await _repo.GetAsync(_ctx.TenantId, ct);
        return ent is not null && ent.Has(feature, _clock.UtcNow);
    }
}
