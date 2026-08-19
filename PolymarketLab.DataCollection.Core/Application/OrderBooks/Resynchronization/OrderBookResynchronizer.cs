using PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization.Models;
using PolymarketLab.DataCollection.Core.Ports;

namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization;

/// <summary>Восстанавливает локальный стакан из официального REST-снимка.</summary>
/// <param name="stateRegistry">Реестр состояний, доступных для восстановления.</param>
/// <param name="snapshotSource">Источник официальных полных снимков.</param>
public sealed class OrderBookResynchronizer(
    IOrderBookStateRegistry stateRegistry,
    IOrderBookSnapshotSource snapshotSource) : IOrderBookResynchronizer
{
    private const int MaximumAttempts = 3;

    /// <inheritdoc />
    public async Task<OrderBookResyncResult> ResynchronizeAsync(
        string assetId,
        OrderBookResyncReason reason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));

        if (!stateRegistry.TryGet(assetId, out var state))
        {
            return OrderBookResyncResult.Failed(
                assetId,
                reason,
                0,
                OrderBookResynchronizationErrors.StateNotFound(assetId));
        }
        if (!state.TryBeginResynchronization(reason, out var token))
        {
            return OrderBookResyncResult.Failed(
                assetId,
                reason,
                0,
                OrderBookResynchronizationErrors.InvalidState(assetId));
        }

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                var snapshotResult = await snapshotSource.GetAsync(assetId, cancellationToken);
                if (snapshotResult.IsFailure)
                {
                    state.TryCompleteResynchronizationFailure(token, reason);
                    return OrderBookResyncResult.Failed(
                        assetId,
                        reason,
                        attempt,
                        snapshotResult.Error);
                }

                try
                {
                    if (state.TryReplaceFromSnapshot(snapshotResult.Value, token))
                    {
                        return OrderBookResyncResult.Synchronized(
                            assetId,
                            reason,
                            attempt,
                            snapshotResult.Value);
                    }
                }
                catch (ArgumentException exception)
                {
                    state.TryCompleteResynchronizationFailure(token, reason);
                    return OrderBookResyncResult.Failed(
                        assetId,
                        reason,
                        attempt,
                        OrderBookResynchronizationErrors.InvalidSnapshot(exception.Message));
                }

                if (attempt < MaximumAttempts
                    && !state.TryRestartResynchronization(token, out token))
                {
                    return OrderBookResyncResult.Failed(
                        assetId,
                        reason,
                        attempt,
                        OrderBookResynchronizationErrors.StateChanged(assetId));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                state.TryCompleteResynchronizationFailure(token, reason);
                throw;
            }
            catch
            {
                state.TryCompleteResynchronizationFailure(token, reason);
                throw;
            }
        }

        state.TryCompleteResynchronizationFailure(token, reason);
        return OrderBookResyncResult.Failed(
            assetId,
            reason,
            MaximumAttempts,
            OrderBookResynchronizationErrors.StateChanged(assetId));
    }
}
