namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Результат нормализации одного логического события.</summary>
public sealed record NormalizationResult
{
    private NormalizationResult(
        int rawItemIndex,
        NormalizationOutcome outcome,
        int? normalizerVersion,
        NormalizedEvent? normalizedEvent,
        NormalizationIssue? issue)
    {
        if (rawItemIndex < 0)
            throw new ArgumentOutOfRangeException(
                nameof(rawItemIndex),
                "Raw item index cannot be negative.");

        RawItemIndex = rawItemIndex;
        Outcome = outcome;
        NormalizerVersion = normalizerVersion;
        Event = normalizedEvent;
        Issue = issue;
    }

    /// <summary>Позиция логического события внутри исходного сообщения.</summary>
    public int RawItemIndex { get; }

    /// <summary>Ожидаемый итог обработки логического события.</summary>
    public NormalizationOutcome Outcome { get; }

    /// <summary>
    /// Версия выбранного нормализатора или <see langword="null" />, если обработчик не был выбран.
    /// </summary>
    public int? NormalizerVersion { get; }

    /// <summary>Нормализованное событие только для результата <see cref="NormalizationOutcome.Processed" />.</summary>
    public NormalizedEvent? Event { get; }

    /// <summary>Структурированная ошибка для неуспешного ожидаемого результата.</summary>
    public NormalizationIssue? Issue { get; }

    /// <summary>Создаёт успешный результат из нормализованного события.</summary>
    /// <param name="normalizedEvent">Сформированное нормализованное событие.</param>
    public static NormalizationResult Processed(NormalizedEvent normalizedEvent)
    {
        ArgumentNullException.ThrowIfNull(normalizedEvent);

        return new NormalizationResult(
            normalizedEvent.RawItemIndex,
            NormalizationOutcome.Processed,
            normalizedEvent.NormalizerVersion,
            normalizedEvent,
            null);
    }

    /// <summary>Создаёт недопустимый результат до выбора конкретного нормализатора.</summary>
    /// <param name="rawItemIndex">Позиция логического события.</param>
    /// <param name="issue">Причина невозможности выбрать или запустить нормализатор.</param>
    public static NormalizationResult Invalid(
        int rawItemIndex,
        NormalizationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return new NormalizationResult(
            rawItemIndex,
            NormalizationOutcome.Invalid,
            null,
            null,
            issue);
    }

    /// <summary>Создаёт недопустимый результат, возвращённый выбранным нормализатором.</summary>
    /// <param name="rawItemIndex">Позиция логического события.</param>
    /// <param name="normalizerVersion">Версия выбранного нормализатора.</param>
    /// <param name="issue">Ошибка внешнего контракта события.</param>
    public static NormalizationResult Invalid(
        int rawItemIndex,
        int normalizerVersion,
        NormalizationIssue issue)
    {
        if (normalizerVersion <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(normalizerVersion),
                "Normalizer version must be positive.");

        ArgumentNullException.ThrowIfNull(issue);

        return new NormalizationResult(
            rawItemIndex,
            NormalizationOutcome.Invalid,
            normalizerVersion,
            null,
            issue);
    }

    /// <summary>Создаёт результат для неизвестного или неподдерживаемого типа события.</summary>
    /// <param name="rawItemIndex">Позиция логического события.</param>
    /// <param name="issue">Описание неподдерживаемого типа.</param>
    public static NormalizationResult Unsupported(
        int rawItemIndex,
        NormalizationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return new NormalizationResult(
            rawItemIndex,
            NormalizationOutcome.Unsupported,
            null,
            null,
            issue);
    }
}
