using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StopCollector;

namespace PolymarketLab.DataCollection.Presentation.Controllers.Models;

public sealed record StopCollectorRequest(Guid SessionId)
{
    public StopCollectorCommand ToCommand() => new(SessionId);
}
