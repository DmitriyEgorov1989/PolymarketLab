using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using static PolymarketLab.SharedKernel.Errors.Error;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.ReplayNormalization;

public sealed class ReplayNormalizationHandler(INormalizationReplayService replayService)
    : IRequestHandler<ReplayNormalizationCommand, Result<ReplayNormalizationResponse, ErrorList>>
{
    public async Task<Result<ReplayNormalizationResponse, ErrorList>> Handle(
        ReplayNormalizationCommand command,
        CancellationToken cancellationToken)
    {
        CollectorSessionId? sessionId = null;
        if (command.SessionId.HasValue)
        {
            var sessionIdResult = CollectorSessionId.Create(command.SessionId.Value);
            if (sessionIdResult.IsFailure)
            {
                return Result.Failure<ReplayNormalizationResponse, ErrorList>(
                    sessionIdResult.Error);
            }

            sessionId = sessionIdResult.Value;
        }

        var filter = new NormalizationReplayFilter(
            command.SourceProjectionVersion,
            command.TargetProjectionVersion,
            sessionId,
            command.EventType);
        var replay = await replayService.ReplayAsync(filter, cancellationToken);
        if (replay.IsFailure)
            return Result.Failure<ReplayNormalizationResponse, ErrorList>(replay.Error);

        var result = replay.Value;
        return new ReplayNormalizationResponse(
            filter.SourceProjectionVersion,
            filter.TargetProjectionVersion,
            command.SessionId,
            filter.EventType,
            result.BatchCount,
            result.Total,
            result.Processed,
            result.Invalid,
            result.Unsupported,
            result.Failed,
            result.FirstRawMessageId,
            result.LastRawMessageId);
    }
}
