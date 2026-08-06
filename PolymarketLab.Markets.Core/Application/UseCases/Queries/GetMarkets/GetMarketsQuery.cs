using CSharpFunctionalExtensions;
using MediatR;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarkets;

public sealed record GetMarketsQuery()
    : IRequest<Result<GetMarketsResponse, ErrorList>>;
