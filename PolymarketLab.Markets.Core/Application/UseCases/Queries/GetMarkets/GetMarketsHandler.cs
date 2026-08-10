using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.Markets.Core.Application.Integration;
using PolymarketLab.Markets.Core.Application.UseCases.Common;
using PolymarketLab.Markets.Core.Ports;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarkets;

public sealed class GetMarketsHandler(
    IMarketRepository marketRepository,
    TimeProvider timeProvider)
    : IRequestHandler<GetMarketsQuery, Result<GetMarketsResponse, ErrorList>>
{
    public async Task<Result<GetMarketsResponse, ErrorList>> Handle(
        GetMarketsQuery request,
        CancellationToken cancellationToken)
    {
        var markets = await marketRepository.GetAllAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();

        return new GetMarketsResponse(
            markets
                .Where(market => MarketAvailability.IsWithinCollectionWindow(
                    market.StartsAt,
                    market.EndsAt,
                    now))
                .Select(MarketResponse.FromMarket)
                .ToArray());
    }
}
