using CSharpFunctionalExtensions;
using MediatR;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionById;

public sealed record GetCollectorSessionByIdQuery(Guid SessionId)
    : IRequest<Result<GetCollectorSessionByIdResponse, ErrorList>>;
