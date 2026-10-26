using System.Text.Json;
using CSharpFunctionalExtensions;
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;
using PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;
using PolymarketLab.DataCollection.Core.Application.Simulation.RunExperiment;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.Errors;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.Simulation;

public sealed class RunExperimentHandlerTests
{
    [Fact]
    public async Task Handle_MissingManifestFingerprint_ShouldNotSilentlyDisablePinning()
    {
        var manifest = JsonSerializer.Deserialize<ExperimentDatasetManifest>(JsonSerializer.Serialize(new
        {
            FormatVersion = 1, Session = new { SessionId = SimulationFixture.SessionId },
            ProjectionVersion = 1, RawFingerprint = new string('a', 64)
        }))!;
        var reader = new UnexpectedReader();
        var handler = new RunExperimentHandler(reader, new ExperimentRunner(new OrderBookProjector()));
        var result = await handler.Handle(new RunExperimentCommand(SimulationFixture.SessionId, 1,
            manifest, ExperimentRunnerTests.Definition()), default);
        result.IsFailure.Should().BeTrue();
        reader.Called.Should().BeFalse();
    }

    private sealed class UnexpectedReader : IExperimentDatasetReader
    {
        public bool Called { get; private set; }
        public Task<Result<ExperimentDatasetSnapshot, Error>> ReadAsync(ExperimentDatasetRequest request, CancellationToken cancellationToken)
        {
            Called = true;
            throw new InvalidOperationException("Malformed pinned request must be rejected before I/O.");
        }
    }
}
