using Xunit;

namespace PolymarketLab.Markets.Infrastructure.Tests.Integration.Postgres;

internal static class PostgreSqlCollection
{
    public const string Name = "Markets PostgreSQL integration";
}

[CollectionDefinition(PostgreSqlCollection.Name, DisableParallelization = true)]
public sealed class PostgreSqlCollectionDefinition : ICollectionFixture<PostgreSqlFixture>;
