using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorOrderBookIntegrity;

/// <summary>Воспроизводит committed typed-события в изолированных временных состояниях стаканов.</summary>
public sealed class CollectorOrderBookIntegrityCoordinator(
    IOrderBookIntegrityReader reader,
    IOrderBookProjector projector,
    ILogger<CollectorOrderBookIntegrityCoordinator> logger)
    : ICollectorOrderBookIntegrityCoordinator
{
    /// <inheritdoc />
    public async Task<UnitResult<Error>> EvaluateAsync(
        CollectorSessionId sessionId,
        int projectionVersion,
        IReadOnlyCollection<TokenId> tokenIds,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectionVersion);
        ArgumentNullException.ThrowIfNull(tokenIds);

        try
        {
            var events = await reader.ReadAsync(
                sessionId,
                projectionVersion,
                cancellationToken);
            var expectedAssetIds = tokenIds
                .Select(tokenId => tokenId.Value)
                .ToHashSet(StringComparer.Ordinal);
            if (expectedAssetIds.Count == 0)
                throw new InvalidOperationException("Collector session has no snapshot tokens.");

            var states = expectedAssetIds.ToDictionary(
                assetId => assetId,
                assetId => new OrderBookState(assetId),
                StringComparer.Ordinal);
            var initializedAssetIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var @event in events)
            {
                foreach (var assetId in GetAssetIds(@event))
                {
                    if (!states.TryGetValue(assetId, out var state))
                    {
                        return UnitResult.Failure(
                            CollectorOrderBookIntegrityErrors.UnexpectedAsset(
                                sessionId,
                                assetId));
                    }

                    if (@event is NormalizedOrderBookEvent.BookSnapshot)
                    {
                        var snapshotResult = projector.Apply(state, @event);
                        if (snapshotResult.IntegrityIssue is not null)
                        {
                            return UnitResult.Failure(
                                CollectorOrderBookIntegrityErrors.IntegrityIssue(
                                    sessionId,
                                    assetId,
                                    snapshotResult.IntegrityIssue));
                        }

                        initializedAssetIds.Add(assetId);
                        continue;
                    }

                    if (!initializedAssetIds.Contains(assetId))
                    {
                        return UnitResult.Failure(
                            CollectorOrderBookIntegrityErrors.MissingSnapshot(
                                sessionId,
                                assetId));
                    }

                    var result = projector.Apply(state, @event);
                    if (result.IntegrityIssue is not null)
                    {
                        return UnitResult.Failure(
                            CollectorOrderBookIntegrityErrors.IntegrityIssue(
                                sessionId,
                                assetId,
                                result.IntegrityIssue));
                    }
                }
            }

            var missingAssetId = expectedAssetIds
                .FirstOrDefault(assetId => !initializedAssetIds.Contains(assetId));
            if (missingAssetId is not null)
            {
                return UnitResult.Failure(
                    CollectorOrderBookIntegrityErrors.MissingSnapshot(
                        sessionId,
                        missingAssetId));
            }

            return UnitResult.Success<Error>();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Failed to evaluate order book integrity for collector session {SessionId} and projection version {ProjectionVersion}.",
                sessionId.Value,
                projectionVersion);
            return UnitResult.Failure(
                CollectorOrderBookIntegrityErrors.ReadFailed(sessionId));
        }
    }

    private static IEnumerable<string> GetAssetIds(NormalizedOrderBookEvent @event) =>
        @event switch
        {
            NormalizedOrderBookEvent.BookSnapshot snapshot => [snapshot.Record.AssetId],
            NormalizedOrderBookEvent.PriceChanges changes => changes.Records
                .Select(change => change.AssetId)
                .Distinct(StringComparer.Ordinal),
            NormalizedOrderBookEvent.TickSizeChange change => [change.Record.AssetId],
            NormalizedOrderBookEvent.BestBidAsk quote => [quote.Record.AssetId],
            _ => throw new ArgumentOutOfRangeException(nameof(@event))
        };
}
