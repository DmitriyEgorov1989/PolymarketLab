using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Сохранённое окно рынка для проверки допустимости запуска.</summary>
/// <param name="MarketId">Внутренний идентификатор рынка.</param>
/// <param name="EventStartsAt">Точное сохранённое UTC-время начала предметного окна.</param>
public sealed record CollectionMarketWindow(
    MarketId MarketId,
    DateTimeOffset EventStartsAt);
