using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Устойчивый прогресс обработки сообщений сессии.</summary>
/// <param name="SessionId">Идентификатор сессии.</param>
/// <param name="MessagesReceived">Количество полностью полученных сообщений.</param>
/// <param name="MessagesPersisted">Количество подтверждённых PostgreSQL сообщений.</param>
/// <param name="LastMessageAt">Момент получения последнего сообщения.</param>
/// <param name="ReconnectCount">Количество повторных подключений.</param>
public sealed record CollectorSessionProgress(
    CollectorSessionId SessionId,
    long MessagesReceived,
    long MessagesPersisted,
    DateTimeOffset? LastMessageAt,
    long ReconnectCount)
{
    /// <summary>Создаёт пустой прогресс новой сессии.</summary>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <returns>Прогресс с нулевыми счётчиками.</returns>
    public static CollectorSessionProgress Empty(CollectorSessionId sessionId) => new(
        sessionId,
        0,
        0,
        null,
        0);
}
