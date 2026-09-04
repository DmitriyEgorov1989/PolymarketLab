namespace PolymarketLab.DataCollection.Core.Application.UseCases.Common;

/// <summary>Безопасное состояние разрешения рынка для HTTP-ответа.</summary>
/// <param name="SignaledAt">Момент подтверждающего WebSocket signal; <see langword="null" />, пока signal не принят.</param>
/// <param name="ConfirmedAt">Момент согласования всех resolution sources; <see langword="null" />, пока consensus не достигнут.</param>
/// <param name="WinningTokenId">Выигравший token id; <see langword="null" />, пока consensus не достигнут.</param>
/// <param name="WinningOutcome">Выигравший outcome; <see langword="null" />, пока consensus не достигнут.</param>
/// <param name="ConnectionEpoch">Connection epoch WebSocket signal; <see langword="null" />, пока consensus не достигнут.</param>
/// <param name="LastPollingCycleAt">Время последнего начатого polling cycle либо <see langword="null" />.</param>
/// <param name="SourceStates">Последнее observation каждого источника, отсортированное WebSocket, Gamma, Clob.</param>
/// <param name="ConfirmationSources">Exact terminal evidence состоявшегося consensus; пусто до подтверждения.</param>
public sealed record CollectorResolutionResponse(
    DateTimeOffset? SignaledAt,
    DateTimeOffset? ConfirmedAt,
    string? WinningTokenId,
    string? WinningOutcome,
    long? ConnectionEpoch,
    DateTimeOffset? LastPollingCycleAt,
    IReadOnlyList<CollectorResolutionSourceResponse> SourceStates,
    IReadOnlyList<CollectorResolutionSourceResponse> ConfirmationSources);

/// <summary>Безопасное resolution observation одного источника для HTTP-ответа.</summary>
/// <param name="Source">Строковое имя источника: <c>WebSocket</c>, <c>Gamma</c> или <c>Clob</c>.</param>
/// <param name="Status">Строковое имя проверенного статуса observation.</param>
/// <param name="ObservedAt">Локальное UTC-время observation.</param>
/// <param name="WinningTokenId">Проверенный выигравший token id либо <see langword="null" />.</param>
/// <param name="WinningOutcome">Проверенный выигравший outcome либо <see langword="null" />.</param>
/// <param name="ErrorCode">Безопасный код ошибки либо <see langword="null" />.</param>
/// <param name="ErrorMessage">Безопасное сообщение ошибки либо <see langword="null" />.</param>
public sealed record CollectorResolutionSourceResponse(
    string Source,
    string Status,
    DateTimeOffset ObservedAt,
    string? WinningTokenId,
    string? WinningOutcome,
    string? ErrorCode,
    string? ErrorMessage);
