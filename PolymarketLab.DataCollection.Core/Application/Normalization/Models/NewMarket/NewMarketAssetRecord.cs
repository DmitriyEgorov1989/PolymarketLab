namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Упорядоченное соответствие актива и исхода нового рынка.</summary>
public sealed record NewMarketAssetRecord : NormalizedRecord
{
    /// <summary>Создаёт упорядоченное соответствие актива и исхода.</summary>
    /// <param name="itemIndex">Общий индекс в массивах <c>assets_ids</c> и <c>outcomes</c>.</param>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="outcome">Соответствующий активу исход.</param>
    public NewMarketAssetRecord(int itemIndex, string assetId, string outcome)
    {
        if (itemIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(itemIndex), "Item index cannot be negative.");
        if (string.IsNullOrWhiteSpace(assetId))
            throw new ArgumentException("Asset id is required.", nameof(assetId));
        if (string.IsNullOrWhiteSpace(outcome))
            throw new ArgumentException("Outcome is required.", nameof(outcome));

        ItemIndex = itemIndex;
        AssetId = assetId;
        Outcome = outcome;
    }

    /// <summary>Общий индекс в исходных массивах.</summary>
    public int ItemIndex { get; }

    /// <summary>Идентификатор актива.</summary>
    public string AssetId { get; }

    /// <summary>Соответствующий активу исход.</summary>
    public string Outcome { get; }
}
