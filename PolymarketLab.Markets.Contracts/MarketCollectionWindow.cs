using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.Markets.Contracts;

/// <summary>Сохранённое предметное окно зарегистрированного рынка без внешнего обновления.</summary>
/// <param name="MarketId">Внутренний идентификатор зарегистрированного рынка.</param>
/// <param name="EventStartsAt">Точное сохранённое UTC-время начала предметного окна.</param>
public sealed record MarketCollectionWindow(
    MarketId MarketId,
    DateTimeOffset EventStartsAt);
