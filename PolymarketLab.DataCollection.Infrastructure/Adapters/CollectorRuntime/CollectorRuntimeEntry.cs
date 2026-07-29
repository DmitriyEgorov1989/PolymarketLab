using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorRuntimeEntry(ICollectorWorker worker)
{
    private readonly object _sync = new();
    private Task<UnitResult<Error>>? _startTask;
    private Task<UnitResult<Error>>? _stopTask;
    private bool _completionObserved;

    public Task<CollectorWorkerCompletion>? ObserveCompletion()
    {
        lock (_sync)
        {
            if (_completionObserved)
                return null;

            _completionObserved = true;
            return worker.Completion;
        }
    }

    public OperationAttempt Start(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_stopTask is not null)
                return new OperationAttempt(_stopTask, false, true);

            if (worker.Completion.IsCompleted)
            {
                return new OperationAttempt(
                    GetCompletionResultAsync(worker.Completion),
                    false,
                    true);
            }

            if (_startTask is not null)
                return new OperationAttempt(_startTask, false);

            _startTask = Invoke(() => worker.StartAsync(cancellationToken));
            return new OperationAttempt(_startTask, true);
        }
    }

    public OperationAttempt Stop()
    {
        lock (_sync)
        {
            if (_stopTask is not null)
                return new OperationAttempt(_stopTask, false);

            _stopTask = Invoke(() => worker.StopAsync(CancellationToken.None));
            return new OperationAttempt(_stopTask, true);
        }
    }

    private static Task<UnitResult<Error>> Invoke(
        Func<Task<UnitResult<Error>>> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception)
        {
            return Task.FromException<UnitResult<Error>>(exception);
        }
    }

    private static async Task<UnitResult<Error>> GetCompletionResultAsync(
        Task<CollectorWorkerCompletion> completion)
    {
        return (await completion).Result;
    }

    internal readonly record struct OperationAttempt(
        Task<UnitResult<Error>> Task,
        bool IsOwner,
        bool RetryAfterCompletion = false);
}
