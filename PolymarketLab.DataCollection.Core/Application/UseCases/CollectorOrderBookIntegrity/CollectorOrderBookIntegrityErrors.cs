using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorOrderBookIntegrity;

/// <summary>Безопасные ошибки terminal-проверки целостности стаканов.</summary>
internal static class CollectorOrderBookIntegrityErrors
{
    /// <summary>В сохранённой последовательности обнаружено нарушение целостности.</summary>
    public static Error IntegrityIssue(
        CollectorSessionId sessionId,
        string assetId,
        OrderBookIntegrityIssue issue) => new(
        "collector.order_book.integrity.issue",
        $"Collector session '{sessionId.Value}' order book for asset '{assetId}' " +
        $"has integrity issue '{issue.Type}'.",
        ErrorType.Failure);

    /// <summary>Для snapshot token отсутствует committed полный снимок стакана.</summary>
    public static Error MissingSnapshot(
        CollectorSessionId sessionId,
        string assetId) => new(
        "collector.order_book.integrity.issue",
        $"Collector session '{sessionId.Value}' order book for asset '{assetId}' " +
        "has no committed initial snapshot.",
        ErrorType.Failure);

    /// <summary>Сохранённое событие относится к token вне immutable session snapshot.</summary>
    public static Error UnexpectedAsset(
        CollectorSessionId sessionId,
        string assetId) => new(
        "collector.order_book.integrity.issue",
        $"Collector session '{sessionId.Value}' has an order book event for " +
        $"unexpected asset '{assetId}'.",
        ErrorType.Failure);

    /// <summary>Сохранённую последовательность не удалось безопасно прочитать или воспроизвести.</summary>
    public static Error ReadFailed(CollectorSessionId sessionId) => new(
        "collector.order_book.integrity.read_failed",
        $"Collector session '{sessionId.Value}' order book integrity could not be evaluated.",
        ErrorType.Failure);
}
