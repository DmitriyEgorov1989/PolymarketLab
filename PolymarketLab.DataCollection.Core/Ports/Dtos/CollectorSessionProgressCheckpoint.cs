using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Снимок runtime-счётчиков для устойчивого обновления прогресса.</summary>
/// <param name="SessionId">Идентификатор сессии.</param>
/// <param name="CurrentConnectionEpoch">Текущая эпоха подключения; 0 означает, что подключение ещё не установлено.</param>
/// <param name="MessagesReceived">Количество полностью полученных сообщений.</param>
/// <param name="MessagesEnqueued">Количество сообщений, успешно переданных в bounded ingestion.</param>
/// <param name="MessagesPersisted">Количество сообщений, подтверждённых PostgreSQL.</param>
/// <param name="LastMessageAt">Момент получения последнего сообщения.</param>
/// <param name="ReconnectCount">Количество повторных подключений.</param>
public sealed record CollectorSessionProgressCheckpoint(
    CollectorSessionId SessionId,
    long CurrentConnectionEpoch,
    long MessagesReceived,
    long MessagesEnqueued,
    long MessagesPersisted,
    DateTimeOffset? LastMessageAt,
    long ReconnectCount);
