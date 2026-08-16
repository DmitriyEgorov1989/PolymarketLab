namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

/// <summary>Тип нарушения целостности текущего состояния стакана.</summary>
public enum OrderBookIntegrityIssueType
{
    BestBidMismatch = 1,
    BestAskMismatch = 2,
    SpreadMismatch = 3,
    TickSizeMismatch = 4,
    CrossedBook = 5,
    UnexpectedAsset = 6,
    EventOrderViolation = 7,
    GapDetected = 8,
    SnapshotHashMismatch = 9
}
