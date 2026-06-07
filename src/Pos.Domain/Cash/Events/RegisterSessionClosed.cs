using Pos.SharedKernel;

namespace Pos.Domain.Cash.Events;

/// <summary>End-of-day fact: a register shift closed with its cash-up result. The seam HQ/cloud sync
/// (M1) reads from the outbox to roll branch takings up.</summary>
public sealed record RegisterSessionClosed(
    Guid SessionId,
    Guid TenantId,
    Guid StoreId,
    Guid RegisterId,
    decimal ExpectedCash,
    decimal CountedCash,
    decimal Variance,
    string Currency) : DomainEvent;
