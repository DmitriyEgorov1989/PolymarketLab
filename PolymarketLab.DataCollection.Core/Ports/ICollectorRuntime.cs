using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

public interface ICollectorRuntime
{
    Task<UnitResult<Error>> StartAsync(
        CollectorRuntimeStartRequest request,
        CancellationToken cancellationToken);

    Task<UnitResult<Error>> StopAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);
}
