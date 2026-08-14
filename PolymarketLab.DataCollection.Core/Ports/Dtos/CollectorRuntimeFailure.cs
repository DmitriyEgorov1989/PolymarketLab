using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Сообщение об автономной ошибке выполняющегося сборщика.</summary>
/// <param name="SessionId">Идентификатор завершившейся сессии.</param>
/// <param name="FailedAt">Момент обнаружения ошибки.</param>
/// <param name="Error">Структурированная ошибка runtime.</param>
public sealed record CollectorRuntimeFailure(
    CollectorSessionId SessionId,
    DateTimeOffset FailedAt,
    Error Error);
