using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal interface ICollectorWorker
{
    Task<UnitResult<Error>> StartAsync(CancellationToken cancellationToken);

    Task<UnitResult<Error>> StopAsync(CancellationToken cancellationToken);
}
