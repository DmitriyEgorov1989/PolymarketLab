using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Core.Application.Normalization;

public interface IRawMessageNormalizer
{
    string EventType { get; }
    int Version { get; }
    NormalizationResult Normalize(LogicalRawEvent rawEvent);
}
