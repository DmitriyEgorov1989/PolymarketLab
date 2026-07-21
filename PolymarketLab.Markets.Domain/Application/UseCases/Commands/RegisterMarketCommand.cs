using CSharpFunctionalExtensions;
using MediatR;
using static PolymarketLab.SharedKernel.Errors.Error;

namespace PolymarketLab.Markets.Core.Application.UseCases.Commands
{
    public record RegisterMarketCommand(string MarketUri) :
        IRequest<Result<RegisterMarketResponse, ErrorList>>;
}
