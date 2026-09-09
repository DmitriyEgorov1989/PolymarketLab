using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.DataCollection.Core.Application.UseCases.Common;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessions;

public sealed record GetCollectorSessionsQuery
    : IRequest<Result<GetCollectorSessionsResponse, Error.ErrorList>>;
