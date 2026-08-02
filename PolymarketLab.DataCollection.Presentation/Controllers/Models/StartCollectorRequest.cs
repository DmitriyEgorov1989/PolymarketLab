using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;

namespace PolymarketLab.DataCollection.Presentation.Controllers.Models
{

    public sealed record StartCollectorRequest(Guid SessionId)
    {
        public StartCollectorCommand ToCommand() => new(SessionId);
    }
}
