using System.Collections.Immutable;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;

/// <summary>Детерминированный паспорт фактически прочитанного архивного входа.</summary>
/// <param name="FormatVersion">Версия формата манифеста.</param>
/// <param name="Session">Сохранённые реквизиты сессии, без обращения к текущему рынку.</param>
/// <param name="ProjectionVersion">Точная версия выбранной проекции.</param>
/// <param name="RawCount">Количество исходных сообщений, включая подготовку до окна.</param>
/// <param name="FirstRawMessageId">Первый исходный идентификатор; <see langword="null" /> для пустого архива.</param>
/// <param name="LastRawMessageId">Последний исходный идентификатор; <see langword="null" /> для пустого архива.</param>
/// <param name="RawFingerprint">SHA-256 строк исходного архива по алгоритму эпика №56.</param>
/// <param name="LedgerFingerprint">SHA-256 сохранённых записей обработки выбранной версии.</param>
/// <param name="ProjectionFingerprint">SHA-256 заголовков и всех типизированных строк выбранной версии.</param>
/// <param name="EvidenceFingerprint">SHA-256 сохранённых свидетельств подтверждения исхода.</param>
/// <param name="Algorithms">Версии алгоритмов канонизации и проверки.</param>
/// <param name="Normalizers">Фактические версии нормализаторов по типам событий.</param>
/// <param name="Inputs">Настройки и объявленные внешние входы.</param>
/// <param name="ManifestFingerprint">SHA-256 остальных полей манифеста без самого этого поля.</param>
public sealed record ExperimentDatasetManifest(
    int FormatVersion,
    ExperimentSessionSnapshot Session,
    int ProjectionVersion,
    long RawCount,
    long? FirstRawMessageId,
    long? LastRawMessageId,
    string RawFingerprint,
    string LedgerFingerprint,
    string ProjectionFingerprint,
    string EvidenceFingerprint,
    ImmutableSortedDictionary<string, string> Algorithms,
    ImmutableArray<ExperimentNormalizerVersion> Normalizers,
    ExperimentDatasetInputs Inputs,
    string ManifestFingerprint);

/// <summary>Неизменяемая копия идентичности, расписания и результата сессии.</summary>
/// <param name="SessionId">Точный идентификатор сессии.</param>
/// <param name="MarketId">Внутренний идентификатор рынка.</param>
/// <param name="ExternalEventId">Внешний идентификатор события; <see langword="null" /> для неизвестного значения.</param>
/// <param name="EventSlug">Обозначение события; <see langword="null" /> для неизвестного значения.</param>
/// <param name="ExternalMarketId">Внешний идентификатор рынка; <see langword="null" /> для неизвестного значения.</param>
/// <param name="MarketSlug">Обозначение рынка; <see langword="null" /> для неизвестного значения.</param>
/// <param name="ConditionId">Идентификатор условия; <see langword="null" /> для неизвестного значения.</param>
/// <param name="EventStartsAt">Включительное начало окна UTC; <see langword="null" /> для неизвестного значения.</param>
/// <param name="EventEndsAt">Исключительный конец окна UTC; <see langword="null" /> для неизвестного значения.</param>
/// <param name="Tokens">Токены и исходы в сохранённом порядке OutcomeIndex.</param>
/// <param name="ResolutionConfirmedAt">Время подтверждения исхода UTC; <see langword="null" /> без подтверждения.</param>
/// <param name="WinningTokenId">Токен победителя; <see langword="null" /> без сохранённого победителя.</param>
/// <param name="WinningOutcome">Исход победителя; <see langword="null" /> без сохранённого победителя.</param>
public sealed record ExperimentSessionSnapshot(
    Guid SessionId,
    Guid MarketId,
    string? ExternalEventId,
    string? EventSlug,
    string? ExternalMarketId,
    string? MarketSlug,
    string? ConditionId,
    DateTimeOffset? EventStartsAt,
    DateTimeOffset? EventEndsAt,
    ImmutableArray<ExperimentSessionToken> Tokens,
    DateTimeOffset? ResolutionConfirmedAt,
    string? WinningTokenId,
    string? WinningOutcome);

/// <summary>Сохранённый токен одного исхода.</summary>
/// <param name="TokenId">Внешний идентификатор токена.</param>
/// <param name="Outcome">Название исхода.</param>
/// <param name="OutcomeIndex">Позиция во внешнем рынке.</param>
public sealed record ExperimentSessionToken(string TokenId, string Outcome, int OutcomeIndex);

/// <summary>Фактически обнаруженная версия нормализатора.</summary>
/// <param name="EventType">Тип нормализованного события.</param>
/// <param name="Version">Сохранённая версия алгоритма.</param>
public sealed record ExperimentNormalizerVersion(string EventType, int Version);
