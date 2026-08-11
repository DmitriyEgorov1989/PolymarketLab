using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Содержит сохранённое исходное сообщение и его идентичность для нормализации.</summary>
public sealed record RawMessageEnvelope
{
    /// <summary>Создаёт снимок исходного сообщения с собственной копией payload.</summary>
    /// <param name="rawMessageId">Идентификатор строки исходного сообщения.</param>
    /// <param name="sessionId">Идентификатор сессии сборщика.</param>
    /// <param name="receivedAt">Момент получения сообщения приложением.</param>
    /// <param name="payload">Исходные UTF-8 bytes WebSocket-сообщения.</param>
    public RawMessageEnvelope(
        long rawMessageId,
        CollectorSessionId sessionId,
        DateTimeOffset receivedAt,
        ReadOnlyMemory<byte> payload)
    {
        if (rawMessageId <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(rawMessageId),
                "Raw message id must be positive.");

        RawMessageId = rawMessageId;
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        ReceivedAt = receivedAt;
        Payload = payload.ToArray();
    }

    /// <summary>Идентификатор строки исходного сообщения.</summary>
    public long RawMessageId { get; }

    /// <summary>Идентификатор сессии, в которой получено сообщение.</summary>
    public CollectorSessionId SessionId { get; }

    /// <summary>Момент получения сообщения приложением.</summary>
    public DateTimeOffset ReceivedAt { get; }

    /// <summary>Собственная копия исходных UTF-8 bytes WebSocket-сообщения.</summary>
    public ReadOnlyMemory<byte> Payload { get; }
}
