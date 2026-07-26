using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed class CollectorRuntimeEntry(ICollectorWorker worker)
{
    private readonly object _sync = new();
    private Task<UnitResult<Error>>? _startTask;
    private Task<UnitResult<Error>>? _stopTask;

    public OperationAttempt Start(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_startTask is not null)
                return new OperationAttempt(_startTask, false);

            _startTask = Invoke(() => worker.StartAsync(cancellationToken));
            return new OperationAttempt(_startTask, true);
        }
    }

    public OperationAttempt Stop(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_stopTask is not null)
                return new OperationAttempt(_stopTask, false);

            _stopTask = Invoke(() => worker.StopAsync(cancellationToken));
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

    internal readonly record struct OperationAttempt(
        Task<UnitResult<Error>> Task,
        bool IsOwner);
}
