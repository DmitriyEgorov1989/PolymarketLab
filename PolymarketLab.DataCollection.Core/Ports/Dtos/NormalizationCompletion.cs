using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Терминальный результат нормализации одного исходного сообщения.</summary>
public sealed record NormalizationCompletion
{
    private NormalizationCompletion(
        NormalizationStatus status,
        IReadOnlyCollection<NormalizedEvent> events,
        NormalizationIssue? issue)
    {
        Status = status;
        Events = events.ToArray();
        Issue = issue;
    }

    /// <summary>Терминальный статус исходного сообщения.</summary>
    public NormalizationStatus Status { get; }

    /// <summary>Нормализованные события успешного результата.</summary>
    public IReadOnlyList<NormalizedEvent> Events { get; }

    /// <summary>Ожидаемая ошибка для неуспешного терминального результата.</summary>
    public NormalizationIssue? Issue { get; }

    /// <summary>Создаёт успешное завершение с собственной копией событий.</summary>
    /// <param name="events">События с уникальными индексами внутри исходного сообщения.</param>
    /// <returns>Завершение со статусом <see cref="NormalizationStatus.Processed" />.</returns>
    public static NormalizationCompletion Processed(
        IReadOnlyCollection<NormalizedEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.GroupBy(normalizedEvent => normalizedEvent.RawItemIndex)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Raw item indexes must be unique within one message.",
                nameof(events));
        }

        return new NormalizationCompletion(NormalizationStatus.Processed, events, null);
    }

    /// <summary>Создаёт завершение для сообщения с недопустимыми данными.</summary>
    /// <param name="issue">Структурированная причина недопустимости.</param>
    /// <returns>Завершение со статусом <see cref="NormalizationStatus.Invalid" />.</returns>
    public static NormalizationCompletion Invalid(NormalizationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return new NormalizationCompletion(NormalizationStatus.Invalid, [], issue);
    }

    /// <summary>Создаёт завершение для неподдерживаемого типа события.</summary>
    /// <param name="issue">Описание неподдерживаемого события.</param>
    /// <returns>Завершение со статусом <see cref="NormalizationStatus.Unsupported" />.</returns>
    public static NormalizationCompletion Unsupported(NormalizationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return new NormalizationCompletion(NormalizationStatus.Unsupported, [], issue);
    }
}
