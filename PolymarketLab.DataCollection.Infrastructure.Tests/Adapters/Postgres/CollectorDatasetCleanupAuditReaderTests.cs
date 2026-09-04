using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.Postgres;

public sealed class CollectorDatasetCleanupAuditReaderTests
{
    private static readonly DateTimeOffset CompletedAt =
        DateTimeOffset.Parse("2026-09-03T10:01:00Z");

    [Fact]
    public async Task GetBySessionIdAsync_WithExistingAudit_ShouldReturnAudit()
    {
        var options = CreateOptions(new InMemoryDatabaseRoot());
        await using var context = new DataCollectionDbContext(options);
        var session = CreateSession();
        context.Add(session);
        await context.SaveChangesAsync();
        var audit = new CollectorDatasetCleanupAudit(
            session.Id,
            CompletedAt,
            1,
            2,
            14);
        context.Add(new PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models
            .CollectorDatasetCleanupAuditRecord(audit));
        await context.SaveChangesAsync();
        var reader = new CollectorDatasetCleanupAuditReader(context);

        var result = await reader.GetBySessionIdAsync(session.Id, CancellationToken.None);

        result.Should().BeEquivalentTo(audit);
    }

    [Fact]
    public async Task GetBySessionIdAsync_WithMissingAudit_ShouldReturnNull()
    {
        var options = CreateOptions(new InMemoryDatabaseRoot());
        await using var context = new DataCollectionDbContext(options);
        var session = CreateSession();
        context.Add(session);
        await context.SaveChangesAsync();
        var reader = new CollectorDatasetCleanupAuditReader(context);

        var result = await reader.GetBySessionIdAsync(session.Id, CancellationToken.None);

        result.Should().BeNull();
    }

    private static DbContextOptions<DataCollectionDbContext> CreateOptions(
        InMemoryDatabaseRoot databaseRoot) =>
        new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), databaseRoot)
            .Options;

    private static CollectorSession CreateSession() =>
        CollectorSession.Create(
            CollectorSessionId.Create(Guid.NewGuid()).Value,
            MarketId.Create(Guid.NewGuid()).Value,
            "event-123",
            "btc-updown-5m-1200",
            "market-123",
            "btc-updown-5m-1200",
            "0xabc",
            DateTimeOffset.Parse("2026-09-03T10:03:00Z"),
            DateTimeOffset.Parse("2026-09-03T10:08:00Z"),
            3,
            [
                new CollectorSessionTokenDefinition(TokenId.Create("1001").Value, "Yes", 0),
                new CollectorSessionTokenDefinition(TokenId.Create("1002").Value, "No", 1)
            ],
            DateTimeOffset.Parse("2026-09-03T10:00:00Z")).Value;
}
