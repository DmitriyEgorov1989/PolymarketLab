using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Application.Resolution;
using PolymarketLab.DataCollection.Core.Domain.Models.Resolution;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories;

internal sealed class ResolutionObservationRepository(DataCollectionDbContext dbContext)
    : IResolutionObservationRepository
{
    public async Task<DurableResolutionState> GetStateAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.ResolutionStates
            .AsNoTracking()
            .SingleOrDefaultAsync(current => current.SessionId == sessionId, cancellationToken);
        var observations = await dbContext.ResolutionObservations
            .AsNoTracking()
            .Where(observation => observation.SessionId == sessionId)
            .OrderBy(observation => observation.Id)
            .Include(observation => observation.Outcomes)
            .ToListAsync(cancellationToken);
        var durableObservations = observations.Select(MapObservation).ToArray();

        if (state is null)
            return new DurableResolutionState(sessionId, 0, null, null, durableObservations);

        ResolutionConfirmationReference? confirmation = null;
        if (state.PrimaryObservationId is not null
            && state.ConfirmingObservationId is not null
            && state.ConfirmedAt is not null)
        {
            confirmation = new ResolutionConfirmationReference(
                state.PrimaryObservationId.Value,
                state.ConfirmingObservationId.Value,
                state.ConfirmedAt.Value);
        }

        return new DurableResolutionState(
            sessionId,
            state.LastScannedRawMessageId,
            state.LastPollingCycleAt,
            confirmation,
            durableObservations);
    }

    public async Task SaveWebSocketScanAsync(
        DurableWebSocketResolutionScan scan,
        CancellationToken cancellationToken)
    {
        await ExecuteUnfencedAsync(
            scan.SessionId,
            false,
            async () =>
            {
                await SaveWebSocketScanCoreAsync(scan, cancellationToken);
                return true;
            },
            cancellationToken);
    }

    private async Task SaveWebSocketScanCoreAsync(
        DurableWebSocketResolutionScan scan,
        CancellationToken cancellationToken)
    {
        var state = await GetOrCreateStateAsync(scan.SessionId, cancellationToken);
        state.AdvanceScanner(scan.LastScannedRawMessageId);

        var rawMessageIds = scan.Validations
            .Select(validation => validation.Candidate.RawMessageId)
            .Distinct()
            .ToArray();
        var existing = rawMessageIds.Length == 0
            ? []
            : await dbContext.ResolutionObservations
                .AsNoTracking()
                .Where(observation => observation.RawMessageId != null
                    && rawMessageIds.Contains(observation.RawMessageId.Value))
                .Select(observation => new
                {
                    RawMessageId = observation.RawMessageId!.Value,
                    RawItemIndex = observation.RawItemIndex!.Value
                })
                .ToListAsync(cancellationToken);
        var existingKeys = existing
            .Select(item => (item.RawMessageId, item.RawItemIndex))
            .ToHashSet();

        foreach (var validation in scan.Validations)
        {
            var candidate = validation.Candidate;
            if (!existingKeys.Add((candidate.RawMessageId, candidate.RawItemIndex)))
                continue;

            var observation = new ResolutionObservationEntity(
                scan.SessionId,
                ResolutionObservationSource.WebSocket,
                candidate.ReceivedAt,
                validation.Status)
            {
                WinnerTokenId = validation.Winner?.TokenId,
                WinnerOutcome = validation.Winner?.Outcome,
                ExternalMarketId = candidate.ExternalMarketId,
                ConditionId = candidate.ConditionId,
                ErrorCode = validation.ErrorCode,
                ErrorMessage = validation.ErrorMessage,
                RawMessageId = candidate.RawMessageId,
                RawItemIndex = candidate.RawItemIndex,
                ConnectionEpoch = candidate.ConnectionEpoch
            };
            if (candidate.AssetIds is not null)
            {
                var index = 0;
                foreach (var tokenId in candidate.AssetIds)
                {
                    var isWinner = string.Equals(
                        tokenId,
                        validation.Winner?.TokenId,
                        StringComparison.Ordinal);
                    observation.Outcomes.Add(new ResolutionObservationOutcomeEntity(
                        index,
                        tokenId,
                        isWinner ? validation.Winner?.Outcome : null,
                        null,
                        isWinner));
                    index++;
                }
            }

            dbContext.ResolutionObservations.Add(observation);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<long> SaveGammaObservationAsync(
        CollectorSessionId sessionId,
        GammaTerminalResolutionObservation observation,
        CancellationToken cancellationToken)
    {
        var entity = new ResolutionObservationEntity(
            sessionId,
            ResolutionObservationSource.Gamma,
            observation.ObservedAt,
            observation.Status == GammaTerminalResolutionStatus.Terminal
                ? DurableResolutionObservationStatus.Terminal
                : DurableResolutionObservationStatus.NonTerminal)
        {
            WinnerTokenId = observation.Winner?.TokenId,
            WinnerOutcome = observation.Winner?.Outcome,
            ExternalEventId = observation.ExternalEventId,
            EventSlug = observation.EventSlug,
            ExternalMarketId = observation.ExternalMarketId,
            MarketSlug = observation.MarketSlug,
            ConditionId = observation.ConditionId,
            Closed = observation.Closed,
            AcceptingOrders = observation.AcceptingOrders,
            UmaResolutionStatus = observation.UmaResolutionStatus,
            ExternalClosedAt = observation.ExternalClosedAt
        };
        foreach (var outcome in observation.Outcomes)
        {
            entity.Outcomes.Add(new ResolutionObservationOutcomeEntity(
                outcome.OutcomeIndex,
                outcome.TokenId,
                outcome.Outcome,
                outcome.Price,
                observation.Winner?.TokenId == outcome.TokenId));
        }

        return await ExecuteUnfencedAsync(
            sessionId,
            0L,
            () => SaveObservationAsync(entity, cancellationToken),
            cancellationToken);
    }

    public async Task<long> SaveClobObservationAsync(
        CollectorSessionId sessionId,
        ClobTerminalResolutionObservation observation,
        CancellationToken cancellationToken)
    {
        var entity = new ResolutionObservationEntity(
            sessionId,
            ResolutionObservationSource.Clob,
            observation.ObservedAt,
            observation.Status == ClobTerminalResolutionStatus.Terminal
                ? DurableResolutionObservationStatus.Terminal
                : DurableResolutionObservationStatus.NonTerminal)
        {
            WinnerTokenId = observation.Winner?.TokenId,
            WinnerOutcome = observation.Winner?.Outcome,
            ConditionId = observation.ConditionId,
            Closed = observation.Closed,
            AcceptingOrders = observation.AcceptingOrders
        };
        foreach (var outcome in observation.Outcomes)
        {
            entity.Outcomes.Add(new ResolutionObservationOutcomeEntity(
                outcome.OutcomeIndex,
                outcome.TokenId,
                outcome.Outcome,
                outcome.Price,
                observation.Winner?.TokenId == outcome.TokenId));
        }

        return await ExecuteUnfencedAsync(
            sessionId,
            0L,
            () => SaveObservationAsync(entity, cancellationToken),
            cancellationToken);
    }

    public async Task<long> SaveFailureAsync(
        DurableResolutionFailure failure,
        CancellationToken cancellationToken)
    {
        var entity = new ResolutionObservationEntity(
            failure.SessionId,
            failure.Source,
            failure.ObservedAt,
            DurableResolutionObservationStatus.Failed)
        {
            ErrorCode = failure.ErrorCode,
            ErrorMessage = failure.ErrorMessage
        };

        return await ExecuteUnfencedAsync(
            failure.SessionId,
            0L,
            () => SaveObservationAsync(entity, cancellationToken),
            cancellationToken);
    }

    public async Task RecordPollingCycleAsync(
        CollectorSessionId sessionId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await ExecuteUnfencedAsync(
            sessionId,
            false,
            async () =>
            {
                var state = await GetOrCreateStateAsync(sessionId, cancellationToken);
                state.RecordPollingCycle(startedAt);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task SetConfirmationReferenceAsync(
        CollectorSessionId sessionId,
        ResolutionConfirmationReference confirmation,
        CancellationToken cancellationToken)
    {
        await ExecuteUnfencedAsync(
            sessionId,
            false,
            async () =>
            {
                var state = await GetOrCreateStateAsync(sessionId, cancellationToken);
                state.Confirm(
                    confirmation.PrimaryObservationId,
                    confirmation.ConfirmingObservationId,
                    confirmation.ConfirmedAt);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    private async Task<long> SaveObservationAsync(
        ResolutionObservationEntity entity,
        CancellationToken cancellationToken)
    {
        dbContext.ResolutionObservations.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    private static DurableResolutionObservation MapObservation(
        ResolutionObservationEntity observation)
    {
        var winner = observation.WinnerTokenId is not null
            && observation.WinnerOutcome is not null
                ? new ResolutionWinner(
                    observation.WinnerTokenId,
                    observation.WinnerOutcome)
                : null;
        var outcomes = observation.Outcomes
            .OrderBy(outcome => outcome.OutcomeIndex)
            .Select(outcome => new DurableResolutionOutcome(
                outcome.OutcomeIndex,
                outcome.TokenId,
                outcome.Outcome,
                outcome.Price,
                outcome.IsWinner))
            .ToArray();

        return new DurableResolutionObservation(
            observation.Id,
            observation.Source,
            observation.ObservedAt,
            observation.Status,
            winner,
            observation.ExternalEventId,
            observation.EventSlug,
            observation.ExternalMarketId,
            observation.MarketSlug,
            observation.ConditionId,
            observation.Closed,
            observation.AcceptingOrders,
            observation.UmaResolutionStatus,
            observation.ExternalClosedAt,
            observation.ErrorCode,
            observation.ErrorMessage,
            observation.RawMessageId,
            observation.RawItemIndex,
            observation.ConnectionEpoch,
            outcomes);
    }

    private async Task<ResolutionStateEntity> GetOrCreateStateAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        var tracked = dbContext.ResolutionStates.Local
            .SingleOrDefault(state => state.SessionId == sessionId);
        if (tracked is not null)
            return tracked;

        var state = await dbContext.ResolutionStates
            .SingleOrDefaultAsync(current => current.SessionId == sessionId, cancellationToken);
        if (state is not null)
            return state;

        state = new ResolutionStateEntity(sessionId);
        dbContext.ResolutionStates.Add(state);
        return state;
    }

    private async Task<TResult> ExecuteUnfencedAsync<TResult>(
        CollectorSessionId sessionId,
        TResult fencedResult,
        Func<Task<TResult>> write,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
            return await write();

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);
        var fenced = await CollectorSessionWriteFence.LockAsync(
            dbContext,
            transaction,
            [sessionId],
            cancellationToken);
        if (fenced.Contains(sessionId))
        {
            await transaction.RollbackAsync(cancellationToken);
            return fencedResult;
        }

        var result = await write();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
