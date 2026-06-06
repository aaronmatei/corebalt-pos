namespace Pos.Infrastructure.Outbox;

public interface IOutboxDispatcher
{
    Task<int> DrainAsync(int batchSize = 100, CancellationToken ct = default);
}
