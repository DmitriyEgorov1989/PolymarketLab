using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed record CollectorRuntimeShutdownResult(
    CollectorSessionId SessionId,
    UnitResult<Error> Result,
    Exception? Exception = null);
