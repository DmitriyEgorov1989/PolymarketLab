using CSharpFunctionalExtensions;
using FluentAssertions;
using MediatR;
using PolymarketLab.Markets.Core.Application.UseCases.Commands;
using PolymarketLab.SharedKernel.Mediation;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;
using Xunit;

namespace PolymarketLab.Markets.Domain.Tests.Application.Mediation;

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_WithInvalidRequest_ShouldReturnErrorsWithoutCallingHandler()
    {
        var behavior = new ValidationBehavior<RegisterMarketCommand, RegisterMarketResponse>(
            [new RegisterCommandValidation()]);
        var handlerCalled = false;
        RequestHandlerDelegate<Result<RegisterMarketResponse, ErrorList>> next = _ =>
        {
            handlerCalled = true;
            return Task.FromResult(Success());
        };

        var result = await behavior.Handle(
            new RegisterMarketCommand(string.Empty),
            next,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().ContainSingle();
        result.Error.Single().Code.Should().Be("value.is.required");
        handlerCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithValidRequest_ShouldCallHandlerOnce()
    {
        var behavior = new ValidationBehavior<RegisterMarketCommand, RegisterMarketResponse>(
            [new RegisterCommandValidation()]);
        var handlerCallCount = 0;
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        var receivedCancellationToken = CancellationToken.None;
        RequestHandlerDelegate<Result<RegisterMarketResponse, ErrorList>> next = token =>
        {
            handlerCallCount++;
            receivedCancellationToken = token;
            return Task.FromResult(Success());
        };

        var result = await behavior.Handle(
            new RegisterMarketCommand("https://polymarket.com/event/example"),
            next,
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        handlerCallCount.Should().Be(1);
        receivedCancellationToken.Should().Be(cancellationToken);
    }

    private static Result<RegisterMarketResponse, ErrorList> Success()
    {
        return new RegisterMarketResponse(Guid.NewGuid(), true);
    }
}
