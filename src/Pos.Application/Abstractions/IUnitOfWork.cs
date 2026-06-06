namespace Pos.Application.Abstractions;

/// <summary>
/// Transactional boundary. One SaveChangesAsync atomically commits the aggregate change
/// AND the outbox rows that mirror its domain events — that's what makes the
/// at-least-once HQ sync safe from "saved but never published" gaps.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Run <paramref name="work"/> (which typically increments the receipt sequence then SaveChanges)
    /// inside a single database transaction, so the receipt-number increment commits atomically with
    /// the sale + stock movements + outbox rows.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);

    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default);
}
