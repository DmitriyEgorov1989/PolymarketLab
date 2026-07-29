using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using System.Threading.Channels;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;

internal sealed class RawMarketMessageChannel : IRawMarketMessageSink
{
    private readonly Channel<RawMarketMessage> _channel;

    public RawMarketMessageChannel(IOptions<RawMessageIngestionOptions> options)
    {
        _channel = Channel.CreateBounded<RawMarketMessage>(
            new BoundedChannelOptions(options.Value.Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
    }

    internal ChannelReader<RawMarketMessage> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(
        RawMarketMessage message,
        CancellationToken cancellationToken)
    {
        var ownedMessage = message with
        {
            Payload = message.Payload.ToArray()
        };

        return _channel.Writer.WriteAsync(ownedMessage, cancellationToken);
    }

    internal bool TryComplete(Exception? error = null)
    {
        return _channel.Writer.TryComplete(error);
    }
}
