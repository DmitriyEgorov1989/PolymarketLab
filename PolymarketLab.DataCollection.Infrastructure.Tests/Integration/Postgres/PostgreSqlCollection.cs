using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Integration.Postgres;

internal static class PostgreSqlCollection
{
    public const string Name = "DataCollection PostgreSQL integration";
}

[CollectionDefinition(PostgreSqlCollection.Name, DisableParallelization = true)]
public sealed class PostgreSqlCollectionDefinition
    : ICollectionFixture<PostgreSqlFixture>;
