using MediatR;
using Microsoft.AspNetCore.Mvc;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StopCollector;
using PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionById;
using PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionByMarket;
using PolymarketLab.DataCollection.Presentation.Controllers.Models;
using PolymarketLab.Framework;
using PolymarketLab.Framework.Response;

namespace PolymarketLab.DataCollection.Presentation.Controllers;

public sealed class CollectorController(IMediator mediator) : ApplicationController
{
    [HttpGet("{sessionId:guid}")]
    public async Task<ActionResult<GetCollectorSessionByIdResponse>> GetCollectorSessionById(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(
            new GetCollectorSessionByIdQuery(sessionId),
            cancellationToken);

        return response.ToResponseErrorOrResult();
    }

    [HttpGet("by-market/{marketId:guid}")]
    public async Task<ActionResult<GetCollectorSessionByMarketResponse>> GetCollectorSessionByMarket(
        Guid marketId,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(
            new GetCollectorSessionByMarketQuery(marketId),
            cancellationToken);

        return response.ToResponseErrorOrResult();
    }

    [HttpPost]
    public async Task<ActionResult<StartCollectorResponse>> StartCollector(
        [FromBody] StartCollectorRequest request,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(
            request.ToCommand(),
            cancellationToken);

        return response.ToResponseErrorOrResult();
    }

    [HttpPost("{sessionId:guid}/stop")]
    public async Task<ActionResult<StopCollectorResponse>> StopCollector(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(
            new StopCollectorCommand(sessionId),
            cancellationToken);

        return response.ToResponseErrorOrResult();
    }
}
