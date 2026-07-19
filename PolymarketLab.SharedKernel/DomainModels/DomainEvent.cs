using MediatR;

namespace PolymarketLab.SharedKernel.DomainModels;

public abstract record DomainEvent : INotification
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
