namespace PolymarketLab.DataCollection.Core.Application.UseCases.Common;

/// <summary>Готовность подписки текущей connection epoch для HTTP-ответа.</summary>
/// <param name="ConnectionEpoch">Текущая сохранённая connection epoch.</param>
/// <param name="Tokens">Готовность каждого snapshot-токена текущей epoch.</param>
public sealed record CollectorReadinessResponse(
    long ConnectionEpoch,
    IReadOnlyList<CollectorTokenReadinessResponse> Tokens);

/// <summary>Durable-готовность одного токена текущей connection epoch.</summary>
/// <param name="TokenId">Внешний идентификатор токена.</param>
/// <param name="InitialBookEnqueuedAt">
/// Момент успешной постановки initial book в bounded ingestion текущей epoch;
/// <see langword="null" />, если durable observation текущей epoch отсутствует.
/// </param>
public sealed record CollectorTokenReadinessResponse(
    string TokenId,
    DateTimeOffset? InitialBookEnqueuedAt);
