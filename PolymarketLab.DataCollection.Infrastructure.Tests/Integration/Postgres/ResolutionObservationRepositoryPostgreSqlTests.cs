using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

[Collection(PostgreSqlCollection.Name)]
public sealed class ResolutionObservationRepositoryPostgreSqlTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task SaveFailureAsync_AfterInvalidationFence_ShouldNotPersistObservation()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        var session = CreateSession();
        session.BeginInvalidation(
            session.CreatedAt.AddSeconds(1),
            CollectorStopReason.ResolutionFailure,
            "collector.resolution.conflict",
            "Resolution conflict.");
        context.CollectorSessions.Add(session);
        await context.SaveChangesAsync();

        var observationId = await new ResolutionObservationRepository(context)
            .SaveFailureAsync(
                new DurableResolutionFailure(
                    session.Id,
                    ResolutionObservationSource.Gamma,
                    session.CreatedAt.AddSeconds(2),
                    "gamma.terminal_resolution.timeout",
                    "Gamma timeout."),
                CancellationToken.None);

        observationId.Should().Be(0);
        context.ChangeTracker.Clear();
        (await context.ResolutionObservations.CountAsync()).Should().Be(0);
    }

    private static CollectorSessionAggregate CreateSession()
    {
        var createdAt = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
        return CollectorSessionAggregate.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            MarketId.Create(Guid.NewGuid()).Value,
            "event-1",
            "event-slug",
            "market-1",
            "market-slug",
            "0xcondition",
            createdAt.AddMinutes(2),
            createdAt.AddMinutes(7),
            1,
            [
                new CollectorSessionTokenDefinition(TokenId.Create("token-1").Value, "Yes", 0),
                new CollectorSessionTokenDefinition(TokenId.Create("token-2").Value, "No", 1)
            ],
            createdAt).Value;
    }

    private static DataCollectionDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new DataCollectionDbContext(options);
    }
}
