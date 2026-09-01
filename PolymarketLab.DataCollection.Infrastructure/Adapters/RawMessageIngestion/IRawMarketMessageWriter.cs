using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;

/// <summary>Атомарно сохраняет raw batch и durable progress незаблокированных сессий.</summary>
internal interface IRawMarketMessageWriter
{
    /// <summary>Сохраняет сообщения и отделяет сессии, закрытые durable write fence.</summary>
    /// <param name="messages">Исходные сообщения; коллекция не должна быть <see langword="null" />.</param>
    /// <param name="checkpoints">Снимки progress; коллекция не должна быть <see langword="null" />.</param>
    /// <param name="cancellationToken">Токен отмены транзакции.</param>
    /// <returns>Идентификаторы сессий с сохранёнными и отклонёнными сообщениями.</returns>
    Task<RawMarketMessageWriteResult> WriteBatchAsync(
        IReadOnlyCollection<RawMarketMessage> messages,
        IReadOnlyCollection<CollectorSessionProgressCheckpoint> checkpoints,
        CancellationToken cancellationToken);
}
