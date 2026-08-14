using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Захватывает сохранённые исходные сообщения для эксклюзивной нормализации.</summary>
public interface IRawMessageNormalizationClaimRepository
{
    /// <summary>
    /// Захватывает очередной упорядоченный пакет сообщений для указанной версии проекции.
    /// Устаревшие незавершённые захваты могут быть выданы повторно.
    /// </summary>
    /// <param name="projectionVersion">Версия нормализованной проекции.</param>
    /// <param name="batchSize">Максимальное количество сообщений в пакете.</param>
    /// <param name="claimTimeout">Срок, после которого незавершённый захват считается устаревшим.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Захваченные сообщения по возрастанию идентификатора исходной строки.</returns>
    Task<IReadOnlyList<ClaimedRawMessage>> ClaimBatchAsync(
        int projectionVersion,
        int batchSize,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken);
}
