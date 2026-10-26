using System.Collections.Immutable;
using PolymarketLab.DataCollection.Core.Application.Simulation.Execution;
using PolymarketLab.DataCollection.Core.Application.Simulation.ExternalData;
using PolymarketLab.DataCollection.Core.Application.Simulation.Rules;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.RunExperiment;

/// <summary>Ручное либо заранее детерминированное действие без чтения будущих данных.</summary>
/// <param name="At">Виртуальное время отправки.</param>
/// <param name="Order">Немедленная заявка с задержкой.</param>
public sealed record ScheduledOrder(DateTimeOffset At, SimulatedOrder Order);

/// <summary>Полный явный сценарий; все его внешние данные имеют собственную доступность.</summary>
/// <param name="Version">Поддерживаемая версия временной и финансовой семантики; сейчас 1.</param>
/// <param name="InitialCash">Начальные свободные деньги; начальных токенов нет.</param>
/// <param name="EpisodeEndsAt">Горизонт ожидания, не раньше конца рыночного окна.</param>
/// <param name="Orders">Заранее заданные действия, не исполняемые до своего времени.</param>
/// <param name="Rules">Явно экспериментальные профили; пусто означает неизвестные правила.</param>
/// <param name="Btc">Внешний ряд с доказанным или явно экспериментальным временем доступности.</param>
/// <param name="MaximumBtcAge">Допустимый возраст; null разрешён только при пустом BTC-ряде.</param>
/// <param name="RequireFreshBtc">Блокировать ли отправку и исполнение без свежего BTC.</param>
/// <param name="Settlement">Отдельные свидетельства исхода.</param>
/// <param name="SettlementPolicy">Достаточные источники и выплата либо null, запрещающий расчёт.</param>
public sealed record ExperimentDefinition(int Version, decimal InitialCash, DateTimeOffset EpisodeEndsAt,
    ImmutableArray<ScheduledOrder> Orders, ImmutableArray<TradingRulesProfile> Rules,
    ImmutableArray<BtcObservation> Btc, TimeSpan? MaximumBtcAge, bool RequireFreshBtc,
    ImmutableArray<SettlementEvidence> Settlement, SettlementPolicy? SettlementPolicy);
