using System.Text.Json;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>
/// Представляет один JSON-объект, выделенный из исходного сообщения для нормализации.
/// </summary>
public sealed record LogicalRawEvent
{
    /// <summary>Создаёт логическое событие с собственной копией JSON-объекта.</summary>
    /// <param name="rawMessageId">Идентификатор исходного сообщения.</param>
    /// <param name="rawItemIndex">Позиция объекта в корневом массиве или ноль для корневого объекта.</param>
    /// <param name="projectionVersion">Версия формируемой нормализованной проекции.</param>
    /// <param name="sessionId">Идентификатор сессии сборщика.</param>
    /// <param name="receivedAt">Момент получения исходного сообщения.</param>
    /// <param name="json">JSON-объект логического события.</param>
    public LogicalRawEvent(
        long rawMessageId,
        int rawItemIndex,
        int projectionVersion,
        CollectorSessionId sessionId,
        DateTimeOffset receivedAt,
        JsonElement json)
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

        if (json.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Logical raw event must be a JSON object.", nameof(json));

        RawMessageId = rawMessageId;
        RawItemIndex = rawItemIndex;
        ProjectionVersion = projectionVersion;
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        ReceivedAt = receivedAt;
        Json = json.Clone();
    }

    /// <summary>Идентификатор исходного сообщения.</summary>
    public long RawMessageId { get; }

    /// <summary>Позиция объекта в корневом массиве или ноль для корневого объекта.</summary>
    public int RawItemIndex { get; }

    /// <summary>Версия формируемой нормализованной проекции.</summary>
    public int ProjectionVersion { get; }

    /// <summary>Идентификатор сессии, в которой получено сообщение.</summary>
    public CollectorSessionId SessionId { get; }

    /// <summary>Момент получения исходного сообщения приложением.</summary>
    public DateTimeOffset ReceivedAt { get; }

    /// <summary>Собственная копия JSON-объекта логического события.</summary>
    public JsonElement Json { get; }
}
