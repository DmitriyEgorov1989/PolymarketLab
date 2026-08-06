using CSharpFunctionalExtensions;
using MediatR;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionByMarket;

public sealed record GetCollectorSessionByMarketQuery(Guid MarketId)
    : IRequest<Result<GetCollectorSessionByMarketResponse, ErrorList>>;
