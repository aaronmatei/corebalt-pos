namespace Pos.SharedKernel;

/// <summary>
/// Aggregate root: the consistency boundary that also records domain events.
/// Events are later drained to an outbox (step 2) and shipped to HQ during sync.
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent e) => _domainEvents.Add(e);
    public void ClearDomainEvents() => _domainEvents.Clear();

    protected AggregateRoot() { }
    protected AggregateRoot(Guid id) : base(id) { }
}
