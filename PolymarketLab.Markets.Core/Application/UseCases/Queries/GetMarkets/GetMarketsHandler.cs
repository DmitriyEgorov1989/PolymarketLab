using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.Markets.Core.Application.Integration;
using PolymarketLab.Markets.Core.Application.UseCases.Common;
using PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate;
using PolymarketLab.Markets.Core.Ports;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarkets;

public sealed class GetMarketsHandler(
    IMarketRepository marketRepository,
    IExternalMarketGateway externalMarketGateway,
    TimeProvider timeProvider)
    : IRequestHandler<GetMarketsQuery, Result<GetMarketsResponse, ErrorList>>
{
    public async Task<Result<GetMarketsResponse, ErrorList>> Handle(
        GetMarketsQuery request,
        CancellationToken cancellationToken)
    {
        var markets = (await marketRepository.GetAllAsync(cancellationToken))
            .Where(market => market.EventEndsAt > timeProvider.GetUtcNow())
            .ToArray();

        if (request.TradingNow)
        {
            var tradingMarkets = new List<Market>();

            foreach (var market in markets)
            {
                var externalMarketResult = await externalMarketGateway.GetByMarketSlugAsync(
                    market.MarketSlug,
                    cancellationToken);
                if (externalMarketResult.IsFailure)
                {
                    return Result.Failure<GetMarketsResponse, ErrorList>(
                        externalMarketResult.Error);
                }

                if (MarketAvailability.IsAvailable(externalMarketResult.Value))
                    tradingMarkets.Add(market);
            }

            markets = tradingMarkets.ToArray();
        }

        return new GetMarketsResponse(
            markets
                .Select(MarketResponse.FromMarket)
                .ToArray());
    }
}
