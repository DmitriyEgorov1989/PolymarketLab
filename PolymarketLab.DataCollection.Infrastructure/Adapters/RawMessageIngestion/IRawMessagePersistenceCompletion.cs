using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;

internal interface IRawMessagePersistenceCompletion
{
    Task<RawMessagePersistenceCompletionResult> Completion { get; }

    void CompleteProducers();

    Task<RawMessagePersistenceCompletionResult> WaitForCompletionAsync(
        CancellationToken cancellationToken);
}

internal sealed record RawMessagePersistenceCompletionResult(
    UnitResult<Error> Result,
    int? UnconfirmedMessageCount)
{
    public static RawMessagePersistenceCompletionResult Success(
        int? unconfirmedMessageCount)
    {
        return new RawMessagePersistenceCompletionResult(
            UnitResult.Success<Error>(),
            unconfirmedMessageCount);
    }

    public static RawMessagePersistenceCompletionResult Failure(
        Error error,
        int? unconfirmedMessageCount)
    {
        return new RawMessagePersistenceCompletionResult(
            UnitResult.Failure(error),
            unconfirmedMessageCount);
    }
}
