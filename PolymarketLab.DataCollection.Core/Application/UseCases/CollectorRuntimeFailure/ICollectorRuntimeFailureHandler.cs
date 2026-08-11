using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;
using CollectorRuntimeFailureNotification = PolymarketLab.DataCollection.Core.Ports.Dtos.CollectorRuntimeFailure;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorRuntimeFailure;

public interface ICollectorRuntimeFailureHandler
{
    Task<UnitResult<Error>> HandleAsync(
        CollectorRuntimeFailureNotification failure,
        CancellationToken cancellationToken);
}
