using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.Errors;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Application.UseCases.Commands.ReplayNormalization;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.Commands.ReplayNormalization;

public sealed class ReplayNormalizationHandlerTests
{
    [Fact]
    public async Task Handle_ShouldMapFiltersAndReturnSummary()
    {
        var sessionId = Guid.NewGuid();
        var service = new StubReplayService(Result.Success<NormalizationReplayResult, Error>(
            new NormalizationReplayResult(2, 3, 2, 1, 0, 0, 10, 12)));
        var handler = new ReplayNormalizationHandler(service);

        var result = await handler.Handle(
            new ReplayNormalizationCommand(1, 2, sessionId, "book"),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ReplayNormalizationResponse(
            1, 2, sessionId, "book", 2, 3, 2, 1, 0, 0, 10, 12));
        service.Filter.Should().Be(new NormalizationReplayFilter(
            1,
            2,
            PolymarketLab.SharedKernel.DomainModels.Ids.CollectorSessionId.Create(sessionId).Value,
            "book"));
    }

    [Fact]
    public async Task Handle_ServiceConflict_ShouldReturnError()
    {
        var error = ReplayNormalizationErrors.TargetProjectionVersionIsActive(2);
        var service = new StubReplayService(
            Result.Failure<NormalizationReplayResult, Error>(error));
        var handler = new ReplayNormalizationHandler(service);

        var result = await handler.Handle(
            new ReplayNormalizationCommand(1, 2, null, null),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Single().Should().Be(error);
    }

    private sealed class StubReplayService(
        Result<NormalizationReplayResult, Error> result) : INormalizationReplayService
    {
        public NormalizationReplayFilter? Filter { get; private set; }

        public Task<Result<NormalizationReplayResult, Error>> ReplayAsync(
            NormalizationReplayFilter filter,
            CancellationToken cancellationToken)
        {
            Filter = filter;
            return Task.FromResult(result);
        }
    }
}
