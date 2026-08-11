namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Результат разрешения рынка.</summary>
public sealed record MarketResolvedRecord : NormalizedRecord
{
    /// <summary>Создаёт запись результата разрешения рынка.</summary>
    /// <param name="externalMarketId">Внешний идентификатор рынка.</param>
    /// <param name="winningAssetId">Идентификатор победившего актива.</param>
    /// <param name="winningOutcome">Название победившего исхода.</param>
    public MarketResolvedRecord(
        string externalMarketId,
        string winningAssetId,
        string winningOutcome)
    {
        if (string.IsNullOrWhiteSpace(externalMarketId))
            throw new ArgumentException("External market id is required.", nameof(externalMarketId));
        if (string.IsNullOrWhiteSpace(winningAssetId))
            throw new ArgumentException("Winning asset id is required.", nameof(winningAssetId));
        if (string.IsNullOrWhiteSpace(winningOutcome))
            throw new ArgumentException("Winning outcome is required.", nameof(winningOutcome));

        ExternalMarketId = externalMarketId;
        WinningAssetId = winningAssetId;
        WinningOutcome = winningOutcome;
    }

    /// <summary>Внешний идентификатор рынка.</summary>
    public string ExternalMarketId { get; }

    /// <summary>Идентификатор победившего актива.</summary>
    public string WinningAssetId { get; }

    /// <summary>Название победившего исхода.</summary>
    public string WinningOutcome { get; }
}
