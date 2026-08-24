using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;

/// <summary>Итог применения события и актуальная диагностика состояния.</summary>
/// <param name="Outcome">Результат обработки события проектором.</param>
/// <param name="IntegrityIssue">Актуальное нарушение целостности или <see langword="null" />.</param>
public sealed record OrderBookProjectionResult(
    OrderBookProjectionOutcome Outcome,
    OrderBookIntegrityIssue? IntegrityIssue)
{
    /// <summary>Создаёт результат применённого события.</summary>
    /// <param name="integrityIssue">Актуальное нарушение целостности или <see langword="null" />.</param>
    /// <returns>Результат с исходом <see cref="OrderBookProjectionOutcome.Applied" />.</returns>
    public static OrderBookProjectionResult Applied(OrderBookIntegrityIssue? integrityIssue)
    {
        return new OrderBookProjectionResult(OrderBookProjectionOutcome.Applied, integrityIssue);
    }

    /// <summary>Создаёт результат события, которое не продвинуло состояние.</summary>
    /// <param name="integrityIssue">Сохранённое нарушение целостности или <see langword="null" />.</param>
    /// <returns>Результат с исходом <see cref="OrderBookProjectionOutcome.Ignored" />.</returns>
    public static OrderBookProjectionResult Ignored(OrderBookIntegrityIssue? integrityIssue)
    {
        return new OrderBookProjectionResult(OrderBookProjectionOutcome.Ignored, integrityIssue);
    }
}
