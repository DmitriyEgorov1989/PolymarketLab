using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Core.Application.Normalization;

public interface INormalizationDispatcher
{
    NormalizationResult Dispatch(LogicalRawEvent rawEvent);
}
