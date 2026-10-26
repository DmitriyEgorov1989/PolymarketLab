using System.Collections.Immutable;
using PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;
using PolymarketLab.DataCollection.Core.Application.Simulation.Execution;
using PolymarketLab.DataCollection.Core.Application.Simulation.ExternalData;
using PolymarketLab.DataCollection.Core.Application.Simulation.RunExperiment;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.Reporting;

/// <summary>Неисполняющая ликвидационная оценка; неизвестный остаток не оценивается фиктивной ценой.</summary>
/// <param name="AssetId">Токен позиции.</param>
/// <param name="QuotedQuantity">Количество, для которого есть bids и правила.</param>
/// <param name="UnknownQuantity">Неоценённые токены.</param>
/// <param name="QuotedNetProceeds">Денежная выручка после комиссии только для оценённой части.</param>
/// <param name="EstimatedPnl">Результат оценённой части после себестоимости; null при отсутствии оценки.</param>
public sealed record PositionValuation(string AssetId, decimal QuotedQuantity, decimal UnknownQuantity,
    decimal QuotedNetProceeds, decimal? EstimatedPnl);

/// <summary>Итог одного детерминированного запуска; системное время и длительность не включены.</summary>
/// <param name="SessionId">Выбранная сессия.</param>
/// <param name="ProjectionVersion">Версия проекции.</param>
/// <param name="EventStartsAt">Начало торговли.</param>
/// <param name="EventEndsAt">Конец торговли.</param>
/// <param name="Manifest">Авторитетный паспорт #57; null только для синтетического входа.</param>
/// <param name="ReplayFingerprint">Отпечаток адаптированного входа причинного движка.</param>
/// <param name="Definition">Зафиксированные настройки, сценарий и внешние входы; доступны только в итоговом отчёте.</param>
/// <param name="DefinitionFingerprint">Версия и отпечаток настроек, сценария и внешних входов.</param>
/// <param name="JournalFingerprint">Отпечаток упорядоченного журнала.</param>
/// <param name="Balance">Финальные деньги и позиции.</param>
/// <param name="Valuations">Оценки оставшихся позиций без искусственных продаж.</param>
/// <param name="SettlementStatus">Unconfirmed, Confirmed либо Conflicting.</param>
/// <param name="SettlementObservedAt">Время достаточного подтверждения либо null.</param>
/// <param name="SettlementAppliedAt">Время моделируемой выплаты либо null.</param>
/// <param name="FinalBtc">Финальное доступное состояние внешнего ряда.</param>
/// <param name="DataIssues">Обнаруженные коды проблем качества, без raw payload.</param>
/// <param name="Assumptions">Явные ограничения модели.</param>
/// <param name="Journal">Канонический журнал.</param>
public sealed record ExperimentReport(Guid SessionId, int ProjectionVersion, DateTimeOffset EventStartsAt,
    DateTimeOffset EventEndsAt, ExperimentDatasetManifest? Manifest, ReplayDatasetFingerprint ReplayFingerprint,
    ExperimentDefinition Definition, string DefinitionFingerprint,
    string JournalFingerprint, AccountBalance Balance, ImmutableArray<PositionValuation> Valuations,
    string SettlementStatus, DateTimeOffset? SettlementObservedAt, DateTimeOffset? SettlementAppliedAt,
    BtcDataState FinalBtc, ImmutableArray<string> DataIssues, ImmutableArray<string> Assumptions,
    ImmutableArray<SimulationJournalEntry> Journal);
