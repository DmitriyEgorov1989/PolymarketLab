using System.Collections.Immutable;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.ExternalData;

/// <summary>Качество внешней цены, известное в текущий момент.</summary>
public enum BtcDataStatus
{
    /// <summary>Наблюдения отсутствуют.</summary>
    Missing,
    /// <summary>Известные время и цена укладываются в явно заданный срок.</summary>
    Fresh,
    /// <summary>Наблюдение устарело.</summary>
    Stale,
    /// <summary>Источник явно сообщил разрыв.</summary>
    Gap,
    /// <summary>Время источника отсутствует, свежесть не доказана.</summary>
    UnknownSourceTime,
    /// <summary>Время источника находится впереди локальной доступности.</summary>
    ClockSkew
}

/// <summary>Внешнее наблюдение либо явный разрыв; не определяет победителя рынка.</summary>
/// <param name="Sequence">Уникальная последовательность входного набора.</param>
/// <param name="Source">Поставщик данных.</param>
/// <param name="Pair">Пара с единицами цены, например BTC/USD.</param>
/// <param name="SourceAt">Время источника либо null при отсутствии знания.</param>
/// <param name="AvailableAt">Доказанное или явно экспериментальное время доступности.</param>
/// <param name="Price">Цена либо null для разрыва.</param>
/// <param name="IsGap">Явный разрыв; требует null цены.</param>
public sealed record BtcObservation(long Sequence, string Source, string Pair,
    DateTimeOffset? SourceAt, DateTimeOffset AvailableAt, decimal? Price, bool IsGap = false);

/// <summary>Доступное состояние BTC без будущей точки интерполяции.</summary>
/// <param name="Status">Качество текущего наблюдения.</param>
/// <param name="Observation">Последнее доступное наблюдение либо null.</param>
public sealed record BtcDataState(BtcDataStatus Status, BtcObservation? Observation);

/// <summary>Причинное чтение уже полученного внешнего ряда.</summary>
public static class BtcAvailabilityTimeline
{
    /// <summary>Оценивает ряд по получению и времени источника, без заглядывания вперёд.</summary>
    public static BtcDataState Observe(ImmutableArray<BtcObservation> observations, DateTimeOffset at, TimeSpan? maximumAge)
    {
        var latest = observations.Where(o => o.AvailableAt <= at)
            .OrderBy(o => o.AvailableAt).ThenBy(o => o.Sequence).LastOrDefault();
        if (latest is null) return new(BtcDataStatus.Missing, null);
        if (latest.IsGap) return new(BtcDataStatus.Gap, latest);
        if (latest.SourceAt is null) return new(BtcDataStatus.UnknownSourceTime, latest);
        if (latest.SourceAt > latest.AvailableAt) return new(BtcDataStatus.ClockSkew, latest);
        return new(maximumAge is null || at - latest.AvailableAt >= maximumAge || at - latest.SourceAt.Value >= maximumAge
            ? BtcDataStatus.Stale : BtcDataStatus.Fresh, latest);
    }
}

/// <summary>Отдельное свидетельство подтверждения исхода, не котировка сигнального BTC.</summary>
/// <param name="Source">Источник подтверждения.</param>
/// <param name="Reference">Устойчивая ссылка или идентификатор evidence.</param>
/// <param name="SessionId">Точная сессия.</param>
/// <param name="ConditionId">Проверенное условие рынка.</param>
/// <param name="WinningAssetId">Подтверждённый токен победителя.</param>
/// <param name="ConfirmedAt">Момент подтверждения, не конец окна.</param>
/// <param name="AvailableAt">Момент доступности подтверждения эксперименту.</param>
public sealed record SettlementEvidence(string Source, string Reference, Guid SessionId,
    string ConditionId, string WinningAssetId, DateTimeOffset ConfirmedAt, DateTimeOffset AvailableAt);

/// <summary>Явно выбранные достаточные источники; расчётные деньги моделируются в конце эпизода.</summary>
/// <param name="RequiredSources">Непустой набор источников, все должны согласиться.</param>
/// <param name="WinnerPayout">Денежная выплата за один выигравший токен.</param>
public sealed record SettlementPolicy(ImmutableArray<string> RequiredSources, decimal WinnerPayout);
