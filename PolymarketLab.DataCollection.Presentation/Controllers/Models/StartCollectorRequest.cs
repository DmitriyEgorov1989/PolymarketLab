using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;

namespace PolymarketLab.DataCollection.Presentation.Controllers.Models
{

    public sealed record StartCollectorRequest(Guid MarketId)
    {
        public StartCollectorCommand ToCommand() => new(MarketId);
    }
}
