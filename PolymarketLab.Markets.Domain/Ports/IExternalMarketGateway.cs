using CSharpFunctionalExtensions;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Core.Ports.Dto;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Ports
{
    public interface IExternalMarketGateway
    {
        Task<Result<ExternalMarket, Error>> GetBySlugAsync(
            MarketSlug slug,
            CancellationToken cancellationToken);
    }
}
