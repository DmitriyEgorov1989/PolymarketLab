using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

/// <summary>Устойчивый cursor scanner, polling и ссылки consensus одной session.</summary>
internal sealed class ResolutionStateEntity
{
    private ResolutionStateEntity()
    {
    }

    /// <summary>Создаёт пустое resolution state указанной session.</summary>
    public ResolutionStateEntity(CollectorSessionId sessionId)
    {
        SessionId = sessionId;
    }

    /// <summary>Идентификатор session и первичный ключ state.</summary>
    public CollectorSessionId SessionId { get; private set; } = null!;
    /// <summary>Последний просмотренный raw id; ноль означает отсутствие scan.</summary>
    public long LastScannedRawMessageId { get; private set; }
    /// <summary>Начало последнего polling cycle либо <see langword="null" />.</summary>
    public DateTimeOffset? LastPollingCycleAt { get; private set; }
    /// <summary>Gamma terminal observation id либо <see langword="null" /> до consensus.</summary>
    public long? PrimaryObservationId { get; private set; }
    /// <summary>CLOB terminal observation id либо <see langword="null" /> до consensus.</summary>
    public long? ConfirmingObservationId { get; private set; }
    /// <summary>Время consensus либо <see langword="null" />, пока он не достигнут.</summary>
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>Монотонно продвигает raw scanner cursor.</summary>
    public void AdvanceScanner(long rawMessageId)
    {
        if (rawMessageId > LastScannedRawMessageId)
            LastScannedRawMessageId = rawMessageId;
    }

    /// <summary>Запоминает начало последнего polling cycle.</summary>
    public void RecordPollingCycle(DateTimeOffset startedAt) =>
        LastPollingCycleAt = startedAt;

    /// <summary>Фиксирует ссылки на согласованные Gamma/CLOB observations.</summary>
    public void Confirm(
        long primaryObservationId,
        long confirmingObservationId,
        DateTimeOffset confirmedAt)
    {
        PrimaryObservationId = primaryObservationId;
        ConfirmingObservationId = confirmingObservationId;
        ConfirmedAt = confirmedAt;
    }
}
