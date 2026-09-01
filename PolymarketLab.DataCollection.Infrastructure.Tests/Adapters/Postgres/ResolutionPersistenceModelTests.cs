using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.Postgres;

public sealed class ResolutionPersistenceModelTests
{
    [Fact]
    public void Model_ShouldMapResolutionStateObservationsAndOutcomes()
    {
        using var dbContext = CreateContext();
        var model = dbContext.Model;

        var state = model.FindEntityType(typeof(ResolutionStateEntity));
        state.Should().NotBeNull();
        state!.GetTableName().Should().Be("resolution_states");
        state.GetSchema().Should().Be("data_collection");

        var observation = model.FindEntityType(typeof(ResolutionObservationEntity));
        observation.Should().NotBeNull();
        observation!.GetTableName().Should().Be("resolution_observations");
        var wsIndex = observation.GetIndexes().Single(index =>
            index.GetDatabaseName() == "ux_resolution_observations_ws_raw_item");
        wsIndex.IsUnique.Should().BeTrue();
        wsIndex.Properties.Select(property => property.Name).Should().Equal(
            nameof(ResolutionObservationEntity.RawMessageId),
            nameof(ResolutionObservationEntity.RawItemIndex));

        var outcome = model.FindEntityType(typeof(ResolutionObservationOutcomeEntity));
        outcome.Should().NotBeNull();
        outcome!.GetTableName().Should().Be("resolution_observation_outcomes");
        outcome.FindPrimaryKey()!.Properties.Select(property => property.Name).Should().Equal(
            nameof(ResolutionObservationOutcomeEntity.ObservationId),
            nameof(ResolutionObservationOutcomeEntity.OutcomeIndex));

        var session = model.FindEntityType(typeof(CollectorSessionAggregate));
        session!.FindProperty(nameof(CollectorSessionAggregate.ResolutionSignaledAt))
            .Should().NotBeNull();
        session.FindProperty(nameof(CollectorSessionAggregate.ResolutionConfirmedAt))
            .Should().NotBeNull();
        session.FindProperty(nameof(CollectorSessionAggregate.WinningTokenId))
            .Should().NotBeNull();
        session.FindProperty(nameof(CollectorSessionAggregate.WinningOutcome))
            .Should().NotBeNull();
        session.FindProperty(nameof(CollectorSessionAggregate.ResolutionConnectionEpoch))
            .Should().NotBeNull();
    }

    private static DataCollectionDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql("Host=localhost;Database=resolution_model;Username=test;Password=test")
            .Options;
        return new DataCollectionDbContext(options);
    }
}
