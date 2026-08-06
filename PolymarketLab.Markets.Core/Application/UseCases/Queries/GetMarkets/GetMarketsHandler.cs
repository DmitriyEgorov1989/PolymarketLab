using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.Markets.Core.Application.UseCases.Common;
using PolymarketLab.Markets.Core.Ports;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarkets;

public sealed class GetMarketsHandler(IMarketRepository marketRepository)
    : IRequestHandler<GetMarketsQuery, Result<GetMarketsResponse, ErrorList>>
{
    public async Task<Result<GetMarketsResponse, ErrorList>> Handle(
        GetMarketsQuery request,
        CancellationToken cancellationToken)
    {
        var markets = await marketRepository.GetAllAsync(cancellationToken);

        return new GetMarketsResponse(
            markets
                .Select(MarketResponse.FromMarket)
                .ToArray());
    }
}
