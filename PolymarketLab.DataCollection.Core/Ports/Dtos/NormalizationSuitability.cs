namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>
/// Согласованный снимок обработки raw-сообщений одной collector session
/// указанной snapshot-версией нормализации.
/// </summary>
/// <param name="RawCount">Количество raw-сообщений session.</param>
/// <param name="LedgerCount">Количество ledger rows указанной версии для raw-сообщений session.</param>
/// <param name="ProcessedCount">Количество ledger rows со статусом <c>Processed</c>.</param>
/// <param name="PendingCount">Количество ledger rows со статусом <c>Pending</c>.</param>
/// <param name="ProcessingCount">Количество ledger rows со статусом <c>Processing</c>.</param>
/// <param name="UnsupportedCount">Количество ledger rows со статусом <c>Unsupported</c>.</param>
/// <param name="InvalidCount">Количество ledger rows со статусом <c>Invalid</c>.</param>
/// <param name="FailedCount">Количество ledger rows со статусом <c>Failed</c>.</param>
/// <param name="ResolutionRawItemProcessed">
/// <see langword="true" />, если strict WebSocket resolution observation ссылается
/// на обработанный parent raw и normalized <c>market_resolved</c> item этой версии.
/// </param>
public sealed record NormalizationSuitability(
    long RawCount,
    long LedgerCount,
    long ProcessedCount,
    long PendingCount,
    long ProcessingCount,
    long UnsupportedCount,
    long InvalidCount,
    long FailedCount,
    bool ResolutionRawItemProcessed)
{
    /// <summary>Количество raw-сообщений без ledger row указанной версии.</summary>
    public long MissingCount => RawCount - LedgerCount;
}
