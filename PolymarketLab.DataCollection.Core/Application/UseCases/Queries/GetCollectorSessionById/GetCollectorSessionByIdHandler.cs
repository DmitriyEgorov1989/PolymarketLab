using CSharpFunctionalExtensions;
using FluentValidation;
using MediatR;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.UseCases.Common;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;
using PolymarketLab.SharedKernel.Extensions.Validations;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionById;

public sealed class GetCollectorSessionByIdHandler(
    IValidator<GetCollectorSessionByIdQuery> validator,
    ICollectorSessionRepository sessionRepository,
    ICollectorSessionProgressRepository progressRepository)
    : IRequestHandler<GetCollectorSessionByIdQuery, Result<GetCollectorSessionByIdResponse, ErrorList>>
{
    public async Task<Result<GetCollectorSessionByIdResponse, ErrorList>> Handle(
        GetCollectorSessionByIdQuery request,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToValidationErrorResponse(request);

        var sessionIdResult = CollectorSessionId.Create(request.SessionId);
        if (sessionIdResult.IsFailure)
            return Failure(sessionIdResult.Error);

        var session = await sessionRepository.GetByIdAsync(sessionIdResult.Value, cancellationToken);
        if (session is null)
            return Failure(CollectorSessionQueryErrors.NotFound(request.SessionId));

        var progress = await progressRepository.GetAsync(session.Id, cancellationToken);
        return new GetCollectorSessionByIdResponse(
            CollectorSessionResponse.FromSession(session, progress));
    }

    private static Result<GetCollectorSessionByIdResponse, ErrorList> Failure(params Error[] errors)
    {
        return Result.Failure<GetCollectorSessionByIdResponse, ErrorList>(errors.ToList());
    }
}
