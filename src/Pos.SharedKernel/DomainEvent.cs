using Pos.SharedKernel.Ids;

namespace Pos.SharedKernel;

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Uuid7.NewGuid();
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
