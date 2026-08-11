using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>
/// Объединяет идентичность логического события, версионный заголовок и предметные записи.
/// </summary>
public sealed record NormalizedEvent
{
    /// <summary>Создаёт успешно нормализованное логическое событие.</summary>
    /// <param name="rawMessageId">Идентификатор исходного сообщения.</param>
    /// <param name="rawItemIndex">Позиция логического события внутри исходного сообщения.</param>
    /// <param name="projectionVersion">Версия схемы нормализованной проекции.</param>
    /// <param name="normalizerVersion">Версия обработчика внешнего события.</param>
    /// <param name="eventType">Исходное значение <c>event_type</c>.</param>
    /// <param name="sessionId">Идентификатор сессии сборщика.</param>
    /// <param name="receivedAt">Момент получения исходного сообщения.</param>
    /// <param name="sourceTimestamp">Epoch milliseconds из внешнего события.</param>
    /// <param name="marketConditionId">Идентификатор условия рынка, если присутствует.</param>
    /// <param name="assetId">Идентификатор актива, если событие относится к одному активу.</param>
    /// <param name="records">Предметные записи, сформированные нормализатором.</param>
    public NormalizedEvent(
        long rawMessageId,
        int rawItemIndex,
        int projectionVersion,
        int normalizerVersion,
        string eventType,
        CollectorSessionId sessionId,
        DateTimeOffset receivedAt,
        long? sourceTimestamp,
        string? marketConditionId,
        string? assetId,
        IReadOnlyCollection<NormalizedRecord> records)
    {
        if (rawMessageId <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(rawMessageId),
                "Raw message id must be positive.");

        if (rawItemIndex < 0)
            throw new ArgumentOutOfRangeException(
                nameof(rawItemIndex),
                "Raw item index cannot be negative.");

        if (projectionVersion <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(projectionVersion),
                "Projection version must be positive.");

        if (normalizerVersion <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(normalizerVersion),
                "Normalizer version must be positive.");

        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));

        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
            throw new ArgumentException(
                "A normalized event must contain at least one record.",
                nameof(records));

        RawMessageId = rawMessageId;
        RawItemIndex = rawItemIndex;
        ProjectionVersion = projectionVersion;
        NormalizerVersion = normalizerVersion;
        EventType = eventType;
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        ReceivedAt = receivedAt;
        SourceTimestamp = sourceTimestamp;
        MarketConditionId = marketConditionId;
        AssetId = assetId;
        Records = records.ToArray();
    }

    /// <summary>Идентификатор исходного сообщения.</summary>
    public long RawMessageId { get; }

    /// <summary>Позиция логического события внутри исходного сообщения.</summary>
    public int RawItemIndex { get; }

    /// <summary>Версия схемы нормализованной проекции.</summary>
    public int ProjectionVersion { get; }

    /// <summary>Версия обработчика внешнего события.</summary>
    public int NormalizerVersion { get; }

    /// <summary>Исходное значение <c>event_type</c>.</summary>
    public string EventType { get; }

    /// <summary>Идентификатор сессии, в которой получено сообщение.</summary>
    public CollectorSessionId SessionId { get; }

    /// <summary>Момент получения исходного сообщения приложением.</summary>
    public DateTimeOffset ReceivedAt { get; }

    /// <summary>Epoch milliseconds из внешнего события или <see langword="null" />.</summary>
    public long? SourceTimestamp { get; }

    /// <summary>Идентификатор условия рынка или <see langword="null" />, если поле отсутствует.</summary>
    public string? MarketConditionId { get; }

    /// <summary>Идентификатор актива или <see langword="null" /> для событий уровня рынка.</summary>
    public string? AssetId { get; }

    /// <summary>Собственная копия предметных записей, сформированных нормализатором.</summary>
    public IReadOnlyList<NormalizedRecord> Records { get; }
}
