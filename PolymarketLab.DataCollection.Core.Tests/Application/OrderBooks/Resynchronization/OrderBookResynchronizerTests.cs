using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization.Models;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using BestBidAskRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.BestBidAskRecord;
using BookLevelRecord = PolymarketLab.DataCollection.Core.Application.Normalization.Models.BookLevelRecord;
using BookSnapshotRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.BookSnapshotRecord;
using NormalizedOrderBookSide = PolymarketLab.DataCollection.Core.Application.Normalization.Models.OrderBookSide;
using PriceChangeRecord = PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models.PriceChangeRecord;
using TradeSide = PolymarketLab.DataCollection.Core.Application.Normalization.Models.TradeSide;

namespace PolymarketLab.DataCollection.Core.Tests.Application.OrderBooks.Resynchronization;

public sealed class OrderBookResynchronizerTests
{
    [Fact]
    public async Task ResynchronizeAsync_SuspectState_ShouldReplaceStateAndPreserveArchivePosition()
    {
        var registry = CreateRegistry();
        var state = CreateSuspectState(registry);
        var position = state.EventPosition;
        var previousBids = state.Bids;
        var previousAsks = state.Asks;
        var snapshot = CreateRestSnapshot();
        var sut = new OrderBookResynchronizer(registry, StubSnapshotSource.Success(snapshot));

        var result = await sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.CrossedBook,
            CancellationToken.None);

        result.Outcome.Should().Be(OrderBookResyncOutcome.Synchronized);
        result.Attempts.Should().Be(1);
        result.Snapshot.Should().BeSameAs(snapshot);
        result.Error.Should().BeNull();
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.Bids.Values.Should().Equal(new OrderBookLevel(0.4m, 12m));
        state.Asks.Values.Should().Equal(new OrderBookLevel(0.6m, 18m));
        state.Bids.Should().NotContainKey(0.3m);
        state.Bids.Should().NotContainKey(0.7m);
        state.Asks.Should().NotContainKey(0.9m);
        previousBids.Keys.Should().Equal(0.3m, 0.7m);
        previousAsks.Keys.Should().Equal(0.6m, 0.9m);
        state.BestBid.Should().Be(0.4m);
        state.BestAsk.Should().Be(0.6m);
        state.Spread.Should().Be(0.2m);
        state.TickSize.Should().Be(0.01m);
        state.MarketConditionId.Should().Be("condition-rest");
        state.Hash.Should().Be("hash-rest");
        state.SourceTimestamp.Should().Be(2000);
        position.Should().NotBeNull();
        state.EventPosition.Should().Be(position!);
        state.IntegrityIssue.Should().BeNull();
    }

    [Fact]
    public async Task ResynchronizeAsync_SameHash_ShouldStillReplaceAllLevels()
    {
        var registry = CreateRegistry();
        var state = CreateSynchronizedState(registry, hash: "shared-hash");
        var snapshot = CreateRestSnapshot(
            hash: "shared-hash",
            bids: [new OrderBookSnapshotLevel(0.3m, 30m)],
            asks: [new OrderBookSnapshotLevel(0.7m, 40m)]);
        var sut = new OrderBookResynchronizer(registry, StubSnapshotSource.Success(snapshot));

        var result = await sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.Manual,
            CancellationToken.None);

        result.Outcome.Should().Be(OrderBookResyncOutcome.Synchronized);
        state.Hash.Should().Be("shared-hash");
        state.Bids.Values.Should().Equal(new OrderBookLevel(0.3m, 30m));
        state.Asks.Values.Should().Equal(new OrderBookLevel(0.7m, 40m));
        state.Bids.Should().NotContainKey(0.4m);
        state.Asks.Should().NotContainKey(0.6m);
    }

    [Fact]
    public void Apply_PriceChangeWithoutHash_ShouldPreserveLastExternalHash()
    {
        var registry = CreateRegistry();
        var state = CreateSynchronizedState(registry, hash: "snapshot-hash");

        state.Apply([
            new PriceChangeRecord(
                2,
                0,
                2,
                "asset",
                1500,
                TradeSide.Buy,
                0.5m,
                15m,
                null,
                0.5m,
                0.6m,
                0)
        ]);

        state.Hash.Should().Be("snapshot-hash");
    }

    [Fact]
    public async Task ResynchronizeAsync_ManualFailure_ShouldRestoreSynchronizedStatus()
    {
        var registry = CreateRegistry();
        var state = CreateSynchronizedState(registry);
        var expectedError = new Error("snapshot.failed", "Snapshot failed.", ErrorType.Failure);
        var sut = new OrderBookResynchronizer(
            registry,
            StubSnapshotSource.Failure(expectedError));

        var result = await sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.Manual,
            CancellationToken.None);

        result.Error.Should().Be(expectedError);
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.Hash.Should().Be("hash-local");
    }

    [Fact]
    public async Task ResynchronizeAsync_ManualFailure_ShouldRestoreInitialDiagnostics()
    {
        var registry = CreateRegistry();
        var state = CreateSynchronizedState(registry);
        var completion = new TaskCompletionSource<Result<OrderBookSnapshot, Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = new OrderBookResynchronizer(
            registry,
            new StubSnapshotSource((_, _, _) => completion.Task));
        var resynchronization = sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.Manual,
            CancellationToken.None);
        state.Apply(new BestBidAskRecord(
            2,
            0,
            2,
            "asset",
            1500,
            0.1m,
            0.6m,
            0.5m));
        state.IntegrityIssue.Should().NotBeNull();
        completion.SetResult(Result.Failure<OrderBookSnapshot, Error>(new Error(
            "snapshot.failed",
            "Snapshot failed.",
            ErrorType.Failure)));

        await resynchronization;

        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.IntegrityIssue.Should().BeNull();
    }

    [Fact]
    public async Task ResynchronizeAsync_ReconnectFailure_ShouldMarkSynchronizedStateStale()
    {
        var registry = CreateRegistry();
        var state = CreateSynchronizedState(registry);
        var sut = new OrderBookResynchronizer(
            registry,
            StubSnapshotSource.Failure(new Error(
                "snapshot.failed",
                "Snapshot failed.",
                ErrorType.Failure)));

        await sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.Reconnect,
            CancellationToken.None);

        state.Status.Should().Be(OrderBookSyncStatus.Stale);
    }

    [Theory]
    [InlineData(OrderBookResyncReason.Manual)]
    [InlineData(OrderBookResyncReason.Reconnect)]
    public async Task ResynchronizeAsync_UninitializedState_ShouldAllowInitialization(
        OrderBookResyncReason reason)
    {
        var registry = CreateRegistry();
        var state = registry.GetOrAdd("asset");
        var sut = new OrderBookResynchronizer(
            registry,
            StubSnapshotSource.Success(CreateRestSnapshot(sourceTimestamp: 1000)));

        var result = await sut.ResynchronizeAsync("asset", reason, CancellationToken.None);

        result.Outcome.Should().Be(OrderBookResyncOutcome.Synchronized);
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.Bids.Should().ContainSingle();
        state.Asks.Should().ContainSingle();
    }

    [Fact]
    public async Task ResynchronizeAsync_ManualFailureFromUninitialized_ShouldRestoreStatusAndRejectDeltas()
    {
        var registry = CreateRegistry();
        var state = registry.GetOrAdd("asset");
        var completion = new TaskCompletionSource<Result<OrderBookSnapshot, Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = new OrderBookResynchronizer(
            registry,
            new StubSnapshotSource((_, _, _) => completion.Task));
        var resynchronization = sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.Manual,
            CancellationToken.None);

        var deltaAction = () => state.Apply([
            new PriceChangeRecord(
                1,
                0,
                1,
                "asset",
                1000,
                TradeSide.Buy,
                0.4m,
                10m,
                "hash",
                0.4m,
                0.6m,
                0)
        ]);
        deltaAction.Should().Throw<InvalidOperationException>();
        completion.SetResult(Result.Failure<OrderBookSnapshot, Error>(new Error(
            "snapshot.failed",
            "Snapshot failed.",
            ErrorType.Failure)));

        await resynchronization;
        state.Status.Should().Be(OrderBookSyncStatus.Uninitialized);
    }

    [Theory]
    [InlineData(OrderBookResyncReason.BestBidMismatch)]
    [InlineData(OrderBookResyncReason.BestAskMismatch)]
    [InlineData(OrderBookResyncReason.SpreadMismatch)]
    [InlineData(OrderBookResyncReason.TickSizeMismatch)]
    [InlineData(OrderBookResyncReason.CrossedBook)]
    [InlineData(OrderBookResyncReason.HashMismatch)]
    public async Task ResynchronizeAsync_SuspectReason_ShouldBeAccepted(
        OrderBookResyncReason reason)
    {
        var registry = CreateRegistry();
        var state = CreateSuspectState(registry);
        var sut = new OrderBookResynchronizer(
            registry,
            StubSnapshotSource.Success(CreateRestSnapshot()));

        var result = await sut.ResynchronizeAsync("asset", reason, CancellationToken.None);

        result.Outcome.Should().Be(OrderBookResyncOutcome.Synchronized);
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
    }

    [Theory]
    [InlineData(OrderBookResyncReason.GapDetected)]
    [InlineData(OrderBookResyncReason.StaleState)]
    [InlineData(OrderBookResyncReason.HashMismatch)]
    public async Task ResynchronizeAsync_StaleReason_ShouldBeAccepted(
        OrderBookResyncReason reason)
    {
        var registry = CreateRegistry();
        var state = CreateSynchronizedState(registry);
        state.MarkStale(new OrderBookIntegrityIssue(
            OrderBookIntegrityIssueType.GapDetected,
            "Gap detected.",
            1,
            DateTimeOffset.UtcNow));
        var sut = new OrderBookResynchronizer(
            registry,
            StubSnapshotSource.Success(CreateRestSnapshot()));

        var result = await sut.ResynchronizeAsync("asset", reason, CancellationToken.None);

        result.Outcome.Should().Be(OrderBookResyncOutcome.Synchronized);
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
    }

    [Fact]
    public async Task ResynchronizeAsync_StaleState_ShouldRestoreState()
    {
        var registry = CreateRegistry();
        var state = registry.GetOrAdd("asset");
        state.MarkStale(new OrderBookIntegrityIssue(
            OrderBookIntegrityIssueType.GapDetected,
            "Gap detected.",
            null,
            DateTimeOffset.UtcNow));
        var sut = new OrderBookResynchronizer(
            registry,
            StubSnapshotSource.Success(CreateRestSnapshot(sourceTimestamp: 1000)));

        var result = await sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.GapDetected,
            CancellationToken.None);

        result.Outcome.Should().Be(OrderBookResyncOutcome.Synchronized);
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
    }

    [Fact]
    public async Task ResynchronizeAsync_SourceFailure_ShouldPreserveErrorAndMarkStateStale()
    {
        var registry = CreateRegistry();
        var state = CreateSuspectState(registry);
        var expectedError = new Error("snapshot.failed", "Snapshot failed.", ErrorType.Failure);
        var sut = new OrderBookResynchronizer(
            registry,
            StubSnapshotSource.Failure(expectedError));

        var result = await sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.CrossedBook,
            CancellationToken.None);

        result.Outcome.Should().Be(OrderBookResyncOutcome.Failed);
        result.Attempts.Should().Be(1);
        result.Error.Should().Be(expectedError);
        result.Snapshot.Should().BeNull();
        state.Status.Should().Be(OrderBookSyncStatus.Stale);
    }

    [Fact]
    public async Task ResynchronizeAsync_StateChangedDuringRequest_ShouldFetchFreshSnapshot()
    {
        var registry = CreateRegistry();
        var state = CreateSuspectState(registry);
        var source = new StubSnapshotSource((call, _, _) =>
        {
            if (call == 1)
            {
                state.Apply([
                    new PriceChangeRecord(
                        2,
                        0,
                        2,
                        "asset",
                        1500,
                        TradeSide.Sell,
                        0.8m,
                        20m,
                        "hash-change",
                        0.7m,
                        0.8m,
                        0)
                ]);
            }

            return Task.FromResult(Result.Success<OrderBookSnapshot, Error>(
                CreateRestSnapshot(sourceTimestamp: call == 1 ? 1600 : 2000)));
        });
        var sut = new OrderBookResynchronizer(registry, source);

        var result = await sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.CrossedBook,
            CancellationToken.None);

        result.Outcome.Should().Be(OrderBookResyncOutcome.Synchronized);
        result.Attempts.Should().Be(2);
        source.Calls.Should().Be(2);
        state.SourceTimestamp.Should().Be(2000);
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
    }

    [Fact]
    public async Task ResynchronizeAsync_CrossedSnapshot_ShouldRejectWithoutPartialReplacement()
    {
        var registry = CreateRegistry();
        var state = CreateSuspectState(registry);
        var originalBids = state.Bids.Values.ToArray();
        var snapshot = CreateRestSnapshot(
            bids: [new OrderBookSnapshotLevel(0.8m, 10m)],
            asks: [new OrderBookSnapshotLevel(0.7m, 10m)]);
        var sut = new OrderBookResynchronizer(registry, StubSnapshotSource.Success(snapshot));

        var result = await sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.CrossedBook,
            CancellationToken.None);

        result.Outcome.Should().Be(OrderBookResyncOutcome.Failed);
        result.Error!.Code.Should().Be("OrderBook.Resynchronization.InvalidSnapshot");
        state.Bids.Values.Should().Equal(originalBids);
        state.Status.Should().Be(OrderBookSyncStatus.Stale);
    }

    [Fact]
    public async Task ResynchronizeAsync_OlderSnapshot_ShouldRejectWithoutPartialReplacement()
    {
        var registry = CreateRegistry();
        var state = CreateSuspectState(registry);
        var originalHash = state.Hash;
        var sut = new OrderBookResynchronizer(
            registry,
            StubSnapshotSource.Success(CreateRestSnapshot(sourceTimestamp: 999)));

        var result = await sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.CrossedBook,
            CancellationToken.None);

        result.Outcome.Should().Be(OrderBookResyncOutcome.Failed);
        result.Error!.Code.Should().Be("OrderBook.Resynchronization.InvalidSnapshot");
        state.Hash.Should().Be(originalHash);
        state.SourceTimestamp.Should().Be(1000);
        state.Status.Should().Be(OrderBookSyncStatus.Stale);
    }

    [Fact]
    public async Task ResynchronizeAsync_StateKeepsChanging_ShouldStopAfterMaximumAttempts()
    {
        var registry = CreateRegistry();
        var state = CreateSuspectState(registry);
        var source = new StubSnapshotSource((call, _, _) =>
        {
            state.Apply([
                new PriceChangeRecord(
                    call + 1,
                    0,
                    call + 1,
                    "asset",
                    1000 + call,
                    TradeSide.Sell,
                    0.6m,
                    20m + call,
                    $"hash-{call}",
                    0.7m,
                    0.6m,
                    0)
            ]);
            return Task.FromResult(Result.Success<OrderBookSnapshot, Error>(
                CreateRestSnapshot(sourceTimestamp: 2000 + call)));
        });
        var sut = new OrderBookResynchronizer(registry, source);

        var result = await sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.CrossedBook,
            CancellationToken.None);

        result.Outcome.Should().Be(OrderBookResyncOutcome.Failed);
        result.Attempts.Should().Be(3);
        result.Error!.Code.Should().Be("OrderBook.Resynchronization.StateChanged");
        source.Calls.Should().Be(3);
        state.Status.Should().Be(OrderBookSyncStatus.Stale);
    }

    [Fact]
    public async Task ResynchronizeAsync_OverlappingRequest_ShouldRejectSecondOperation()
    {
        var registry = CreateRegistry();
        var state = CreateSuspectState(registry);
        var completion = new TaskCompletionSource<Result<OrderBookSnapshot, Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubSnapshotSource((_, _, _) => completion.Task);
        var sut = new OrderBookResynchronizer(registry, source);

        var firstTask = sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.CrossedBook,
            CancellationToken.None);
        state.Status.Should().Be(OrderBookSyncStatus.Resynchronizing);
        state.Apply(new BestBidAskRecord(
            2,
            0,
            2,
            "asset",
            1500,
            0.1m,
            0.6m,
            0.5m));
        state.Status.Should().Be(OrderBookSyncStatus.Resynchronizing);

        var second = await sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.CrossedBook,
            CancellationToken.None);

        second.Outcome.Should().Be(OrderBookResyncOutcome.Failed);
        second.Error!.Code.Should().Be("OrderBook.Resynchronization.InvalidState");
        source.Calls.Should().Be(1);

        var expectedError = new Error("snapshot.failed", "Snapshot failed.", ErrorType.Failure);
        completion.SetResult(Result.Failure<OrderBookSnapshot, Error>(expectedError));
        var first = await firstTask;
        first.Error.Should().Be(expectedError);
        state.Status.Should().Be(OrderBookSyncStatus.Stale);
    }

    [Fact]
    public async Task ResynchronizeAsync_WebSocketSnapshotWins_ShouldIgnoreLateRestFailure()
    {
        var registry = CreateRegistry();
        var state = CreateSuspectState(registry);
        var completion = new TaskCompletionSource<Result<OrderBookSnapshot, Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubSnapshotSource((_, _, _) => completion.Task);
        var sut = new OrderBookResynchronizer(registry, source);
        var resynchronization = sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.CrossedBook,
            CancellationToken.None);

        state.Apply(new BookSnapshotRecord(
            2,
            0,
            2,
            "asset",
            "condition-websocket",
            2000,
            "hash-websocket",
            0.01m,
            [new BookLevelRecord(NormalizedOrderBookSide.Bid, 0, 0.4m, 10m)],
            [new BookLevelRecord(NormalizedOrderBookSide.Ask, 0, 0.6m, 20m)]));
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        var expectedError = new Error("snapshot.failed", "Snapshot failed.", ErrorType.Failure);
        completion.SetResult(Result.Failure<OrderBookSnapshot, Error>(expectedError));

        var result = await resynchronization;

        result.Error.Should().Be(expectedError);
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
        state.Hash.Should().Be("hash-websocket");
    }

    [Fact]
    public async Task ResynchronizeAsync_DeltaDuringStaleRecovery_ShouldRemainResynchronizing()
    {
        var registry = CreateRegistry();
        var state = registry.GetOrAdd("asset");
        state.Apply(new BookSnapshotRecord(
            1,
            0,
            1,
            "asset",
            "condition-local",
            1000,
            "hash-local",
            0.01m,
            [new BookLevelRecord(NormalizedOrderBookSide.Bid, 0, 0.4m, 10m)],
            [new BookLevelRecord(NormalizedOrderBookSide.Ask, 0, 0.6m, 20m)]));
        state.MarkStale(new OrderBookIntegrityIssue(
            OrderBookIntegrityIssueType.GapDetected,
            "Gap detected.",
            1,
            DateTimeOffset.UtcNow));
        var completion = new TaskCompletionSource<Result<OrderBookSnapshot, Error>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = new OrderBookResynchronizer(
            registry,
            new StubSnapshotSource((_, _, _) => completion.Task));
        var resynchronization = sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.GapDetected,
            CancellationToken.None);

        state.Apply([
            new PriceChangeRecord(
                2,
                0,
                2,
                "asset",
                1500,
                TradeSide.Buy,
                0.5m,
                15m,
                "hash-change",
                0.5m,
                0.6m,
                0)
        ]);

        state.Status.Should().Be(OrderBookSyncStatus.Resynchronizing);
        state.IntegrityIssue!.Type.Should().Be(OrderBookIntegrityIssueType.GapDetected);
        completion.SetResult(Result.Success<OrderBookSnapshot, Error>(CreateRestSnapshot()));
        var result = await resynchronization;
        result.Outcome.Should().Be(OrderBookResyncOutcome.Synchronized);
        state.Status.Should().Be(OrderBookSyncStatus.Synchronized);
    }

    [Fact]
    public async Task ResynchronizeAsync_MissingOrInvalidState_ShouldNotCallSource()
    {
        var registry = CreateRegistry();
        registry.GetOrAdd("uninitialized");
        var source = StubSnapshotSource.Success(CreateRestSnapshot());
        var sut = new OrderBookResynchronizer(registry, source);

        var missing = await sut.ResynchronizeAsync(
            "missing",
            OrderBookResyncReason.Manual,
            CancellationToken.None);
        var invalid = await sut.ResynchronizeAsync(
            "uninitialized",
            OrderBookResyncReason.BestBidMismatch,
            CancellationToken.None);

        missing.Error!.Code.Should().Be("OrderBook.Resynchronization.StateNotFound");
        invalid.Error!.Code.Should().Be("OrderBook.Resynchronization.InvalidState");
        missing.Attempts.Should().Be(0);
        invalid.Attempts.Should().Be(0);
        source.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ResynchronizeAsync_CallerCancellation_ShouldMarkStateStaleAndRethrow()
    {
        var registry = CreateRegistry();
        var state = CreateSuspectState(registry);
        var source = new StubSnapshotSource(async (_, _, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable.");
        });
        var sut = new OrderBookResynchronizer(registry, source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => sut.ResynchronizeAsync(
            "asset",
            OrderBookResyncReason.CrossedBook,
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        state.Status.Should().Be(OrderBookSyncStatus.Stale);
    }

    [Fact]
    public void Registry_GetOrAdd_ShouldReturnSameCaseSensitiveState()
    {
        var registry = CreateRegistry();

        var first = registry.GetOrAdd("asset");
        var second = registry.GetOrAdd("asset");
        var differentCase = registry.GetOrAdd("ASSET");

        second.Should().BeSameAs(first);
        differentCase.Should().NotBeSameAs(first);
        registry.TryGet("asset", out var found).Should().BeTrue();
        found.Should().BeSameAs(first);
    }

    private static OrderBookStateRegistry CreateRegistry()
    {
        return new OrderBookStateRegistry(TimeProvider.System);
    }

    private static OrderBookState CreateSuspectState(IOrderBookStateRegistry registry)
    {
        var state = registry.GetOrAdd("asset");
        state.Apply(new BookSnapshotRecord(
            1,
            0,
            1,
            "asset",
            "condition-local",
            1000,
            "hash-local",
            0.01m,
            [
                new BookLevelRecord(NormalizedOrderBookSide.Bid, 0, 0.3m, 5m),
                new BookLevelRecord(NormalizedOrderBookSide.Bid, 1, 0.7m, 10m)
            ],
            [
                new BookLevelRecord(NormalizedOrderBookSide.Ask, 0, 0.6m, 20m),
                new BookLevelRecord(NormalizedOrderBookSide.Ask, 1, 0.9m, 25m)
            ]));
        state.Status.Should().Be(OrderBookSyncStatus.Suspect);
        return state;
    }

    private static OrderBookState CreateSynchronizedState(
        IOrderBookStateRegistry registry,
        string hash = "hash-local")
    {
        var state = registry.GetOrAdd("asset");
        state.Apply(new BookSnapshotRecord(
            1,
            0,
            1,
            "asset",
            "condition-local",
            1000,
            hash,
            0.01m,
            [new BookLevelRecord(NormalizedOrderBookSide.Bid, 0, 0.4m, 10m)],
            [new BookLevelRecord(NormalizedOrderBookSide.Ask, 0, 0.6m, 20m)]));
        return state;
    }

    private static OrderBookSnapshot CreateRestSnapshot(
        long sourceTimestamp = 2000,
        string hash = "hash-rest",
        IReadOnlyCollection<OrderBookSnapshotLevel>? bids = null,
        IReadOnlyCollection<OrderBookSnapshotLevel>? asks = null)
    {
        return new OrderBookSnapshot(
            "condition-rest",
            "asset",
            sourceTimestamp,
            hash,
            bids ?? [new OrderBookSnapshotLevel(0.4m, 12m)],
            asks ?? [new OrderBookSnapshotLevel(0.6m, 18m)],
            1m,
            0.01m,
            false,
            0.5m);
    }

    private sealed class StubSnapshotSource(
        Func<int, string, CancellationToken, Task<Result<OrderBookSnapshot, Error>>> handler)
        : IOrderBookSnapshotSource
    {
        public int Calls { get; private set; }

        public Task<Result<OrderBookSnapshot, Error>> GetAsync(
            string assetId,
            CancellationToken cancellationToken)
        {
            Calls++;
            return handler(Calls, assetId, cancellationToken);
        }

        public static StubSnapshotSource Success(OrderBookSnapshot snapshot)
        {
            return new StubSnapshotSource((_, _, _) => Task.FromResult(
                Result.Success<OrderBookSnapshot, Error>(snapshot)));
        }

        public static StubSnapshotSource Failure(Error error)
        {
            return new StubSnapshotSource((_, _, _) => Task.FromResult(
                Result.Failure<OrderBookSnapshot, Error>(error)));
        }
    }
}
