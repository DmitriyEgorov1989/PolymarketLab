using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
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
        session.FindProperty(nameof(CollectorSessionAggregate.Status))!
            .IsConcurrencyToken.Should().BeTrue();

        var activeMarketIndex = session.GetIndexes().Single(index =>
            index.GetDatabaseName() == "ux_collector_sessions_active_market");

        activeMarketIndex.IsUnique.Should().BeTrue();
        activeMarketIndex.GetFilter().Should().Be("\"status\" IN (0, 1, 2)");
        activeMarketIndex.Properties
            .Select(property => property.Name)
            .Should()
            .Equal(nameof(CollectorSessionAggregate.MarketId));
    }

    [Fact]
    public void Model_ShouldMapRawMarketMessageAndSessionReceivedIndex()
    {
        var message = _model.FindEntityType(typeof(RawMarketMessageRecord));

        message.Should().NotBeNull();
        message!.GetTableName().Should().Be("raw_market_messages");
        message.GetSchema().Should().Be("data_collection");
        AssertConverter<CollectorSessionId, Guid>(
            message,
            nameof(RawMarketMessageRecord.SessionId));

        var id = message.FindProperty(nameof(RawMarketMessageRecord.Id));
        id.Should().NotBeNull();
        id!.ValueGenerated.Should().Be(ValueGenerated.OnAdd);
        id.GetColumnType().Should().Be("bigint");

        var receivedAt = message.FindProperty(
            nameof(RawMarketMessageRecord.ReceivedAt));
        receivedAt.Should().NotBeNull();
        receivedAt!.GetColumnType().Should().Be("timestamp with time zone");

        var payload = message.FindProperty(nameof(RawMarketMessageRecord.Payload));
        payload.Should().NotBeNull();
        payload!.GetColumnType().Should().Be("bytea");
        payload.IsNullable.Should().BeFalse();

        var index = message.GetIndexes().Single(candidate =>
            candidate.GetDatabaseName() ==
            "ix_raw_market_messages_session_received_id");
        index.IsUnique.Should().BeFalse();
        index.Properties
            .Select(property => property.Name)
            .Should()
            .Equal(
                nameof(RawMarketMessageRecord.SessionId),
                nameof(RawMarketMessageRecord.ReceivedAt),
                nameof(RawMarketMessageRecord.Id));

        var foreignKey = message.GetForeignKeys().Single();
        foreignKey.PrincipalEntityType.ClrType.Should().Be(
            typeof(CollectorSessionAggregate));
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
    }

    [Fact]
    public void Model_ShouldMapCollectorSessionProgress()
    {
        var progress = _model.FindEntityType(typeof(CollectorSessionProgressRecord));

        progress.Should().NotBeNull();
        progress!.GetTableName().Should().Be("collector_session_progress");
        progress.GetSchema().Should().Be("data_collection");
        AssertConverter<CollectorSessionId, Guid>(
            progress,
            nameof(CollectorSessionProgressRecord.SessionId));
        progress.FindProperty(nameof(CollectorSessionProgressRecord.MessagesReceived))!
            .GetColumnType().Should().Be("bigint");
        progress.FindProperty(nameof(CollectorSessionProgressRecord.MessagesPersisted))!
            .GetColumnType().Should().Be("bigint");
        progress.FindProperty(nameof(CollectorSessionProgressRecord.LastMessageAt))!
            .GetColumnType().Should().Be("timestamp with time zone");
        progress.FindProperty(nameof(CollectorSessionProgressRecord.ReconnectCount))!
            .GetColumnType().Should().Be("bigint");
        progress.GetForeignKeys().Single().DeleteBehavior.Should().Be(DeleteBehavior.Cascade);
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
