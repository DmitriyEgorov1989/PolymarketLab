using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using System.Threading.Channels;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;

internal sealed class RawMarketMessageChannel : IRawMarketMessageSink
{
    private readonly Channel<RawMarketMessage> _channel;
    private int _queuedCount;

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
    internal int QueuedCount => Volatile.Read(ref _queuedCount);

    public ValueTask EnqueueAsync(
        RawMarketMessage message,
        CancellationToken cancellationToken)
    {
        var ownedMessage = message with
        {
            Payload = message.Payload.ToArray()
        };

        return EnqueueOwnedAsync(ownedMessage, cancellationToken);
    }

    internal bool TryRead(out RawMarketMessage message)
    {
        if (!_channel.Reader.TryRead(out message!))
            return false;

        Interlocked.Decrement(ref _queuedCount);
        return true;
    }

    internal bool TryComplete(Exception? error = null)
    {
        return _channel.Writer.TryComplete(error);
    }

    private async ValueTask EnqueueOwnedAsync(
        RawMarketMessage message,
        CancellationToken cancellationToken)
    {
        while (await WaitToWriteAsync(cancellationToken))
        {
            Interlocked.Increment(ref _queuedCount);
            if (_channel.Writer.TryWrite(message))
                return;

            Interlocked.Decrement(ref _queuedCount);
        }

        throw new ChannelClosedException();
    }

    private async ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _channel.Writer.WaitToWriteAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ChannelClosedException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ChannelClosedException(
                "Raw market message channel is closed.",
                exception);
        }
    }
}
