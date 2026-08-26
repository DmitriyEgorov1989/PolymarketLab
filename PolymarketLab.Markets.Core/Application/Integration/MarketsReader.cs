using CSharpFunctionalExtensions;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.Markets.Core.Application.Errors;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Application.Integration;

internal sealed class MarketsReader(
    IMarketRepository repository,
    IExternalMarketGateway externalMarketGateway) : IMarketsReader
{
    public async Task<Result<MarketForCollection?, Error>> GetForCollectionAsync(
        MarketId marketId,
        CancellationToken cancellationToken)
    {
        var market = await repository.GetByIdAsync(marketId, cancellationToken);

        if (market is null)
            return (MarketForCollection?)null;

        var externalMarketResult = await externalMarketGateway.GetByMarketSlugAsync(
            market.Slug,
            cancellationToken);
        if (externalMarketResult.IsFailure)
            return externalMarketResult.Error;

        if (!MarketAvailability.IsAvailable(externalMarketResult.Value))
        {
            return MarketCollectionErrors.Unavailable(market.Id.Value);
        }

        return new MarketForCollection(
            market.Id,
            market.Slug.Value,
            market.Tokens
                .Select(token => new MarketTokenForCollection(
                    token.ExternalTokenId,
                    token.Outcome,
                    token.OutcomeIndex))
                .ToArray());
    }
}
