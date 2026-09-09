using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.Markets.Infrastructure.Adapters.Postgres;

namespace PolymarketLab.Acceptance.Tests.PostgreSql;

public sealed class AcceptanceDatabase(
    string databaseName,
    string connectionString,
    string administrativeConnectionString) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public async Task ApplyMigrationsAsync()
    {
        await using (var markets = CreateMarketsContext())
        {
            await markets.Database.MigrateAsync();
            (await markets.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }

        await using (var dataCollection = CreateDataCollectionContext())
        {
            await dataCollection.Database.MigrateAsync();
            (await dataCollection.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }
    }

    public MarketsDbContext CreateMarketsContext()
    {
        var options = new DbContextOptionsBuilder<MarketsDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new MarketsDbContext(options);
    }

    public DataCollectionDbContext CreateDataCollectionContext()
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new DataCollectionDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(administrativeConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)} WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"")}\"";
}
