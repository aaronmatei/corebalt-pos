using Pos.SharedKernel;
using Pos.SharedKernel.Ids;
using Pos.Domain.Cash.Events;

namespace Pos.Domain.Cash;

public enum SessionStatus { Open = 0, Closed = 1 }

/// <summary>
/// A register SHIFT — the spine of cash management. One Open session per register at a time; sales,
/// returns and cash movements attach to it. Opening is a fact; CLOSING freezes the counted/expected/
/// variance and makes the session an immutable end-of-day fact (INVARIANT #3 — never edited after).
/// Expected/Variance are computed from the session's facts at close time and stored, not tracked live.
/// </summary>
public sealed class RegisterSession : AggregateRoot, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }      // the branch that owns this session
    public Guid RegisterId { get; private set; }   // the lane / till
    public string RegisterLabel { get; private set; } = string.Empty; // "Lane 1" captured at open

    public Guid OpenedBy { get; private set; }
    public string OpenedByName { get; private set; } = string.Empty;
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public Money OpeningFloat { get; private set; } = Money.Zero();

    public Guid? ClosedBy { get; private set; }
    public string? ClosedByName { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    // Frozen at close — immutable cash-up facts.
    public Money? CountedCash { get; private set; }
    public Money? ExpectedCash { get; private set; }
    public Money? Variance { get; private set; } // CountedCash − ExpectedCash (negative = short, positive = over)
    public bool VarianceAcknowledged { get; private set; } // a Manager signed off a large variance

    public SessionStatus Status { get; private set; }
    public bool IsOpen => Status == SessionStatus.Open;

    private RegisterSession() { } // EF

    public static RegisterSession Open(Guid tenantId, Guid storeId, Guid registerId, string registerLabel,
        Guid openedBy, string openedByName, Money openingFloat)
    {
        if (openingFloat.Amount < 0) throw new ArgumentException("Opening float cannot be negative.", nameof(openingFloat));
        return new RegisterSession
        {
            Id = Uuid7.NewGuid(),
            TenantId = tenantId,
            StoreId = storeId,
            RegisterId = registerId,
            RegisterLabel = registerLabel ?? string.Empty,
            OpenedBy = openedBy,
            OpenedByName = openedByName ?? string.Empty,
            OpenedAtUtc = DateTimeOffset.UtcNow,
            OpeningFloat = new Money(openingFloat.Amount, openingFloat.Currency),
            Status = SessionStatus.Open,
        };
    }

    /// <summary>
    /// Close the shift: freeze the counted cash, the expected cash (computed from the session's facts by
    /// the caller) and the variance. Idempotent guard: only an Open session can be closed.
    /// </summary>
    public void Close(Guid closedBy, string closedByName, Money countedCash, Money expectedCash, bool varianceAcknowledged)
    {
        if (Status != SessionStatus.Open)
            throw new InvalidOperationException("Only an open session can be closed.");
        if (countedCash.Amount < 0) throw new ArgumentException("Counted cash cannot be negative.", nameof(countedCash));

        ClosedBy = closedBy;
        ClosedByName = closedByName ?? string.Empty;
        ClosedAtUtc = DateTimeOffset.UtcNow;
        CountedCash = new Money(countedCash.Amount, countedCash.Currency);
        ExpectedCash = new Money(expectedCash.Amount, expectedCash.Currency);
        Variance = countedCash.Subtract(expectedCash);
        VarianceAcknowledged = varianceAcknowledged;
        Status = SessionStatus.Closed;

        Raise(new RegisterSessionClosed(Id, TenantId, StoreId, RegisterId,
            ExpectedCash.Amount, CountedCash.Amount, Variance.Amount, Currency: OpeningFloat.Currency));
    }
}
