namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

/// <summary>Диагностика нарушения целостности текущего состояния стакана.</summary>
/// <param name="Type">Классифицированный тип нарушения.</param>
/// <param name="Message">Диагностическое описание обнаруженного расхождения.</param>
/// <param name="NormalizedEventId">Связанное нормализованное событие или <see langword="null" />.</param>
/// <param name="DetectedAt">Момент обнаружения нарушения.</param>
public sealed record OrderBookIntegrityIssue(
    OrderBookIntegrityIssueType Type,
    string Message,
    long? NormalizedEventId,
    DateTimeOffset DetectedAt);
