using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Снимок runtime-счётчиков для устойчивого обновления прогресса.</summary>
/// <param name="SessionId">Идентификатор сессии.</param>
/// <param name="MessagesReceived">Количество полностью полученных сообщений.</param>
/// <param name="LastMessageAt">Момент получения последнего сообщения.</param>
/// <param name="ReconnectCount">Количество повторных подключений.</param>
public sealed record CollectorSessionProgressCheckpoint(
    CollectorSessionId SessionId,
    long MessagesReceived,
    DateTimeOffset? LastMessageAt,
    long ReconnectCount);
