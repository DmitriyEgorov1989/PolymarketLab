using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.DataCollection.Core.Application.UseCases.Common;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionByMarket;

public sealed class GetCollectorSessionByMarketHandler(
    ICollectorSessionRepository sessionRepository,
    ICollectorSessionResponseFactory responseFactory)
    : IRequestHandler<GetCollectorSessionByMarketQuery, Result<GetCollectorSessionByMarketResponse, ErrorList>>
{
    public async Task<Result<GetCollectorSessionByMarketResponse, ErrorList>> Handle(
        GetCollectorSessionByMarketQuery request,
        CancellationToken cancellationToken)
    {
        var marketIdResult = MarketId.Create(request.MarketId);
        if (marketIdResult.IsFailure)
            return Failure(marketIdResult.Error);

        var session = await sessionRepository.GetCurrentByMarketIdAsync(
            marketIdResult.Value,
            cancellationToken);

        if (session is null)
            return new GetCollectorSessionByMarketResponse(null);

        var response = await responseFactory.CreateAsync(session, cancellationToken);
        return new GetCollectorSessionByMarketResponse(response);
    }

    private static Result<GetCollectorSessionByMarketResponse, ErrorList> Failure(params Error[] errors)
    {
        return Result.Failure<GetCollectorSessionByMarketResponse, ErrorList>(errors.ToList());
    }
}
