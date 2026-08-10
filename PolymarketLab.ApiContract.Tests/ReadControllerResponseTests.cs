using CSharpFunctionalExtensions;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using PolymarketLab.DataCollection.Core.Application.UseCases.Common;
using PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionById;
using PolymarketLab.DataCollection.Core.Application.UseCases.Queries.GetCollectorSessionByMarket;
using PolymarketLab.DataCollection.Presentation.Controllers;
using PolymarketLab.Framework.Response;
using PolymarketLab.Markets.Core.Application.UseCases.Common;
using PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarketById;
using PolymarketLab.Markets.Core.Application.UseCases.Queries.GetMarkets;
using PolymarketLab.Markets.Presentation.Controllers;
using PolymarketLab.SharedKernel.Errors;
using Xunit;
using ErrorList = PolymarketLab.SharedKernel.Errors.Error.ErrorList;

namespace PolymarketLab.ApiContract.Tests;

public sealed class ReadControllerResponseTests
{
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-08-06T12:00:00Z");

    [Fact]
    public async Task GetMarkets_WithEmptyList_ShouldReturn200Envelope()
    {
        var mediator = Substitute.For<IMediator>();
        var response = new GetMarketsResponse([]);
        mediator.Send(Arg.Any<GetMarketsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<GetMarketsResponse, ErrorList>(response));
        var controller = new MarketController(mediator);

        var action = await controller.GetMarkets(true, CancellationToken.None);

        var envelope = AssertOkEnvelope(action);
        envelope.Result.Should().BeSameAs(response);
        envelope.ListErrors.Should().BeEmpty();
        ((GetMarketsResponse)envelope.Result!).Markets.Should().BeEmpty();
        await mediator.Received(1).Send(
            Arg.Is<GetMarketsQuery>(query => query.TradingNow),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMarketById_WithMarketAndTokens_ShouldReturn200Envelope()
    {
        var marketId = Guid.NewGuid();
        var response = new GetMarketByIdResponse(new MarketResponse(
            marketId,
            "market-123",
            "will-it-rain",
            "0xcondition",
            "Will it rain?",
            CreatedAt,
            CreatedAt.AddHours(1),
            [
                new MarketTokenResponse("token-yes", "Yes", 0),
                new MarketTokenResponse("token-no", "No", 1)
            ]));
        var mediator = Substitute.For<IMediator>();
        mediator.Send(
                Arg.Is<GetMarketByIdQuery>(query => query.MarketId == marketId),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<GetMarketByIdResponse, ErrorList>(response));
        var controller = new MarketController(mediator);

        var action = await controller.GetMarketById(marketId, CancellationToken.None);

        var envelope = AssertOkEnvelope(action);
        envelope.Result.Should().BeSameAs(response);
        var market = ((GetMarketByIdResponse)envelope.Result!).Market;
        market.Should().BeEquivalentTo(response.Market);
        market.Tokens.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetMarketById_WithUnknownId_ShouldReturn404ErrorEnvelope()
    {
        var marketId = Guid.NewGuid();
        var error = new Error(
            "market.query.not_found",
            $"Market '{marketId}' was not found.",
            ErrorType.NotFound,
            "marketId");
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetMarketByIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<GetMarketByIdResponse, ErrorList>(new ErrorList([error])));
        var controller = new MarketController(mediator);

        var action = await controller.GetMarketById(marketId, CancellationToken.None);

        AssertErrorEnvelope(
            action,
            StatusCodes.Status404NotFound,
            "market.query.not_found",
            "marketId");
    }

    [Fact]
    public async Task GetMarketById_WithInvalidId_ShouldReturn400ErrorEnvelope()
    {
        var error = new Error(
            "market.query.market_id.required",
            "Market id is required.",
            ErrorType.ValueIsRequired,
            "marketId");
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetMarketByIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<GetMarketByIdResponse, ErrorList>(new ErrorList([error])));
        var controller = new MarketController(mediator);

        var action = await controller.GetMarketById(Guid.Empty, CancellationToken.None);

        AssertErrorEnvelope(
            action,
            StatusCodes.Status400BadRequest,
            "market.query.market_id.required",
            "marketId");
    }

    [Fact]
    public async Task GetCollectorSessionById_WithFailedSession_ShouldReturn200EnvelopeWithFailureAndCounters()
    {
        var sessionId = Guid.NewGuid();
        var session = CreateCollectorSession(sessionId, "Failed");
        var response = new GetCollectorSessionByIdResponse(session);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(
                Arg.Is<GetCollectorSessionByIdQuery>(query => query.SessionId == sessionId),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<GetCollectorSessionByIdResponse, ErrorList>(response));
        var controller = new CollectorController(mediator);

        var action = await controller.GetCollectorSessionById(sessionId, CancellationToken.None);

        var envelope = AssertOkEnvelope(action);
        var actual = ((GetCollectorSessionByIdResponse)envelope.Result!).Session;
        actual.Should().BeEquivalentTo(session);
        actual.FailureCode.Should().Be("collector.runtime.receive.failed");
        actual.MessagesReceived.Should().Be(120);
        actual.MessagesPersisted.Should().Be(118);
        actual.ReconnectCount.Should().Be(2);
    }

    [Fact]
    public async Task GetCollectorSessionById_WithUnknownId_ShouldReturn404ErrorEnvelope()
    {
        var error = new Error(
            "collector.query.session.not_found",
            "Collector session was not found.",
            ErrorType.NotFound,
            "sessionId");
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetCollectorSessionByIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<GetCollectorSessionByIdResponse, ErrorList>(new ErrorList([error])));
        var controller = new CollectorController(mediator);

        var action = await controller.GetCollectorSessionById(Guid.NewGuid(), CancellationToken.None);

        AssertErrorEnvelope(
            action,
            StatusCodes.Status404NotFound,
            "collector.query.session.not_found",
            "sessionId");
    }

    [Fact]
    public async Task GetCollectorSessionByMarket_WithSession_ShouldReturn200Envelope()
    {
        var marketId = Guid.NewGuid();
        var session = CreateCollectorSession(Guid.NewGuid(), "Running", marketId);
        var response = new GetCollectorSessionByMarketResponse(session);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(
                Arg.Is<GetCollectorSessionByMarketQuery>(query => query.MarketId == marketId),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<GetCollectorSessionByMarketResponse, ErrorList>(response));
        var controller = new CollectorController(mediator);

        var action = await controller.GetCollectorSessionByMarket(marketId, CancellationToken.None);

        var envelope = AssertOkEnvelope(action);
        ((GetCollectorSessionByMarketResponse)envelope.Result!).Session
            .Should().BeEquivalentTo(session);
    }

    [Fact]
    public async Task GetCollectorSessionByMarket_WithoutSessions_ShouldReturn200EnvelopeWithNullSession()
    {
        var response = new GetCollectorSessionByMarketResponse(null);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetCollectorSessionByMarketQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<GetCollectorSessionByMarketResponse, ErrorList>(response));
        var controller = new CollectorController(mediator);

        var action = await controller.GetCollectorSessionByMarket(
            Guid.NewGuid(),
            CancellationToken.None);

        var envelope = AssertOkEnvelope(action);
        ((GetCollectorSessionByMarketResponse)envelope.Result!).Session.Should().BeNull();
    }

    [Fact]
    public async Task GetCollectorSessionByMarket_WithInvalidId_ShouldReturn400ErrorEnvelope()
    {
        var error = new Error(
            "collector.query.market_id.required",
            "Market id is required.",
            ErrorType.ValueIsRequired,
            "marketId");
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetCollectorSessionByMarketQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<GetCollectorSessionByMarketResponse, ErrorList>(new ErrorList([error])));
        var controller = new CollectorController(mediator);

        var action = await controller.GetCollectorSessionByMarket(Guid.Empty, CancellationToken.None);

        AssertErrorEnvelope(
            action,
            StatusCodes.Status400BadRequest,
            "collector.query.market_id.required",
            "marketId");
    }

    private static CollectorSessionResponse CreateCollectorSession(
        Guid sessionId,
        string status,
        Guid? marketId = null)
    {
        return new CollectorSessionResponse(
            sessionId,
            marketId ?? Guid.NewGuid(),
            status,
            CreatedAt,
            CreatedAt.AddSeconds(1),
            status == "Running" ? null : CreatedAt.AddMinutes(30),
            status == "Failed" ? "collector.runtime.receive.failed" : null,
            status == "Failed" ? "Receive failed." : null,
            120,
            118,
            CreatedAt.AddMinutes(29),
            2);
    }

    private static Envelope AssertOkEnvelope<T>(ActionResult<T> action)
    {
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status200OK);
        return result.Value.Should().BeOfType<Envelope>().Subject;
    }

    private static void AssertErrorEnvelope<T>(
        ActionResult<T> action,
        int expectedStatus,
        string expectedCode,
        string expectedField)
    {
        var result = action.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(expectedStatus);
        var envelope = result.Value.Should().BeOfType<Envelope>().Subject;
        envelope.Result.Should().BeNull();
        envelope.ListErrors.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            ErrorCode = expectedCode,
            InvalidField = expectedField
        });
    }
}
