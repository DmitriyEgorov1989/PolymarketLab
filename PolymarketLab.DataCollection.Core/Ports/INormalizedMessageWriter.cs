using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Атомарно сохраняет результат нормализации одного исходного сообщения.</summary>
public interface INormalizedMessageWriter
{
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
