using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.Postgres;

public sealed class DataCollectionDbContextModelTests
{
    private readonly IModel _model = CreateContext().Model;

    [Fact]
    public void Model_ShouldMapCollectorSessionAndActiveMarketIndex()
    {
        var session = _model.FindEntityType(typeof(CollectorSessionAggregate));

        session.Should().NotBeNull();
        session!.GetTableName().Should().Be("collector_sessions");
        session.GetSchema().Should().Be("data_collection");
        AssertConverter<CollectorSessionId, Guid>(
            session,
            nameof(CollectorSessionAggregate.Id));
        AssertConverter<MarketId, Guid>(
            session,
            nameof(CollectorSessionAggregate.MarketId));

        var activeMarketIndex = session.GetIndexes().Single(index =>
            index.GetDatabaseName() == "ux_collector_sessions_active_market");

        activeMarketIndex.IsUnique.Should().BeTrue();
        activeMarketIndex.GetFilter().Should().Be("\"status\" IN (0, 1, 2)");
        activeMarketIndex.Properties
            .Select(property => property.Name)
            .Should()
            .Equal(nameof(CollectorSessionAggregate.MarketId));
    }

    private static DataCollectionDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=data_collection_model;Username=test;Password=test")
            .Options;

        return new DataCollectionDbContext(options);
    }

    private static void AssertConverter<TModel, TProvider>(
        IEntityType entityType,
        string propertyName)
    {
        var property = entityType.FindProperty(propertyName);

        property.Should().NotBeNull();
        var converter = property!.GetTypeMapping().Converter;
        converter.Should().NotBeNull();
        converter!.ModelClrType.Should().Be(typeof(TModel));
        converter.ProviderClrType.Should().Be(typeof(TProvider));
    }
}
