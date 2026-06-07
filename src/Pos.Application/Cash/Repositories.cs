using Pos.Domain.Cash;

namespace Pos.Application.Cash;

public interface IRegisterSessionRepository
{
    /// <summary>The single Open session for a register, if any.</summary>
    Task<RegisterSession?> GetOpenAsync(Guid tenantId, Guid storeId, Guid registerId, CancellationToken ct = default);
    Task<RegisterSession?> GetAsync(Guid tenantId, Guid storeId, Guid sessionId, CancellationToken ct = default);
    Task AddAsync(RegisterSession session, CancellationToken ct = default);

    /// <summary>Sessions opened in a window, newest first — back-office review (optionally one register).</summary>
    Task<IReadOnlyList<RegisterSession>> ListAsync(Guid tenantId, Guid storeId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? registerId, CancellationToken ct = default);
}

public interface ICashMovementRepository
{
    Task AddAsync(CashMovement movement, CancellationToken ct = default);
    Task<IReadOnlyList<CashMovement>> ListBySessionAsync(Guid tenantId, Guid storeId, Guid sessionId, CancellationToken ct = default);
}

/// <summary>Cash-office policy: the variance magnitude (in the store currency) above which closing a
/// shift needs Manager acknowledgement.</summary>
public sealed class CashOfficeOptions
{
    public decimal VarianceAckThreshold { get; init; } = 500m;
}
