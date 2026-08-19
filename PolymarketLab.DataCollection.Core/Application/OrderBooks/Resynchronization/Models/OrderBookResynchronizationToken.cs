using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization.Models;

/// <summary>Идентифицирует активную операцию и ожидаемую версию состояния.</summary>
/// <param name="OperationId">Монотонный идентификатор операции для защиты от позднего завершения.</param>
/// <param name="ExpectedVersion">Версия состояния, при которой разрешена публикация снимка.</param>
/// <param name="InitialStatus">Статус до начала операции, восстанавливаемый при ошибке ручной проверки.</param>
/// <param name="InitialIntegrityIssue">Диагностика до начала операции или <see langword="null" />.</param>
internal readonly record struct OrderBookResynchronizationToken(
    long OperationId,
    long ExpectedVersion,
    OrderBookSyncStatus InitialStatus,
    OrderBookIntegrityIssue? InitialIntegrityIssue);
