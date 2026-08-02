using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

public interface ICollectorSessionShutdownHandler
{
    Task<UnitResult<Error>> MarkStoppingAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);

    Task<UnitResult<Error>> MarkStoppedAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);

    Task<UnitResult<Error>> MarkFailedAsync(
        CollectorSessionId sessionId,
        Error error,
        CancellationToken cancellationToken);
}
