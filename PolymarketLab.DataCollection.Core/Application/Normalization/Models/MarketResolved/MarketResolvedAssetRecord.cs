namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Актив разрешённого рынка в исходном порядке.</summary>
public sealed record MarketResolvedAssetRecord : NormalizedRecord
{
    /// <summary>Создаёт запись актива разрешённого рынка.</summary>
    /// <param name="itemIndex">Позиция актива в исходном массиве.</param>
    /// <param name="assetId">Идентификатор актива.</param>
    public MarketResolvedAssetRecord(int itemIndex, string assetId)
    {
        if (itemIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(itemIndex), "Item index cannot be negative.");
        if (string.IsNullOrWhiteSpace(assetId))
            throw new ArgumentException("Asset id is required.", nameof(assetId));

        ItemIndex = itemIndex;
        AssetId = assetId;
    }

    /// <summary>Позиция актива в исходном массиве.</summary>
    public int ItemIndex { get; }

    /// <summary>Идентификатор актива.</summary>
    public string AssetId { get; }
}
