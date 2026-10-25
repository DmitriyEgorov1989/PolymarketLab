using System.Collections.Immutable;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;

/// <summary>Доказанность отдельного источника сведений, не смешанная с качеством архива.</summary>
public enum DatasetKnowledge
{
    /// <summary>Сведения отсутствуют.</summary>
    Missing,
    /// <summary>Есть закреплённая внешняя ссылка, содержимое ещё не проверено.</summary>
    Declared,
    /// <summary>Сохранённые сведения проверены в рамках соответствующей проверки.</summary>
    Available,
    /// <summary>Сохранённые сведения противоречат друг другу или неполны.</summary>
    Conflicting
}

/// <summary>Отчёт об архиве; не является разрешением причинной торговли или расчёта позиции.</summary>
/// <param name="MetadataComplete">Полны ли обязательные реквизиты и упорядоченные исходы.</param>
/// <param name="NormalizationComplete">Обработано ли каждое исходное сообщение выбранной версией.</param>
/// <param name="ProjectionConsistent">Согласованы ли принадлежность и структура типизированных строк.</param>
/// <param name="TerminalOrderBookIntegrity">Результат терминальной проверки; <see langword="null" />, если она не выполнялась.</param>
/// <param name="Resolution">Доступность подтверждённого исхода.</param>
/// <param name="Fees">Доступность архивных сведений о комиссии, без выбора торговой формулы.</param>
/// <param name="MinimumOrderSize">Доступность минимального размера заявки отдельно от шага цены.</param>
/// <param name="Btc">Доступность внешнего набора BTC, без проверки его причинности.</param>
/// <param name="Diagnostics">Безопасные причины ограничений без исходных сообщений.</param>
public sealed record ExperimentDatasetSuitability(
    bool MetadataComplete,
    bool NormalizationComplete,
    bool ProjectionConsistent,
    bool? TerminalOrderBookIntegrity,
    DatasetKnowledge Resolution,
    DatasetKnowledge Fees,
    DatasetKnowledge MinimumOrderSize,
    DatasetKnowledge Btc,
    ImmutableArray<ExperimentDatasetDiagnostic> Diagnostics)
{
    /// <summary>Пригоден ли архив для последующей причинной проверки, ещё не для торговли.</summary>
    public bool IsArchiveUsable => !Diagnostics.Any(diagnostic => diagnostic.BlocksReplay);
}

/// <summary>Одна независимая причина ограничения входных данных.</summary>
/// <param name="Code">Устойчивый машинный код.</param>
/// <param name="Message">Безопасное описание причины.</param>
/// <param name="BlocksReplay">Запрещает ли причина использование архива в воспроизведении.</param>
public sealed record ExperimentDatasetDiagnostic(string Code, string Message, bool BlocksReplay);
