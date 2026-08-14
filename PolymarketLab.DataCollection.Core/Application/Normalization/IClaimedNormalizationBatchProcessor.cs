using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Core.Application.Normalization;

public interface IClaimedNormalizationBatchProcessor
{
    Task<NormalizationBatchResult> ProcessClaimsAsync(
        IReadOnlyList<ClaimedRawMessage> claims,
        CancellationToken cancellationToken);
}
