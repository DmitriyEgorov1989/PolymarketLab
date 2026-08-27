using CSharpFunctionalExtensions;
using PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Ports;

/// <summary>
///     Сохраняет и разрешает полностью материализованные aggregate зарегистрированных рынков.
/// </summary>
public interface IMarketRepository
{
    /// <summary>Возвращает все зарегистрированные рынки.</summary>
    Task<IReadOnlyCollection<Market>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Возвращает рынок по локальному идентификатору либо <see langword="null"/>, если рынок отсутствует.</summary>
    Task<Market?> GetByIdAsync(MarketId marketId, CancellationToken cancellationToken);

    /// <summary>Возвращает рынок по slug события либо <see langword="null"/>, если рынок отсутствует.</summary>
    Task<Market?> GetByEventSlugAsync(EventSlug eventSlug, CancellationToken cancellationToken);

    /// <summary>Возвращает рынок по внешнему идентификатору события либо <see langword="null"/>, если рынок отсутствует.</summary>
    Task<Market?> GetByExternalEventIdAsync(ExternalEventId externalEventId, CancellationToken cancellationToken);

    /// <summary>Возвращает рынок по slug дочернего рынка либо <see langword="null"/>, если рынок отсутствует.</summary>
    Task<Market?> GetBySlugAsync(MarketSlug slug, CancellationToken cancellationToken);

    /// <summary>Возвращает рынок по внешнему идентификатору дочернего рынка либо <see langword="null"/>, если рынок отсутствует.</summary>
    Task<Market?> GetByExternalIdAsync(ExternalMarketId externalMarketId, CancellationToken cancellationToken);

    /// <summary>Возвращает рынок по идентификатору condition либо <see langword="null"/>, если рынок отсутствует.</summary>
    Task<Market?> GetByConditionIdAsync(ConditionId conditionId, CancellationToken cancellationToken);

    /// <summary>Возвращает рынки, содержащие хотя бы один из указанных внешних идентификаторов tokens.</summary>
    Task<IReadOnlyCollection<Market>> GetByAnyTokenIdsAsync(
        IReadOnlyCollection<TokenId> tokenIds,
        CancellationToken cancellationToken);

    /// <summary>Пытается добавить новый рынок с сохранением известных semantics конфликтов identity.</summary>
    Task<Result<MarketInsertStatus, Error>> TryAddAsync(Market market, CancellationToken cancellationToken);

    /// <summary>Сохраняет только изменяемые наблюдения расписания существующего рынка.</summary>
    Task<UnitResult<Error>> UpdateScheduleAsync(Market market, CancellationToken cancellationToken);
}
