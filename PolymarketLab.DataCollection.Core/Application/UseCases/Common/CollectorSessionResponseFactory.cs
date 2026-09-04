using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Common;

/// <inheritdoc />
public sealed class CollectorSessionResponseFactory(
    ICollectorSessionProgressRepository progressRepository,
    ICollectorTokenReadinessRepository tokenReadinessRepository,
    IResolutionObservationRepository resolutionRepository,
    ICollectorDatasetCleanupAuditReader cleanupAuditReader,
    INormalizationSuitabilityReader normalizationReader)
    : ICollectorSessionResponseFactory
{
    private static readonly TimeSpan PreparationLead = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReadinessLead = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ResolutionWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NormalizationWindow = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public async Task<CollectorSessionResponse> CreateAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        var progress = await progressRepository.GetAsync(session.Id, cancellationToken);
        var tokenReadiness = progress.CurrentConnectionEpoch > 0
            ? await tokenReadinessRepository.GetAsync(
                session.Id,
                progress.CurrentConnectionEpoch,
                cancellationToken)
            : [];
        var resolution = await resolutionRepository.GetStateAsync(session.Id, cancellationToken);
        var cleanup = await cleanupAuditReader.GetBySessionIdAsync(session.Id, cancellationToken);
        var normalization = cleanup is null && session.ProjectionVersion is > 0
            ? await normalizationReader.ReadAsync(
                session.Id,
                session.ProjectionVersion.Value,
                cancellationToken)
            : null;

        return Map(session, progress, tokenReadiness, normalization, resolution, cleanup);
    }

    private static CollectorSessionResponse Map(
        CollectorSessionAggregate session,
        CollectorSessionProgress progress,
        IReadOnlyCollection<CollectorTokenReadiness> tokenReadiness,
        NormalizationSuitability? normalization,
        DurableResolutionState resolution,
        CollectorDatasetCleanupAudit? cleanup)
    {
        var tokens = session.Tokens
            .OrderBy(token => token.OutcomeIndex)
            .Select(token => new CollectorSessionTokenResponse(
                token.TokenId.Value,
                token.Outcome,
                token.OutcomeIndex))
            .ToArray();

        var snapshot = new CollectorSessionSnapshotResponse(
            session.ExternalEventId,
            session.EventSlug,
            session.ExternalMarketId,
            session.MarketSlug,
            session.ConditionId,
            session.EventStartsAt,
            session.EventEndsAt,
            session.ProjectionVersion,
            tokens);

        var readinessByTokenId = tokenReadiness.ToDictionary(x => x.TokenId.Value);
        var readiness = new CollectorReadinessResponse(
            progress.CurrentConnectionEpoch,
            tokens
                .Select(token => new CollectorTokenReadinessResponse(
                    token.TokenId,
                    readinessByTokenId.GetValueOrDefault(token.TokenId)?.InitialBookEnqueuedAt))
                .ToArray());

        var normalizationResponse = normalization is null
            ? null
            : new CollectorNormalizationResponse(
                normalization.RawCount,
                normalization.LedgerCount,
                normalization.ProcessedCount,
                normalization.PendingCount,
                normalization.ProcessingCount,
                normalization.UnsupportedCount,
                normalization.InvalidCount,
                normalization.FailedCount,
                normalization.MissingCount,
                normalization.ResolutionRawItemProcessed);

        var resolutionResponse = MapResolution(session, resolution);

        var cleanupResponse = cleanup is null
            ? null
            : new CollectorCleanupResponse(
                session.InvalidatingAt,
                cleanup.CompletedAt,
                session.ProjectionVersion,
                session.FailureCode,
                session.FailureMessage,
                cleanup.DeletedRawMessageCount,
                cleanup.DeletedNormalizationCount,
                cleanup.DeletedNormalizedEventCount);

        return new CollectorSessionResponse(
            session.Id.Value,
            session.MarketId.Value,
            snapshot,
            session.Status.ToString(),
            session.Phase?.ToString(),
            EffectiveDeadline(session),
            session.CreatedAt,
            session.StartedAt,
            session.SubscriptionReadyAt,
            session.StoppedAt,
            session.InvalidatingAt,
            session.StopReason?.ToString(),
            session.FailureCode,
            session.FailureMessage,
            readiness,
            progress.MessagesReceived,
            progress.MessagesEnqueued,
            progress.MessagesPersisted,
            progress.RawMessageCount,
            progress.LastMessageAt,
            progress.ReconnectCount,
            normalizationResponse,
            resolutionResponse,
            cleanupResponse);
    }

    private static CollectorResolutionResponse MapResolution(
        CollectorSessionAggregate session,
        DurableResolutionState resolution)
    {
        var observations = resolution.Observations;
        var sourceStates = Enum.GetValues<ResolutionObservationSource>()
            .Select(source => observations
                .Where(observation => observation.Source == source)
                .OrderByDescending(observation => observation.ObservedAt)
                .ThenByDescending(observation => observation.Id)
                .FirstOrDefault())
            .Where(observation => observation is not null)
            .Select(observation => MapSourceResponse(observation!))
            .ToArray();

        var confirmationSources = new List<DurableResolutionObservation>();
        if (session.ResolutionSignaledAt is { } signaledAt
            && session.WinningTokenId is { } winningTokenId
            && session.ResolutionConnectionEpoch is { } resolutionEpoch)
        {
            var webSocketEvidence = observations
                .Where(observation =>
                    observation.Source == ResolutionObservationSource.WebSocket
                    && observation.Status == DurableResolutionObservationStatus.Terminal
                    && observation.ObservedAt == signaledAt
                    && observation.ConnectionEpoch == resolutionEpoch
                    && observation.Winner is not null
                    && observation.Winner.TokenId == winningTokenId)
                .OrderBy(observation => observation.Id)
                .FirstOrDefault();
            if (webSocketEvidence is not null)
                confirmationSources.Add(webSocketEvidence);
        }

        if (resolution.Confirmation is { } confirmation)
        {
            foreach (var observationId in new[]
                     {
                         confirmation.PrimaryObservationId,
                         confirmation.ConfirmingObservationId
                     })
            {
                var evidence = observations.SingleOrDefault(
                    observation => observation.Id == observationId);
                if (evidence is not null
                    && evidence.Source != ResolutionObservationSource.WebSocket)
                {
                    confirmationSources.Add(evidence);
                }
            }
        }

        var confirmationResponses = confirmationSources
            .OrderBy(observation => observation.Source)
            .Select(MapSourceResponse)
            .ToArray();

        return new CollectorResolutionResponse(
            session.ResolutionSignaledAt,
            session.ResolutionConfirmedAt,
            session.WinningTokenId,
            session.WinningOutcome,
            session.ResolutionConnectionEpoch,
            resolution.LastPollingCycleAt,
            sourceStates,
            confirmationResponses);
    }

    private static CollectorResolutionSourceResponse MapSourceResponse(
        DurableResolutionObservation observation) =>
        new(
            observation.Source.ToString(),
            observation.Status.ToString(),
            observation.ObservedAt,
            observation.Winner?.TokenId,
            observation.Winner?.Outcome,
            observation.ErrorCode,
            observation.ErrorMessage);

    private static DateTimeOffset? EffectiveDeadline(CollectorSessionAggregate session) =>
        session.Phase switch
        {
            CollectorSessionPhase.WaitingForPreparation =>
                session.EventStartsAt - PreparationLead,
            CollectorSessionPhase.Connecting or
            CollectorSessionPhase.AwaitingInitialBooks or
            CollectorSessionPhase.AwaitingHeartbeat => ReadinessDeadline(session),
            CollectorSessionPhase.ReadyBeforeWindow => session.EventStartsAt,
            CollectorSessionPhase.CollectingWindow => session.EventEndsAt,
            CollectorSessionPhase.AwaitingResolution =>
                session.EventEndsAt + ResolutionWindow,
            CollectorSessionPhase.AwaitingNormalization =>
                session.AwaitingNormalizationAt + NormalizationWindow,
            _ => null
        };

    private static DateTimeOffset? ReadinessDeadline(CollectorSessionAggregate session)
    {
        if (session.EventStartsAt is not { } eventStartsAt)
            return null;

        var regularDeadline = eventStartsAt - ReadinessLead;
        return session.StartedAt is null || session.StartedAt < regularDeadline
            ? regularDeadline
            : eventStartsAt;
    }
}
