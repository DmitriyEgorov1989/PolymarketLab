namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Подтверждённое расписание комиссий нового рынка.</summary>
/// <param name="Exponent">Экспонента формулы комиссии.</param>
/// <param name="Rate">Ставка комиссии.</param>
/// <param name="RebateRate">Ставка возврата комиссии.</param>
/// <param name="TakerOnly">Признак применения комиссии только к тейкеру.</param>
public sealed record NewMarketFeeSchedule(
    decimal Exponent,
    decimal Rate,
    decimal RebateRate,
    bool TakerOnly);
