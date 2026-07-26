using CSharpFunctionalExtensions;
using MediatR;
using static PolymarketLab.SharedKernel.Errors.Error;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;

public sealed record StartCollectorCommand(Guid MarketId)
    : IRequest<Result<StartCollectorResponse, ErrorList>>;
