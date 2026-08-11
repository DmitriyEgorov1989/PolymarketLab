using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Core.Ports;

public interface IRawMessageDecoder
{
    RawMessageDecodeResult Decode(RawMessageEnvelope message);
}
