namespace Pos.Application.Abstractions;

/// <summary>
/// Transactional boundary. One SaveChangesAsync atomically commits the aggregate change
/// AND the outbox rows that mirror its domain events — that's what makes the
/// at-least-once HQ sync safe from "saved but never published" gaps.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
