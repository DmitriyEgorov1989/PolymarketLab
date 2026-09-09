using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PolymarketLab.Acceptance.Tests.PostgreSql;
using Xunit;

namespace PolymarketLab.Acceptance.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class HostMigrationAcceptanceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task CleanDatabase_ShouldApplyBothMigrationChainsWithoutPendingMigrations()
    {
        await using var database = await fixture.CreateDatabaseAsync();

        await database.ApplyMigrationsAsync();

        await using var markets = database.CreateMarketsContext();
        await using var dataCollection = database.CreateDataCollectionContext();
        (await markets.Database.GetAppliedMigrationsAsync()).Should().NotBeEmpty();
        (await dataCollection.Database.GetAppliedMigrationsAsync()).Should().NotBeEmpty();
        (await markets.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await dataCollection.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }
}
