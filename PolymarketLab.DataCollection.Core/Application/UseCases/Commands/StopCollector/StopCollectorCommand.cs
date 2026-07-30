using CSharpFunctionalExtensions;
using MediatR;
using static PolymarketLab.SharedKernel.Errors.Error;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StopCollector;

public sealed record StopCollectorCommand(Guid SessionId)
    : IRequest<Result<StopCollectorResponse, ErrorList>>;
