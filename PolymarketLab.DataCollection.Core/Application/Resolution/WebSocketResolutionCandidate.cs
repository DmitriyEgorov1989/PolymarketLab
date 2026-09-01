namespace PolymarketLab.DataCollection.Core.Application.Resolution;

/// <summary>Кандидат resolution, выделенный из устойчиво сохранённого WebSocket raw message.</summary>
/// <param name="RawMessageId">Идентификатор исходного сообщения в PostgreSQL.</param>
/// <param name="RawItemIndex">Позиция объекта в корневом JSON-массиве либо ноль для корневого объекта.</param>
/// <param name="ConnectionEpoch">Эпоха WebSocket-соединения исходного сообщения.</param>
/// <param name="ReceivedAt">Локальное UTC-время получения исходного сообщения.</param>
/// <param name="ExternalMarketId">Внешний идентификатор рынка из сообщения.</param>
/// <param name="ConditionId">Condition id из сообщения.</param>
/// <param name="AssetIds">Полный набор token ids из сообщения.</param>
/// <param name="WinningAssetId">Идентификатор выигравшего токена.</param>
/// <param name="WinningOutcome">Название выигравшего исхода.</param>
public sealed record WebSocketResolutionCandidate(
    long RawMessageId,
    int RawItemIndex,
    long ConnectionEpoch,
    DateTimeOffset ReceivedAt,
    string? ExternalMarketId,
    string? ConditionId,
    IReadOnlyCollection<string>? AssetIds,
    string? WinningAssetId,
    string? WinningOutcome);
