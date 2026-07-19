using CSharpFunctionalExtensions;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Application.Contracts
{
    public interface IPolymarketUrlParser
    {
        Result<MarketSlug, Error> Parse(string url);
    }
}
