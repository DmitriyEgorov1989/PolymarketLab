using MediatR;
using Microsoft.AspNetCore.Mvc;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StopCollector;
using PolymarketLab.DataCollection.Presentation.Controllers.Models;
using PolymarketLab.Framework;
using PolymarketLab.Framework.Response;

namespace PolymarketLab.DataCollection.Presentation.Controllers;

public sealed class CollectorController(IMediator mediator) : ApplicationController
{
    [HttpPost("stop")]
    public async Task<ActionResult<StopCollectorResponse>> StopCollector(
        [FromBody] StopCollectorRequest request,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(
            request.ToCommand(),
            cancellationToken);

        return response.ToResponseErrorOrResult();
    }
}
