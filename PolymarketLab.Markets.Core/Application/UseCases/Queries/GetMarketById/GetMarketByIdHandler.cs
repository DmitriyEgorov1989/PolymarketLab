using CSharpFunctionalExtensions;
using FluentValidation;
using MediatR;
using PolymarketLab.Markets.Core.Application.Errors;
using PolymarketLab.Markets.Core.Application.UseCases.Common;
using PolymarketLab.Markets.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using PolymarketLab.SharedKernel.Extensions.Validations;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarketById;

public sealed class GetMarketByIdHandler(
    IValidator<GetMarketByIdQuery> validator,
    IMarketRepository marketRepository)
    : IRequestHandler<GetMarketByIdQuery, Result<GetMarketByIdResponse, ErrorList>>
{
    public async Task<Result<GetMarketByIdResponse, ErrorList>> Handle(
        GetMarketByIdQuery request,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToValidationErrorResponse(request);

        var marketIdResult = MarketId.Create(request.MarketId);
        if (marketIdResult.IsFailure)
            return Failure(marketIdResult.Error);

        var market = await marketRepository.GetByIdAsync(marketIdResult.Value, cancellationToken);
        if (market is null)
            return Failure(MarketQueryErrors.NotFound(request.MarketId));

        return new GetMarketByIdResponse(MarketResponse.FromMarket(market));
    }

    private static Result<GetMarketByIdResponse, ErrorList> Failure(params Error[] errors)
    {
        return Result.Failure<GetMarketByIdResponse, ErrorList>(errors.ToList());
    }
}
