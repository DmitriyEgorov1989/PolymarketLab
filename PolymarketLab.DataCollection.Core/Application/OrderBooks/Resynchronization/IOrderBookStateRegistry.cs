using System.Diagnostics.CodeAnalysis;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization;

/// <summary>Хранит текущие состояния стаканов по идентификатору актива.</summary>
public interface IOrderBookStateRegistry
{
    /// <summary>Возвращает существующее состояние актива или создаёт неинициализированное.</summary>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <returns>Единственный зарегистрированный экземпляр состояния актива.</returns>
    OrderBookState GetOrAdd(string assetId);

    /// <summary>Пытается найти ранее зарегистрированное состояние актива.</summary>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="state">Найденное состояние или <see langword="null" />.</param>
    /// <returns><see langword="true" />, если состояние зарегистрировано.</returns>
    bool TryGet(
        string assetId,
        [NotNullWhen(true)] out OrderBookState? state);
}
