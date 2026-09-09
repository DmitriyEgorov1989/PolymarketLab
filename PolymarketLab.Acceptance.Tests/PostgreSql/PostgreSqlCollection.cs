using Xunit;

namespace PolymarketLab.Acceptance.Tests.PostgreSql;

internal static class PostgreSqlCollection
{
    public const string Name = "Host acceptance PostgreSQL";
}

[CollectionDefinition(PostgreSqlCollection.Name, DisableParallelization = true)]
public sealed class PostgreSqlCollectionDefinition
    : ICollectionFixture<PostgreSqlFixture>;
