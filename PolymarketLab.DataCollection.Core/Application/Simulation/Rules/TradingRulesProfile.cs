using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.Rules;

/// <summary>Явно экспериментальная комиссия в денежной валюте, не реконструкция исторической формулы Polymarket.</summary>
public sealed record TradingRulesProfile
{
    private TradingRulesProfile(string id, string source, DateTimeOffset availableAt, DateTimeOffset effectiveAt,
        decimal minimumQuantity, decimal quantityQuantum, decimal moneyQuantum, decimal feeBps)
    {
        Id = id; Source = source; AvailableAt = availableAt; EffectiveAt = effectiveAt;
        MinimumQuantity = minimumQuantity; QuantityQuantum = quantityQuantum;
        MoneyQuantum = moneyQuantum; FeeBps = feeBps;
    }

    /// <summary>Идентификатор версии профиля.</summary>
    public string Id { get; }
    /// <summary>Происхождение допущения; не пустое значение.</summary>
    public string Source { get; }
    /// <summary>Время доступности профиля.</summary>
    public DateTimeOffset AvailableAt { get; }
    /// <summary>Включительное время применения.</summary>
    public DateTimeOffset EffectiveAt { get; }
    /// <summary>Минимальный запрашиваемый объём; частичный fill может быть меньше.</summary>
    public decimal MinimumQuantity { get; }
    /// <summary>Квант количества токенов.</summary>
    public decimal QuantityQuantum { get; }
    /// <summary>Квант денежных списаний, зачислений и комиссий.</summary>
    public decimal MoneyQuantum { get; }
    /// <summary>Комиссия от notional в базисных пунктах, удерживаемая деньгами.</summary>
    public decimal FeeBps { get; }

    /// <summary>Создаёт экспериментальный профиль; неизвестные значения не подставляются.</summary>
    public static Result<TradingRulesProfile, Error> CreateExperimental(string id, string source,
        DateTimeOffset availableAt, DateTimeOffset effectiveAt, decimal minimumQuantity,
        decimal quantityQuantum, decimal moneyQuantum, decimal feeBps)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(source)
            || availableAt == default || effectiveAt == default || minimumQuantity <= 0
            || quantityQuantum <= 0 || moneyQuantum <= 0 || minimumQuantity % quantityQuantum != 0
            || feeBps is < 0 or > 10000)
            return SimulationErrors.Invalid("rules", "An explicit experimental fee/size/rounding profile is required.");
        return new TradingRulesProfile(id, source, availableAt.ToUniversalTime(), effectiveAt.ToUniversalTime(),
            minimumQuantity, quantityQuantum, moneyQuantum, feeBps);
    }

    internal decimal Fee(decimal notional) => notional * (FeeBps / 10000m);
    internal decimal BuyReserve(decimal quantity, decimal limit) => checked(
        Quantization.Ceiling(quantity * limit, MoneyQuantum)
        + Quantization.Ceiling(Fee(quantity * limit), MoneyQuantum));
}

/// <summary>Направленное округление неотрицательных финансовых величин.</summary>
public static class Quantization
{
    /// <summary>Округляет вниз к положительному кванту.</summary>
    public static decimal Floor(decimal value, decimal quantum) => decimal.Floor(value / quantum) * quantum;
    /// <summary>Округляет вверх к положительному кванту.</summary>
    public static decimal Ceiling(decimal value, decimal quantum) => decimal.Ceiling(value / quantum) * quantum;
}
