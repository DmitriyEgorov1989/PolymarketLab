using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization;

internal static class OrderBookResynchronizationErrors
{
    public static Error StateNotFound(string assetId) => new(
        "OrderBook.Resynchronization.StateNotFound",
        $"Order book state for asset '{assetId}' was not found.",
        ErrorType.NotFound);

    public static Error InvalidState(string assetId) => new(
        "OrderBook.Resynchronization.InvalidState",
        $"Order book state for asset '{assetId}' is not eligible for resynchronization.",
        ErrorType.Conflict);

    public static Error InvalidSnapshot(string message) => new(
        "OrderBook.Resynchronization.InvalidSnapshot",
        message,
        ErrorType.ValueIsInvalid);

    public static Error StateChanged(string assetId) => new(
        "OrderBook.Resynchronization.StateChanged",
        $"Order book state for asset '{assetId}' kept changing during resynchronization.",
        ErrorType.Conflict);
}
