namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Запрос атомарной записи результата нормализации исходного сообщения.</summary>
/// <param name="Claim">Принадлежащий обработчику захват исходного сообщения.</param>
/// <param name="Completion">Терминальный результат нормализации сообщения.</param>
public sealed record NormalizationWriteRequest(
    ClaimedRawMessage Claim,
    NormalizationCompletion Completion);
