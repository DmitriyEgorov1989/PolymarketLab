using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization;

/// <summary>Потокобезопасный in-memory реестр состояний стаканов.</summary>
/// <param name="timeProvider">Источник времени для создаваемых состояний.</param>
public sealed class OrderBookStateRegistry(TimeProvider timeProvider) : IOrderBookStateRegistry
{
    private readonly ConcurrentDictionary<string, OrderBookState> _states =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public OrderBookState GetOrAdd(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        return _states.GetOrAdd(
            assetId,
            static (id, clock) => new OrderBookState(id, clock),
            timeProvider);
    }

    /// <inheritdoc />
    public bool TryGet(
        string assetId,
        [NotNullWhen(true)] out OrderBookState? state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        return _states.TryGetValue(assetId, out state);
    }
}
