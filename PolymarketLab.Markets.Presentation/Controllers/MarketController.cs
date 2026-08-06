using MediatR;
using Microsoft.AspNetCore.Mvc;
using PolymarketLab.Framework;
using PolymarketLab.Framework.Response;
using PolymarketLab.Markets.Core.Application.UseCases.Commands;
using PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarketById;
using PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarkets;
using PolymarketLab.Markets.Presentation.Controllers.Models;

namespace PolymarketLab.Markets.Presentation.Controllers
{
    public class MarketController(IMediator mediator) : ApplicationController
    {
        [HttpGet]
        public async Task<ActionResult<GetMarketsResponse>> GetMarkets(
            CancellationToken cancellationToken)
        {
            var response = await mediator.Send(new GetMarketsQuery(), cancellationToken);

            return response.ToResponseErrorOrResult();
        }

        [HttpGet("{marketId:guid}")]
        public async Task<ActionResult<GetMarketByIdResponse>> GetMarketById(
            Guid marketId,
            CancellationToken cancellationToken)
        {
            var response = await mediator.Send(new GetMarketByIdQuery(marketId), cancellationToken);

            return response.ToResponseErrorOrResult();
        }

        [HttpPost]
        public async Task<ActionResult<RegisterMarketResponse>> RegisterMarket(
            [FromBody] RegisterMarketRequest registerMarketRequest,
            CancellationToken cancellationToken)
        {
            var response = await mediator.Send(
                registerMarketRequest.ToCommand(), cancellationToken);

            return response.ToResponseErrorOrResult();
        }
    }
}
