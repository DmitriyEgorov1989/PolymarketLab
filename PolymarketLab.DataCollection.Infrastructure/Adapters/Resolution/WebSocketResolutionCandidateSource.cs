using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Application.Resolution;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Resolution;

internal sealed class WebSocketResolutionCandidateSource(DataCollectionDbContext dbContext)
    : IWebSocketResolutionCandidateSource
{
    private const int BatchSize = 500;

    public async Task<WebSocketResolutionScanResult> ScanAsync(
        CollectorSessionId sessionId,
        long afterRawMessageId,
        CancellationToken cancellationToken)
    {
        var targetRawMessageId = await dbContext.RawMarketMessages
            .AsNoTracking()
            .Where(message => message.SessionId == sessionId
                && message.Id > afterRawMessageId)
            .Select(message => (long?)message.Id)
            .MaxAsync(cancellationToken)
            ?? afterRawMessageId;

        if (targetRawMessageId == afterRawMessageId)
            return new WebSocketResolutionScanResult(afterRawMessageId, []);

        var cursor = afterRawMessageId;
        var candidates = new List<WebSocketResolutionCandidate>();
        while (cursor < targetRawMessageId)
        {
            var messages = await dbContext.RawMarketMessages
                .AsNoTracking()
                .Where(message => message.SessionId == sessionId
                    && message.Id > cursor
                    && message.Id <= targetRawMessageId)
                .OrderBy(message => message.Id)
                .Take(BatchSize)
                .Select(message => new ScannedRawMessage(
                    message.Id,
                    message.ConnectionEpoch,
                    message.ReceivedAt,
                    message.Payload))
                .ToListAsync(cancellationToken);
            if (messages.Count == 0)
                break;

            foreach (var message in messages)
            {
                candidates.AddRange(WebSocketResolutionCandidateParser.Parse(
                    message.Id,
                    message.ConnectionEpoch,
                    message.ReceivedAt,
                    message.Payload));
            }

            cursor = messages[^1].Id;
        }

        return new WebSocketResolutionScanResult(cursor, candidates);
    }

    private sealed record ScannedRawMessage(
        long Id,
        long ConnectionEpoch,
        DateTimeOffset ReceivedAt,
        byte[] Payload);
}
