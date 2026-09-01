using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Последовательно читает кандидатов resolution из зафиксированных raw WebSocket-сообщений.</summary>
public interface IWebSocketResolutionCandidateSource
{
    /// <summary>Просматривает следующий ограниченный batch сообщений после заданного cursor.</summary>
    /// <param name="sessionId">Идентификатор целевой сессии.</param>
    /// <param name="afterRawMessageId">Последний ранее просмотренный raw message id.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Новый high-water mark и найденные кандидаты.</returns>
    Task<WebSocketResolutionScanResult> ScanAsync(
        CollectorSessionId sessionId,
        long afterRawMessageId,
        CancellationToken cancellationToken);
}
