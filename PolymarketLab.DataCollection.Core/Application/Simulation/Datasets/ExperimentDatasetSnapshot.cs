using System.Collections.Immutable;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;

/// <summary>Материализованный вход из одного снимка PostgreSQL, не требующий повторного чтения архива.</summary>
/// <param name="Manifest">Детерминированный паспорт набора.</param>
/// <param name="Suitability">Независимые проверки качества и доступности сведений.</param>
/// <param name="Normalization">Счётчики обработки каждого исходного сообщения выбранной версией.</param>
/// <param name="OrderBookEvents">События стакана в архивном порядке, без ретроспективной сортировки.</param>
/// <param name="ProjectionItems">Все типизированные события, включая финансовые метаданные.</param>
/// <param name="ResolutionEvidence">Канонический JSON сохранённых свидетельств без исходных сообщений.</param>
public sealed record ExperimentDatasetSnapshot(
    ExperimentDatasetManifest Manifest,
    ExperimentDatasetSuitability Suitability,
    NormalizationSuitability Normalization,
    ImmutableArray<NormalizedOrderBookEvent> OrderBookEvents,
    ImmutableArray<ExperimentProjectionItem> ProjectionItems,
    string ResolutionEvidence);

/// <summary>Неизменяемая архивная позиция с полным типизированным содержимым.</summary>
/// <param name="RawMessageId">Идентификатор исходного сообщения.</param>
/// <param name="RawItemIndex">Индекс события внутри сообщения.</param>
/// <param name="EventType">Сохранённый тип события.</param>
/// <param name="CanonicalContent">Канонический JSON заголовка и типизированных строк, без raw payload.</param>
public sealed record ExperimentProjectionItem(
    long RawMessageId, int RawItemIndex, string EventType, string CanonicalContent);
