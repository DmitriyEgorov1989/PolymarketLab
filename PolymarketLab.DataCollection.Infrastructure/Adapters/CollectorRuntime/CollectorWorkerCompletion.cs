using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed record CollectorWorkerCompletion(
    UnitResult<Error> Result,
    CollectorWorkerCompletionOrigin Origin,
    DateTimeOffset CompletedAt);
