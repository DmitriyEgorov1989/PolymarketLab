using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization.Models;

/// <summary>Результат восстановления состояния стакана.</summary>
public sealed record OrderBookResyncResult
{
    private OrderBookResyncResult(
        string assetId,
        OrderBookResyncReason reason,
        OrderBookResyncOutcome outcome,
        int attempts,
        OrderBookSnapshot? snapshot,
        Error? error)
    {
        AssetId = assetId;
        Reason = reason;
        Outcome = outcome;
        Attempts = attempts;
        Snapshot = snapshot;
        Error = error;
    }

    /// <summary>Идентификатор актива, для которого выполнялось восстановление.</summary>
    public string AssetId { get; }

    /// <summary>Диагностическая причина запуска восстановления.</summary>
    public OrderBookResyncReason Reason { get; }

    /// <summary>Итог операции восстановления.</summary>
    public OrderBookResyncOutcome Outcome { get; }

    /// <summary>Количество выполненных запросов полного снимка.</summary>
    public int Attempts { get; }

    /// <summary>Опубликованный снимок или <see langword="null" /> при неуспехе.</summary>
    public OrderBookSnapshot? Snapshot { get; }

    /// <summary>Ошибка операции или <see langword="null" /> при успешной синхронизации.</summary>
    public Error? Error { get; }

    /// <summary>Создаёт результат успешной синхронизации.</summary>
    /// <param name="assetId">Идентификатор восстановленного актива.</param>
    /// <param name="reason">Причина запуска восстановления.</param>
    /// <param name="attempts">Количество выполненных запросов снимка.</param>
    /// <param name="snapshot">Проверенный и опубликованный полный снимок.</param>
    /// <returns>Успешный результат без ошибки.</returns>
    public static OrderBookResyncResult Synchronized(
        string assetId,
        OrderBookResyncReason reason,
        int attempts,
        OrderBookSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(assetId, reason, OrderBookResyncOutcome.Synchronized, attempts, snapshot, null);
    }

    /// <summary>Создаёт результат неуспешного восстановления.</summary>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="reason">Причина запуска восстановления.</param>
    /// <param name="attempts">Количество выполненных запросов снимка.</param>
    /// <param name="error">Диагностированная ошибка операции.</param>
    /// <returns>Неуспешный результат без опубликованного снимка.</returns>
    public static OrderBookResyncResult Failed(
        string assetId,
        OrderBookResyncReason reason,
        int attempts,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(assetId, reason, OrderBookResyncOutcome.Failed, attempts, null, error);
    }
}
