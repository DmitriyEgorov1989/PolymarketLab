using PolymarketLab.Markets.Core.Application.UseCases.Commands;
namespace PolymarketLab.Markets.Presentation.Controllers.Models
{
    public record RegisterMarketRequest(string MarketUri)
    {
        public RegisterMarketCommand ToCommand() =>
            new(MarketUri);
    }
}
