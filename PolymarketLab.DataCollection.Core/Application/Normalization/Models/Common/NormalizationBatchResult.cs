namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Сводка обработки одного пакета исходных сообщений.</summary>
public sealed record NormalizationBatchResult
{
    /// <summary>Создаёт согласованную сводку обработки пакета.</summary>
    public NormalizationBatchResult(
        int total,
        int processed,
        int invalid,
        int unsupported,
        int failed,
        long? firstRawMessageId,
        long? lastRawMessageId,
        IReadOnlyCollection<NormalizationMessageError>? errors = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        ArgumentOutOfRangeException.ThrowIfNegative(processed);
        ArgumentOutOfRangeException.ThrowIfNegative(invalid);
        ArgumentOutOfRangeException.ThrowIfNegative(unsupported);
        ArgumentOutOfRangeException.ThrowIfNegative(failed);

        if (total != processed + invalid + unsupported + failed)
            throw new ArgumentException("Total must equal the sum of outcome counts.", nameof(total));

        if (total == 0 && (firstRawMessageId.HasValue || lastRawMessageId.HasValue))
            throw new ArgumentException("An empty batch cannot contain raw message identifiers.");

        if (total > 0
            && (!firstRawMessageId.HasValue
                || !lastRawMessageId.HasValue
                || firstRawMessageId <= 0
                || lastRawMessageId < firstRawMessageId))
        {
            throw new ArgumentException("A non-empty batch requires a valid raw message range.");
        }

        Total = total;
        Processed = processed;
        Invalid = invalid;
        Unsupported = unsupported;
        Failed = failed;
        FirstRawMessageId = firstRawMessageId;
        LastRawMessageId = lastRawMessageId;
        Errors = errors?.ToArray() ?? [];
    }

    /// <summary>Количество захваченных сообщений.</summary>
    public int Total { get; }

    /// <summary>Количество успешно сохранённых сообщений.</summary>
    public int Processed { get; }

    /// <summary>Количество сообщений с недопустимыми данными.</summary>
    public int Invalid { get; }

    /// <summary>Количество сообщений с неподдерживаемым типом события.</summary>
    public int Unsupported { get; }

    /// <summary>Количество сообщений с технической ошибкой или потерянным захватом.</summary>
    public int Failed { get; }

    /// <summary>Наименьший идентификатор сообщения в пакете.</summary>
    public long? FirstRawMessageId { get; }

    /// <summary>Наибольший идентификатор сообщения в пакете.</summary>
    public long? LastRawMessageId { get; }

    /// <summary>Безопасная диагностика неуспешных сообщений без исходного payload.</summary>
    public IReadOnlyList<NormalizationMessageError> Errors { get; }
}
