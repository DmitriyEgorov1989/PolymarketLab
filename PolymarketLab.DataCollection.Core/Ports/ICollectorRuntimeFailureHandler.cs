using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports;

public interface ICollectorRuntimeFailureHandler
{
    Task<UnitResult<Error>> HandleAsync(
        CollectorRuntimeFailure failure,
        CancellationToken cancellationToken);
}
