using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

public interface INormalizationReplayService
{
    Task<Result<NormalizationReplayResult, Error>> ReplayAsync(
        NormalizationReplayFilter filter,
        CancellationToken cancellationToken);
}
