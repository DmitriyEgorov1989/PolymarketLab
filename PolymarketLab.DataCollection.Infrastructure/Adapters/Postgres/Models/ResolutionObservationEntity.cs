using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

/// <summary>Безопасное устойчивое observation одного resolution source без raw payload.</summary>
internal sealed class ResolutionObservationEntity
{
    private ResolutionObservationEntity()
    {
    }

    /// <summary>Создаёт observation указанной session, source и проверки.</summary>
    public ResolutionObservationEntity(
        CollectorSessionId sessionId,
        ResolutionObservationSource source,
        DateTimeOffset observedAt,
        DurableResolutionObservationStatus status)
    {
        SessionId = sessionId;
        Source = source;
        ObservedAt = observedAt;
        Status = status;
    }

    /// <summary>Идентификатор observation.</summary>
    public long Id { get; private set; }
    /// <summary>Идентификатор session.</summary>
    public CollectorSessionId SessionId { get; private set; } = null!;
    /// <summary>Источник observation.</summary>
    public ResolutionObservationSource Source { get; private set; }
    /// <summary>Локальное UTC-время завершения проверки.</summary>
    public DateTimeOffset ObservedAt { get; private set; }
    /// <summary>Безопасный результат проверки.</summary>
    public DurableResolutionObservationStatus Status { get; private set; }
    /// <summary>Winner token id либо <see langword="null" /> без terminal winner.</summary>
    public string? WinnerTokenId { get; set; }
    /// <summary>Winner outcome либо <see langword="null" /> без terminal winner.</summary>
    public string? WinnerOutcome { get; set; }
    /// <summary>Gamma event id либо <see langword="null" /> для других sources.</summary>
    public string? ExternalEventId { get; set; }
    /// <summary>Gamma event slug либо <see langword="null" /> для других sources.</summary>
    public string? EventSlug { get; set; }
    /// <summary>Проверенный market id либо <see langword="null" />, если source его не предоставил.</summary>
    public string? ExternalMarketId { get; set; }
    /// <summary>Gamma market slug либо <see langword="null" /> для других sources.</summary>
    public string? MarketSlug { get; set; }
    /// <summary>Проверенный condition id либо <see langword="null" />.</summary>
    public string? ConditionId { get; set; }
    /// <summary>Terminal closed flag либо <see langword="null" /> для WebSocket.</summary>
    public bool? Closed { get; set; }
    /// <summary>Order acceptance flag либо <see langword="null" /> для WebSocket.</summary>
    public bool? AcceptingOrders { get; set; }
    /// <summary>Gamma UMA status либо <see langword="null" />.</summary>
    public string? UmaResolutionStatus { get; set; }
    /// <summary>Gamma close time либо <see langword="null" />.</summary>
    public DateTimeOffset? ExternalClosedAt { get; set; }
    /// <summary>Безопасный error code либо <see langword="null" />.</summary>
    public string? ErrorCode { get; set; }
    /// <summary>Безопасное error message либо <see langword="null" />.</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>Исходный raw id WebSocket либо <see langword="null" /> для REST.</summary>
    public long? RawMessageId { get; set; }
    /// <summary>Индекс объекта в raw JSON либо <see langword="null" /> для REST.</summary>
    public int? RawItemIndex { get; set; }
    /// <summary>WebSocket connection epoch либо <see langword="null" /> для REST.</summary>
    public long? ConnectionEpoch { get; set; }
    /// <summary>Проверенные исходы source observation.</summary>
    public List<ResolutionObservationOutcomeEntity> Outcomes { get; } = [];
}
