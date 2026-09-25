namespace SBQR.SharedKernel.Domain;

/// <summary>
/// Base class for DDD aggregate roots. Aggregates are the consistency
/// boundary; they own their entities and value objects, and they emit
/// domain events when something interesting happens to them.
/// </summary>
/// <typeparam name="TId">Strongly-typed identifier.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = new();

    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    /// <summary>
    /// Domain events raised by this aggregate since the last
    /// <see cref="ClearDomainEvents"/> call. The hosting infrastructure
    /// is responsible for dispatching these (in the same transaction,
    /// or via an outbox).
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
