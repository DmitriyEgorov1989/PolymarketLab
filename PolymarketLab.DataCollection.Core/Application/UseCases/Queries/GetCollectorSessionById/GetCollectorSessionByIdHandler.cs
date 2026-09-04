using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.Common;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionById;

public sealed class GetCollectorSessionByIdHandler(
    ICollectorSessionRepository sessionRepository,
    ICollectorSessionResponseFactory responseFactory)
    : IRequestHandler<GetCollectorSessionByIdQuery, Result<GetCollectorSessionByIdResponse, ErrorList>>
{
    public async Task<Result<GetCollectorSessionByIdResponse, ErrorList>> Handle(
        GetCollectorSessionByIdQuery request,
        CancellationToken cancellationToken)
    {
        var sessionIdResult = CollectorSessionId.Create(request.SessionId);
        if (sessionIdResult.IsFailure)
            return Failure(sessionIdResult.Error);

        var session = await sessionRepository.GetByIdAsync(sessionIdResult.Value, cancellationToken);
        if (session is null)
            return Failure(CollectorSessionQueryErrors.NotFound(request.SessionId));

        var response = await responseFactory.CreateAsync(session, cancellationToken);
        return new GetCollectorSessionByIdResponse(response);
    }

    private static Result<GetCollectorSessionByIdResponse, ErrorList> Failure(params Error[] errors)
    {
        return Result.Failure<GetCollectorSessionByIdResponse, ErrorList>(errors.ToList());
    }
}
