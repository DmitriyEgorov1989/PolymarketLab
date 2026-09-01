using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Исходное WebSocket-сообщение для устойчивого сохранения.</summary>
/// <param name="SessionId">Идентификатор сессии, получившей сообщение.</param>
/// <param name="ConnectionEpoch">Эпоха подключения, в которой полностью получено сообщение; начинается с 1.</param>
/// <param name="ReceivedAt">Момент полного получения сообщения.</param>
/// <param name="Payload">Исходные UTF-8 bytes сообщения.</param>
public sealed record RawMarketMessage(
    CollectorSessionId SessionId,
    long ConnectionEpoch,
    DateTimeOffset ReceivedAt,
    byte[] Payload);
