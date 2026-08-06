using CSharpFunctionalExtensions;
using MediatR;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarketById;

public sealed record GetMarketByIdQuery(Guid MarketId)
    : IRequest<Result<GetMarketByIdResponse, ErrorList>>;
