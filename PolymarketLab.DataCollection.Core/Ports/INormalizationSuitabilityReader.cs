using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Читает доказательства завершения нормализации одной collector session.</summary>
public interface INormalizationSuitabilityReader
{
    /// <summary>
    /// Одним согласованным persistence read получает raw/ledger cardinality,
    /// status counts и strict resolution provenance указанной версии.
    /// </summary>
    /// <param name="sessionId">Идентификатор проверяемой session.</param>
    /// <param name="projectionVersion">Положительная snapshot-версия session.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Согласованный снимок без raw payload.</returns>
    Task<NormalizationSuitability> ReadAsync(
        CollectorSessionId sessionId,
        int projectionVersion,
        CancellationToken cancellationToken);
}
