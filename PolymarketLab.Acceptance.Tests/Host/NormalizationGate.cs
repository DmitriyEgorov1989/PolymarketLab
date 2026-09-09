using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.Acceptance.Tests.Host;

internal sealed class NormalizationGate
{
    private readonly TaskCompletionSource _released =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _released.TrySetResult();

    public Task WaitAsync(CancellationToken cancellationToken) =>
        _released.Task.WaitAsync(cancellationToken);
}

internal sealed class GatedNormalizationProcessor(
    NormalizationProcessor inner,
    NormalizationGate gate) : INormalizationProcessor
{
    public async Task<NormalizationBatchResult> ProcessBatchAsync(
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        return await inner.ProcessBatchAsync(cancellationToken);
    }
}
