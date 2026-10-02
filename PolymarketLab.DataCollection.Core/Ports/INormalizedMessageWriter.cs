using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Атомарно сохраняет результат нормализации одного исходного сообщения.</summary>
public interface INormalizedMessageWriter
{
    /// <summary>Максимальное поддерживаемое количество сообщений в одном пакете записи.</summary>
    public const int MaximumBatchSize = 1_000;

    /// <summary>Атомарно записывает пакет результатов и возвращает статус каждого запроса в исходном порядке.</summary>
    /// <param name="requests">Непустой список результатов нормализации без повторяющихся захватов.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статусы записей в том же порядке и в том же количестве, что и запросы.</returns>
    /// <remarks>Если метод выбрасывает исключение, ни один запрос пакета не должен остаться записанным.</remarks>
    Task<IReadOnlyList<NormalizationWriteStatus>> WriteBatchAsync(
        IReadOnlyList<NormalizationWriteRequest> requests,
        CancellationToken cancellationToken);

    /// <summary>Записывает нормализованный результат и завершает принадлежащий обработчику захват.</summary>
    /// <param name="claim">Захваченное исходное сообщение с поколением захвата.</param>
    /// <param name="completion">Терминальный результат нормализации сообщения.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Фактический результат записи с учётом идемпотентности и потери захвата.</returns>
    Task<NormalizationWriteStatus> WriteAsync(
        ClaimedRawMessage claim,
        NormalizationCompletion completion,
        CancellationToken cancellationToken);
}
