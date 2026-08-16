namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

/// <summary>Диагностика нарушения целостности текущего состояния стакана.</summary>
public sealed record OrderBookIntegrityIssue(
    OrderBookIntegrityIssueType Type,
    string Message,
    long? NormalizedEventId,
    DateTimeOffset DetectedAt);
