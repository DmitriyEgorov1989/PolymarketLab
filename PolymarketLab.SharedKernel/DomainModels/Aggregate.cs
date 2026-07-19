using CSharpFunctionalExtensions;

namespace PolymarketLab.SharedKernel.DomainModels;

public abstract class Aggregate<TId> : Entity<TId>, IAggregateRoot where TId : IComparable<TId>
{
    private readonly List<DomainEvent> _domainEvents = new();

    protected Aggregate(TId id) : base(id)
    {
    }

    protected Aggregate()
    {
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    public IReadOnlyCollection<DomainEvent> GetDomainEvents()
    {
        return _domainEvents.ToList();
    }

    protected void RaiseDomainEvent(DomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }
}

public interface IAggregateRoot
{
    IReadOnlyCollection<DomainEvent> GetDomainEvents();

    void ClearDomainEvents();
}
