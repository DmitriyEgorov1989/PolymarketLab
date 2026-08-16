using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
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

    [Fact]
    public void Model_ShouldMapRawMessageNormalizationLedger()
    {
        var normalization = _model.FindEntityType(
            typeof(RawMessageNormalizationRecord));

        normalization.Should().NotBeNull();
        normalization!.GetTableName().Should().Be("raw_message_normalizations");
        normalization.GetSchema().Should().Be("data_collection");

        var table = StoreObjectIdentifier.Table(
            "raw_message_normalizations",
            "data_collection");
        var expectedColumns = new Dictionary<string, string>
        {
            [nameof(RawMessageNormalizationRecord.RawMessageId)] = "raw_message_id",
            [nameof(RawMessageNormalizationRecord.ProjectionVersion)] = "projection_version",
            [nameof(RawMessageNormalizationRecord.Status)] = "status",
            [nameof(RawMessageNormalizationRecord.AttemptCount)] = "attempt_count",
            [nameof(RawMessageNormalizationRecord.ClaimedAt)] = "claimed_at",
            [nameof(RawMessageNormalizationRecord.CompletedAt)] = "completed_at",
            [nameof(RawMessageNormalizationRecord.ErrorCode)] = "error_code",
            [nameof(RawMessageNormalizationRecord.ErrorMessage)] = "error_message",
            [nameof(RawMessageNormalizationRecord.ErrorField)] = "error_field"
        };

        normalization.GetProperties()
            .ToDictionary(
                property => property.Name,
                property => property.GetColumnName(table)!)
            .Should()
            .BeEquivalentTo(expectedColumns);

        var rawMessageId = normalization.FindProperty(
            nameof(RawMessageNormalizationRecord.RawMessageId));
        rawMessageId.Should().NotBeNull();
        rawMessageId!.GetColumnType().Should().Be("bigint");
        rawMessageId.ValueGenerated.Should().Be(ValueGenerated.Never);

        normalization.FindProperty(nameof(RawMessageNormalizationRecord.ClaimedAt))!
            .GetColumnType().Should().Be("timestamp with time zone");
        normalization.FindProperty(nameof(RawMessageNormalizationRecord.CompletedAt))!
            .GetColumnType().Should().Be("timestamp with time zone");
        normalization.FindProperty(nameof(RawMessageNormalizationRecord.ErrorCode))!
            .IsNullable.Should().BeTrue();
        normalization.FindProperty(nameof(RawMessageNormalizationRecord.ErrorMessage))!
            .IsNullable.Should().BeTrue();
        normalization.FindProperty(nameof(RawMessageNormalizationRecord.ErrorField))!
            .IsNullable.Should().BeTrue();
        AssertConverter<NormalizationStatus, int>(
            normalization,
            nameof(RawMessageNormalizationRecord.Status));

        normalization.FindPrimaryKey()!.Properties
            .Select(property => property.Name)
            .Should()
            .Equal(
                nameof(RawMessageNormalizationRecord.RawMessageId),
                nameof(RawMessageNormalizationRecord.ProjectionVersion));

        var index = normalization.GetIndexes().Single(candidate =>
            candidate.GetDatabaseName() ==
            "ix_raw_message_normalizations_projection_status_raw_message_id");
        index.IsUnique.Should().BeFalse();
        index.Properties
            .Select(property => property.Name)
            .Should()
            .Equal(
                nameof(RawMessageNormalizationRecord.ProjectionVersion),
                nameof(RawMessageNormalizationRecord.Status),
                nameof(RawMessageNormalizationRecord.RawMessageId));

        var foreignKey = normalization.GetForeignKeys().Single();
        foreignKey.Properties
            .Select(property => property.Name)
            .Should()
            .Equal(nameof(RawMessageNormalizationRecord.RawMessageId));
        foreignKey.PrincipalEntityType.ClrType.Should().Be(
            typeof(RawMarketMessageRecord));
        foreignKey.PrincipalKey.Properties
            .Select(property => property.Name)
            .Should()
            .Equal(nameof(RawMarketMessageRecord.Id));
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
    }

    [Fact]
    public void Model_ShouldMapNormalizedEventHeaderAndIdempotencyIndex()
    {
        var normalizedEvent = _model.FindEntityType(typeof(NormalizedEventRecord));

        normalizedEvent.Should().NotBeNull();
        normalizedEvent!.GetTableName().Should().Be("normalized_events");
        normalizedEvent.GetSchema().Should().Be("data_collection");

        var table = StoreObjectIdentifier.Table(
            "normalized_events",
            "data_collection");
        var expectedColumns = new Dictionary<string, string>
        {
            [nameof(NormalizedEventRecord.Id)] = "id",
            [nameof(NormalizedEventRecord.RawMessageId)] = "raw_message_id",
            [nameof(NormalizedEventRecord.RawItemIndex)] = "raw_item_index",
            [nameof(NormalizedEventRecord.ProjectionVersion)] = "projection_version",
            [nameof(NormalizedEventRecord.NormalizerVersion)] = "normalizer_version",
            [nameof(NormalizedEventRecord.EventType)] = "event_type",
            [nameof(NormalizedEventRecord.SessionId)] = "session_id",
            [nameof(NormalizedEventRecord.ReceivedAt)] = "received_at",
            [nameof(NormalizedEventRecord.SourceTimestamp)] = "source_timestamp",
            [nameof(NormalizedEventRecord.MarketConditionId)] = "market_condition_id",
            [nameof(NormalizedEventRecord.AssetId)] = "asset_id",
            [nameof(NormalizedEventRecord.NormalizedAt)] = "normalized_at"
        };

        normalizedEvent.GetProperties()
            .ToDictionary(
                property => property.Name,
                property => property.GetColumnName(table)!)
            .Should()
            .BeEquivalentTo(expectedColumns);

        var id = normalizedEvent.FindProperty(nameof(NormalizedEventRecord.Id));
        id.Should().NotBeNull();
        id!.GetColumnType().Should().Be("bigint");
        id.ValueGenerated.Should().Be(ValueGenerated.OnAdd);
        normalizedEvent.FindPrimaryKey()!.Properties
            .Select(property => property.Name)
            .Should()
            .Equal(nameof(NormalizedEventRecord.Id));

        normalizedEvent.FindProperty(nameof(NormalizedEventRecord.RawMessageId))!
            .GetColumnType().Should().Be("bigint");
        normalizedEvent.FindProperty(nameof(NormalizedEventRecord.SourceTimestamp))!
            .GetColumnType().Should().Be("bigint");
        normalizedEvent.FindProperty(nameof(NormalizedEventRecord.ReceivedAt))!
            .GetColumnType().Should().Be("timestamp with time zone");
        normalizedEvent.FindProperty(nameof(NormalizedEventRecord.NormalizedAt))!
            .GetColumnType().Should().Be("timestamp with time zone");
        normalizedEvent.FindProperty(nameof(NormalizedEventRecord.EventType))!
            .IsNullable.Should().BeFalse();
        normalizedEvent.FindProperty(nameof(NormalizedEventRecord.SourceTimestamp))!
            .IsNullable.Should().BeTrue();
        normalizedEvent.FindProperty(nameof(NormalizedEventRecord.MarketConditionId))!
            .IsNullable.Should().BeTrue();
        normalizedEvent.FindProperty(nameof(NormalizedEventRecord.AssetId))!
            .IsNullable.Should().BeTrue();
        AssertConverter<CollectorSessionId, Guid>(
            normalizedEvent,
            nameof(NormalizedEventRecord.SessionId));

        var idempotencyIndex = normalizedEvent.GetIndexes().Single(index =>
            index.GetDatabaseName() ==
            "ux_normalized_events_raw_message_item_projection");
        idempotencyIndex.IsUnique.Should().BeTrue();
        idempotencyIndex.Properties
            .Select(property => property.Name)
            .Should()
            .Equal(
                nameof(NormalizedEventRecord.RawMessageId),
                nameof(NormalizedEventRecord.RawItemIndex),
                nameof(NormalizedEventRecord.ProjectionVersion));

        var foreignKey = normalizedEvent.GetForeignKeys().Single();
        foreignKey.Properties
            .Select(property => property.Name)
            .Should()
            .Equal(nameof(NormalizedEventRecord.RawMessageId));
        foreignKey.PrincipalEntityType.ClrType.Should().Be(
            typeof(RawMarketMessageRecord));
        foreignKey.PrincipalKey.Properties
            .Select(property => property.Name)
            .Should()
            .Equal(nameof(RawMarketMessageRecord.Id));
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
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
