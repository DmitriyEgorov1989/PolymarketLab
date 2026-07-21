using MediatR;
using Microsoft.AspNetCore.Mvc;
using PolymarketLab.Framework;
using PolymarketLab.Framework.Response;
using PolymarketLab.Markets.Core.Application.UseCases.Commands;
using PolymarketLab.Markets.Presentation.Controllers.Models;

namespace PolymarketLab.Markets.Presentation.Controllers
{
    public class MarketController(IMediator mediator) : ApplicationController
    {
        [HttpPost("register")]
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