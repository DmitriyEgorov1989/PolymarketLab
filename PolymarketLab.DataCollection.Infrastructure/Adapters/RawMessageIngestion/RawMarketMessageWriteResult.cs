using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;

/// <summary>Результат атомарной записи raw batch с учётом durable write fence.</summary>
/// <param name="PersistedSessionIds">Сессии, для которых сообщения и progress сохранены.</param>
/// <param name="FencedSessionIds">Сессии, записи которых ожидаемо отклонены; коллекция не бывает <see langword="null" />.</param>
internal sealed record RawMarketMessageWriteResult(
    IReadOnlySet<CollectorSessionId> PersistedSessionIds,
    IReadOnlySet<CollectorSessionId> FencedSessionIds)
{
    /// <summary>Результат пустого batch без сохранённых или отклонённых сессий.</summary>
    public static RawMarketMessageWriteResult Empty { get; } = new(
        new HashSet<CollectorSessionId>(),
        new HashSet<CollectorSessionId>());
}
