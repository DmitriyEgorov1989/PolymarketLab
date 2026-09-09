using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.DataCollection.Core.Application.UseCases.Common;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.Errors;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessions;

public sealed class GetCollectorSessionsHandler(
    ICollectorSessionRepository sessionRepository,
    ICollectorSessionResponseFactory responseFactory)
    : IRequestHandler<GetCollectorSessionsQuery, Result<GetCollectorSessionsResponse, ErrorList>>
{
    public async Task<Result<GetCollectorSessionsResponse, ErrorList>> Handle(
        GetCollectorSessionsQuery request,
        CancellationToken cancellationToken)
    {
        var sessions = await sessionRepository.GetCurrentAsync(cancellationToken);
        var responses = new List<CollectorSessionResponse>(sessions.Count);

        foreach (var session in sessions)
            responses.Add(await responseFactory.CreateAsync(session, cancellationToken));

        return new GetCollectorSessionsResponse(responses);
    }
}
