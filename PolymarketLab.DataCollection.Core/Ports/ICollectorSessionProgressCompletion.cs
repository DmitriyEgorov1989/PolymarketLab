using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

public interface ICollectorSessionProgressCompletion
{
    Task<UnitResult<Error>> CompleteAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);
}
