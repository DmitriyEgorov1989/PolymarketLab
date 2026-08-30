using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Core.Ports.Enums;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorScheduling;

public sealed class CollectorScheduler(
    IMarketCollectionSource marketSource,
    ICollectorSessionRepository sessionRepository,
    ICollectorRuntime runtime,
    CollectorBoundaryCheckRegistry boundaryChecks,
    TimeProvider timeProvider) : ICollectorScheduler
{
    private const int MaximumUpdateAttempts = 3;
    private static readonly TimeSpan PreparationLeadTime = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RegularReadinessLeadTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CompensationTimeout = TimeSpan.FromSeconds(10);

    public async Task<Result<CollectorSessionAggregate, Error>> PrepareAsync(
        CollectorSessionAggregate session,
        CollectionMarket market,
        CancellationToken cancellationToken)
    {
        if (session.Status != CollectorSessionStatus.Scheduled)
            return session;

        var now = timeProvider.GetUtcNow();
        var eventStartsAt = session.EventStartsAt;
        if (eventStartsAt is null || now >= eventStartsAt.Value)
            return await InvalidateAsync(session, cancellationToken);
        if (now < eventStartsAt.Value - PreparationLeadTime)
            return session;
        if (!MatchesSnapshot(session, market) || !IsReadyForPreparation(market))
            return await InvalidateAsync(session, cancellationToken);

        var transition = session.BeginPreparation(now);
        if (transition.IsFailure)
            return transition.Error;

        var updateResult = await sessionRepository.TryUpdateAsync(
            session,
            CollectorSessionStatus.Scheduled,
            cancellationToken);
        if (updateResult.IsFailure)
            return updateResult.Error;
        if (updateResult.Value == CollectorSessionUpdateStatus.ConcurrencyConflict)
            return await ResolveConflictAsync(session, cancellationToken);

        var readinessDeadline = now < eventStartsAt.Value - RegularReadinessLeadTime
            ? eventStartsAt.Value - RegularReadinessLeadTime
            : eventStartsAt.Value;
        UnitResult<Error> runtimeResult;
        try
        {
            runtimeResult = await runtime.StartAsync(
                new CollectorRuntimeStartRequest(session.Id, market, readinessDeadline),
                cancellationToken);
        }
        catch
        {
            using var compensation = new CancellationTokenSource(
                CompensationTimeout,
                timeProvider);
            var compensationResult = await InvalidateAsync(session, compensation.Token);
            if (compensationResult.IsFailure)
                return compensationResult.Error;

            throw;
        }
        if (runtimeResult.IsSuccess)
            return session;

        var invalidationResult = await InvalidateAsync(session, cancellationToken);
        return invalidationResult.IsFailure
            ? invalidationResult.Error
            : runtimeResult.Error;
    }

    public async Task<UnitResult<Error>> TickAsync(CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetExclusiveAsync(cancellationToken);
        if (session is null
            || session.Status is CollectorSessionStatus.Invalidating
                or CollectorSessionStatus.Stopping)
            return UnitResult.Success<Error>();

        if (session.Status is CollectorSessionStatus.Starting
            or CollectorSessionStatus.Running)
        {
            return await CheckReadinessBoundaryAsync(session, cancellationToken);
        }
        if (session.Status != CollectorSessionStatus.Scheduled)
            return UnitResult.Success<Error>();

        var now = timeProvider.GetUtcNow();
        if (session.EventStartsAt is null || now >= session.EventStartsAt.Value)
        {
            var staleResult = await InvalidateAsync(session, cancellationToken);
            return staleResult.IsFailure
                ? UnitResult.Failure(staleResult.Error)
                : UnitResult.Success<Error>();
        }
        if (now < session.EventStartsAt.Value - PreparationLeadTime)
            return UnitResult.Success<Error>();

        var marketResult = await marketSource.GetByIdAsync(
            session.MarketId,
            cancellationToken);
        if (marketResult.IsFailure)
        {
            if (marketResult.Error.Type != ErrorType.Conflict)
                return UnitResult.Failure(marketResult.Error);

            var unavailableResult = await InvalidateAsync(session, cancellationToken);
            return unavailableResult.IsFailure
                ? UnitResult.Failure(unavailableResult.Error)
                : UnitResult.Success<Error>();
        }

        if (marketResult.Value is null)
        {
            var missingResult = await InvalidateAsync(session, cancellationToken);
            return missingResult.IsFailure
                ? UnitResult.Failure(missingResult.Error)
                : UnitResult.Success<Error>();
        }

        var preparationResult = await PrepareAsync(
            session,
            marketResult.Value,
            cancellationToken);
        return preparationResult.IsFailure
            ? UnitResult.Failure(preparationResult.Error)
            : UnitResult.Success<Error>();
    }

    private async Task<Result<CollectorSessionAggregate, Error>> InvalidateAsync(
        CollectorSessionAggregate initialSession,
        CancellationToken cancellationToken)
    {
        CollectorSessionAggregate? session = initialSession;
        for (var attempt = 0; attempt < MaximumUpdateAttempts; attempt++)
        {
            if (session.Status == CollectorSessionStatus.Invalidating)
                return session;
            if (!IsExclusive(session.Status))
                return session;

            var expectedStatus = session.Status;
            var transition = session.BeginInvalidation();
            if (transition.IsFailure)
                return transition.Error;

            var updateResult = await sessionRepository.TryUpdateAsync(
                session,
                expectedStatus,
                cancellationToken);
            if (updateResult.IsFailure)
                return updateResult.Error;
            if (updateResult.Value == CollectorSessionUpdateStatus.Updated)
            {
                if (expectedStatus is CollectorSessionStatus.Starting
                    or CollectorSessionStatus.Running)
                {
                    await runtime.StopAsync(session.Id, cancellationToken);
                }

                return session;
            }

            session = await sessionRepository.GetByIdAsync(
                initialSession.Id,
                cancellationToken);
            if (session is null)
                return CollectorSchedulingErrors.StateTransitionConflict(initialSession.Id);
        }

        if (session.Status == CollectorSessionStatus.Invalidating
            || !IsExclusive(session.Status))
        {
            return session;
        }

        return CollectorSchedulingErrors.StateTransitionConflict(initialSession.Id);
    }

    private async Task<UnitResult<Error>> CheckReadinessBoundaryAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        if (session.EventStartsAt is null || session.StartedAt is null)
            return await InvalidateAsUnitAsync(session, cancellationToken);

        var eventStartsAt = session.EventStartsAt.Value;
        var readinessDeadline = session.StartedAt.Value
            < eventStartsAt - RegularReadinessLeadTime
            ? eventStartsAt - RegularReadinessLeadTime
            : eventStartsAt;
        var now = timeProvider.GetUtcNow();
        if (now < readinessDeadline)
            return UnitResult.Success<Error>();
        if (boundaryChecks.IsReadinessVerified(session.Id))
            return UnitResult.Success<Error>();

        var marketResult = await marketSource.GetByIdAsync(
            session.MarketId,
            cancellationToken);
        if (marketResult.IsFailure
            || marketResult.Value is null
            || !MatchesSnapshot(session, marketResult.Value)
            || !IsReadyForPreparation(marketResult.Value))
        {
            return await InvalidateAsUnitAsync(session, cancellationToken);
        }

        if (session.Status == CollectorSessionStatus.Starting)
            return await InvalidateAsUnitAsync(session, cancellationToken);

        boundaryChecks.MarkReadinessVerified(session.Id);
        return UnitResult.Success<Error>();
    }

    private async Task<UnitResult<Error>> InvalidateAsUnitAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        var result = await InvalidateAsync(session, cancellationToken);
        return result.IsFailure
            ? UnitResult.Failure(result.Error)
            : UnitResult.Success<Error>();
    }

    private async Task<Result<CollectorSessionAggregate, Error>> ResolveConflictAsync(
        CollectorSessionAggregate session,
        CancellationToken cancellationToken)
    {
        var current = await sessionRepository.GetByIdAsync(session.Id, cancellationToken);
        return current is null
            ? CollectorSchedulingErrors.StateTransitionConflict(session.Id)
            : current;
    }

    private static bool IsReadyForPreparation(CollectionMarket market) =>
        market.Active
        && !market.Closed
        && market.AcceptingOrders
        && market.OrderBookEnabled;

    private static bool MatchesSnapshot(
        CollectorSessionAggregate session,
        CollectionMarket market)
    {
        if (session.MarketId != market.MarketId
            || !string.Equals(session.ExternalEventId, market.ExternalEventId, StringComparison.Ordinal)
            || !string.Equals(session.EventSlug, market.EventSlug, StringComparison.Ordinal)
            || !string.Equals(session.ExternalMarketId, market.ExternalMarketId, StringComparison.Ordinal)
            || !string.Equals(session.MarketSlug, market.MarketSlug, StringComparison.Ordinal)
            || !string.Equals(session.ConditionId, market.ConditionId, StringComparison.Ordinal)
            || session.EventStartsAt != market.EventStartsAt
            || session.EventEndsAt != market.EventEndsAt)
        {
            return false;
        }

        return session.Tokens
            .Select(token => (token.TokenId, token.Outcome, token.OutcomeIndex))
            .SequenceEqual(market.Tokens.Select(token =>
                (token.TokenId, token.Outcome, token.OutcomeIndex)));
    }

    private static bool IsExclusive(CollectorSessionStatus status) =>
        status is CollectorSessionStatus.Scheduled
            or CollectorSessionStatus.Starting
            or CollectorSessionStatus.Running
            or CollectorSessionStatus.Stopping
            or CollectorSessionStatus.Invalidating;
}
