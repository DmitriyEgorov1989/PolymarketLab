using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.StartCollector;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.Markets.Contracts;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using static PolymarketLab.SharedKernel.Errors.Error;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.MarketIntegration;

/// <summary>Связывает регистрацию рынка с существующим запуском collector.</summary>
internal sealed class RegisteredMarketCollectorScheduler(
    ICollectorSessionRepository sessionRepository,
    IMediator mediator)
    : IRegisteredMarketCollectorScheduler
{
    public async Task<UnitResult<ErrorList>> EnsureScheduledAsync(
        MarketId marketId,
        CancellationToken cancellationToken)
    {
        var activeSession = await sessionRepository.GetActiveByMarketIdAsync(
            marketId,
            cancellationToken);
        if (activeSession is not null)
            return UnitResult.Success<ErrorList>();

        var successfulSession = await sessionRepository.GetSuccessfulByMarketIdAsync(
            marketId,
            cancellationToken);
        if (successfulSession is not null)
            return UnitResult.Success<ErrorList>();

        var startResult = await mediator.Send(
            new StartCollectorCommand(marketId.Value),
            cancellationToken);
        return startResult.IsSuccess
            ? UnitResult.Success<ErrorList>()
            : UnitResult.Failure(startResult.Error);
    }
}
