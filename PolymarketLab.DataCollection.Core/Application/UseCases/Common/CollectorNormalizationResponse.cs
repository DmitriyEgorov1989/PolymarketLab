namespace PolymarketLab.DataCollection.Core.Application.UseCases.Common;

/// <summary>Текущий снимок нормализации snapshot-версии для HTTP-ответа.</summary>
/// <param name="RawCount">Количество raw-сообщений сессии.</param>
/// <param name="LedgerCount">Количество ledger rows snapshot-версии.</param>
/// <param name="ProcessedCount">Количество ledger rows со статусом <c>Processed</c>.</param>
/// <param name="PendingCount">Количество ledger rows со статусом <c>Pending</c>.</param>
/// <param name="ProcessingCount">Количество ledger rows со статусом <c>Processing</c>.</param>
/// <param name="UnsupportedCount">Количество ledger rows со статусом <c>Unsupported</c>.</param>
/// <param name="InvalidCount">Количество ledger rows со статусом <c>Invalid</c>.</param>
/// <param name="FailedCount">Количество ledger rows со статусом <c>Failed</c>.</param>
/// <param name="MissingCount">Количество raw-сообщений без ledger row snapshot-версии.</param>
/// <param name="ResolutionRawItemProcessed">
/// <see langword="true" />, если strict WebSocket resolution observation ссылается
/// на обработанный parent raw и normalized <c>market_resolved</c> item этой версии.
/// </param>
public sealed record CollectorNormalizationResponse(
    long RawCount,
    long LedgerCount,
    long ProcessedCount,
    long PendingCount,
    long ProcessingCount,
    long UnsupportedCount,
    long InvalidCount,
    long FailedCount,
    long MissingCount,
    bool ResolutionRawItemProcessed);
