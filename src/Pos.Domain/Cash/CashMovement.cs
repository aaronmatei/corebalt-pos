using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Cash;

/// <summary>Drawer cash events ONLY — never sales cash or cash refunds (those are Tender/CreditNote
/// facts; single source per fact). OpeningFloat is recorded once when the shift opens.</summary>
public enum CashMovementType { OpeningFloat = 0, PayIn = 1, PayOut = 2, Drop = 3 }

/// <summary>
/// An immutable drawer movement tied to a <see cref="RegisterSession"/>. Amount is always positive; the
/// <see cref="Type"/> decides its sign in the expected-cash formula (<see cref="SignedAmount"/>).
/// </summary>
public sealed class CashMovement : Entity, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid RegisterId { get; private set; }
    public Guid SessionId { get; private set; }
    public CashMovementType Type { get; private set; }
    public Money Amount { get; private set; } = Money.Zero();
    public string Reason { get; private set; } = string.Empty;
    public Guid UserId { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private CashMovement() { } // EF

    public static CashMovement Record(Guid tenantId, Guid storeId, Guid registerId, Guid sessionId,
        CashMovementType type, Money amount, string? reason, Guid userId, string userName)
    {
        if (amount.Amount <= 0) throw new ArgumentException("A cash movement amount must be positive.", nameof(amount));
        return new CashMovement
        {
            Id = Uuid7.NewGuid(),
            TenantId = tenantId,
            StoreId = storeId,
            RegisterId = registerId,
            SessionId = sessionId,
            Type = type,
            Amount = new Money(amount.Amount, amount.Currency),
            Reason = reason?.Trim() ?? string.Empty,
            UserId = userId,
            UserName = userName ?? string.Empty,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>+ for cash coming IN (OpeningFloat, PayIn), − for cash going OUT (PayOut, Drop).</summary>
    public decimal SignedAmount => Type is CashMovementType.PayOut or CashMovementType.Drop ? -Amount.Amount : Amount.Amount;
}
