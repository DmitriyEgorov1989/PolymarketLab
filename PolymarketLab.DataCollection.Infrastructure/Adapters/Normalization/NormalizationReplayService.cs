using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

internal sealed class NormalizationReplayService(
    IServiceScopeFactory scopeFactory,
    IOptions<NormalizerOptions> options) : INormalizationReplayService
{
    private const int MaximumReplayBatchSize = 100;
    private static readonly TimeSpan ContentionDelay = TimeSpan.FromMilliseconds(100);
    private readonly NormalizerOptions options = options.Value;

    public async Task<Result<NormalizationReplayResult, Error>> ReplayAsync(
        NormalizationReplayFilter filter,
        CancellationToken cancellationToken)
    {
        if (options.Enabled && filter.TargetProjectionVersion == options.ProjectionVersion)
        {
            return Result.Failure<NormalizationReplayResult, Error>(
                ReplayNormalizationErrors.TargetProjectionVersionIsActive(
                    filter.TargetProjectionVersion));
        }

        var snapshot = await CaptureSnapshotAsync(cancellationToken);
        var batchSize = Math.Min(options.BatchSize, MaximumReplayBatchSize);
        var batchCount = 0;
        var total = 0;
        var processed = 0;
        var invalid = 0;
        var unsupported = 0;
        var failed = 0;
        long? firstRawMessageId = null;
        long? lastRawMessageId = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = await ProcessBatchAsync(
                filter,
                snapshot,
                batchSize,
                cancellationToken);
            if (batch.Total == 0)
            {
                if (!await HasRemainingAsync(filter, snapshot, cancellationToken))
                    break;

                await Task.Delay(ContentionDelay, cancellationToken);
                continue;
            }

            batchCount++;
            total += batch.Total;
            processed += batch.Processed;
            invalid += batch.Invalid;
            unsupported += batch.Unsupported;
            failed += batch.Failed;
            firstRawMessageId = firstRawMessageId.HasValue
                ? Math.Min(firstRawMessageId.Value, batch.FirstRawMessageId!.Value)
                : batch.FirstRawMessageId;
            lastRawMessageId = lastRawMessageId.HasValue
                ? Math.Max(lastRawMessageId.Value, batch.LastRawMessageId!.Value)
                : batch.LastRawMessageId;
            await Task.Yield();
        }

        return new NormalizationReplayResult(
            batchCount,
            total,
            processed,
            invalid,
            unsupported,
            failed,
            firstRawMessageId,
            lastRawMessageId);
    }

    private async Task<NormalizationReplaySnapshot> CaptureSnapshotAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IRawMessageNormalizationReplayClaimRepository>();
        return await repository.CaptureSnapshotAsync(cancellationToken);
    }

    private async Task<NormalizationBatchResult> ProcessBatchAsync(
        NormalizationReplayFilter filter,
        NormalizationReplaySnapshot snapshot,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IRawMessageNormalizationReplayClaimRepository>();
        var claims = await repository.ClaimBatchAsync(
            filter,
            snapshot,
            batchSize,
            options.ClaimTimeout,
            cancellationToken);
        var processor = scope.ServiceProvider
            .GetRequiredService<IClaimedNormalizationBatchProcessor>();
        return await processor.ProcessClaimsAsync(claims, cancellationToken);
    }

    private async Task<bool> HasRemainingAsync(
        NormalizationReplayFilter filter,
        NormalizationReplaySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IRawMessageNormalizationReplayClaimRepository>();
        return await repository.HasRemainingAsync(filter, snapshot, cancellationToken);
    }
}
