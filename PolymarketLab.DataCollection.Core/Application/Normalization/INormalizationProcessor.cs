using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Core.Application.Normalization;

/// <summary>Обрабатывает один захваченный пакет исходных сообщений.</summary>
public interface INormalizationProcessor
{
    /// <summary>Захватывает и последовательно нормализует очередной пакет сообщений.</summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Сводка фактически обработанного пакета.</returns>
    Task<NormalizationBatchResult> ProcessBatchAsync(
        CancellationToken cancellationToken);
}
