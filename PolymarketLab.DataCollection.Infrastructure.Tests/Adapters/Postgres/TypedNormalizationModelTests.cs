using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.Postgres;

public sealed class TypedNormalizationModelTests
{
    private readonly IModel _model = CreateContext().Model;

    [Fact]
    public void Model_ShouldMapLastTradePrice()
    {
        var entity = AssertColumns<LastTradePriceEntity>(
            "last_trade_price",
            (nameof(LastTradePriceEntity.EventId), "event_id"),
            (nameof(LastTradePriceEntity.Price), "price"),
            (nameof(LastTradePriceEntity.Size), "size"),
            (nameof(LastTradePriceEntity.Side), "side"),
            (nameof(LastTradePriceEntity.FeeRateBps), "fee_rate_bps"),
            (nameof(LastTradePriceEntity.TransactionHash), "transaction_hash"));

        AssertSharedPrimaryKey(entity, nameof(LastTradePriceEntity.EventId));
        AssertNumeric(entity, nameof(LastTradePriceEntity.Price), 29, 28, false);
        AssertNumeric(entity, nameof(LastTradePriceEntity.Size), 29, 18, true);
        AssertNumeric(entity, nameof(LastTradePriceEntity.FeeRateBps), 29, 18, true);
        AssertConverter<TradeSide, int>(entity, nameof(LastTradePriceEntity.Side));
        entity.FindProperty(nameof(LastTradePriceEntity.TransactionHash))!
            .GetMaxLength().Should().BeNull();
        AssertForeignKey<NormalizedEventRecord>(
            entity,
            nameof(LastTradePriceEntity.EventId),
            nameof(NormalizedEventRecord.Id),
            DeleteBehavior.Cascade,
            true);
    }

    [Fact]
    public void Model_ShouldMapPriceChangeItems()
    {
        var entity = AssertColumns<PriceChangeItemEntity>(
            "price_change",
            (nameof(PriceChangeItemEntity.Id), "id"),
            (nameof(PriceChangeItemEntity.EventId), "event_id"),
            (nameof(PriceChangeItemEntity.ItemIndex), "item_index"),
            (nameof(PriceChangeItemEntity.AssetId), "asset_id"),
            (nameof(PriceChangeItemEntity.SourceTimestamp), "source_timestamp"),
            (nameof(PriceChangeItemEntity.Price), "price"),
            (nameof(PriceChangeItemEntity.Size), "size"),
            (nameof(PriceChangeItemEntity.Side), "side"),
            (nameof(PriceChangeItemEntity.Hash), "hash"),
            (nameof(PriceChangeItemEntity.BestBid), "best_bid"),
            (nameof(PriceChangeItemEntity.BestAsk), "best_ask"));

        AssertGeneratedPrimaryKey(entity, nameof(PriceChangeItemEntity.Id));
        AssertNumeric(entity, nameof(PriceChangeItemEntity.Price), 29, 28, false);
        AssertNumeric(entity, nameof(PriceChangeItemEntity.Size), 29, 18, false);
        AssertNumeric(entity, nameof(PriceChangeItemEntity.BestBid), 29, 28, true);
        AssertNumeric(entity, nameof(PriceChangeItemEntity.BestAsk), 29, 28, true);
        AssertConverter<TradeSide, int>(entity, nameof(PriceChangeItemEntity.Side));
        AssertIndex(
            entity,
            "ux_price_change_event_id_item_index",
            true,
            nameof(PriceChangeItemEntity.EventId),
            nameof(PriceChangeItemEntity.ItemIndex));
        AssertIndex(
            entity,
            "ix_price_change_asset_id_source_timestamp",
            false,
            nameof(PriceChangeItemEntity.AssetId),
            nameof(PriceChangeItemEntity.SourceTimestamp));
        AssertForeignKey<NormalizedEventRecord>(
            entity,
            nameof(PriceChangeItemEntity.EventId),
            nameof(NormalizedEventRecord.Id),
            DeleteBehavior.Cascade,
            false);
    }

    [Fact]
    public void Model_ShouldMapBookSnapshotAndLevels()
    {
        var snapshot = AssertColumns<BookSnapshotEntity>(
            "book_snapshots",
            (nameof(BookSnapshotEntity.EventId), "event_id"),
            (nameof(BookSnapshotEntity.Hash), "hash"),
            (nameof(BookSnapshotEntity.TickSize), "tick_size"),
            (nameof(BookSnapshotEntity.LastTradePrice), "last_trade_price"));
        AssertSharedPrimaryKey(snapshot, nameof(BookSnapshotEntity.EventId));
        AssertNumeric(snapshot, nameof(BookSnapshotEntity.TickSize), 29, 28, true);
        AssertNumeric(snapshot, nameof(BookSnapshotEntity.LastTradePrice), 29, 28, true);
        AssertForeignKey<NormalizedEventRecord>(
            snapshot,
            nameof(BookSnapshotEntity.EventId),
            nameof(NormalizedEventRecord.Id),
            DeleteBehavior.Cascade,
            true);

        var level = AssertColumns<BookLevelEntity>(
            "book_levels",
            (nameof(BookLevelEntity.Id), "id"),
            (nameof(BookLevelEntity.EventId), "event_id"),
            (nameof(BookLevelEntity.Side), "side"),
            (nameof(BookLevelEntity.LevelIndex), "level_index"),
            (nameof(BookLevelEntity.Price), "price"),
            (nameof(BookLevelEntity.Size), "size"));
        AssertGeneratedPrimaryKey(level, nameof(BookLevelEntity.Id));
        AssertConverter<OrderBookSide, int>(level, nameof(BookLevelEntity.Side));
        AssertNumeric(level, nameof(BookLevelEntity.Price), 29, 28, false);
        AssertNumeric(level, nameof(BookLevelEntity.Size), 29, 18, false);
        AssertIndex(
            level,
            "ux_book_levels_event_side_level_index",
            true,
            nameof(BookLevelEntity.EventId),
            nameof(BookLevelEntity.Side),
            nameof(BookLevelEntity.LevelIndex));
        AssertForeignKey<BookSnapshotEntity>(
            level,
            nameof(BookLevelEntity.EventId),
            nameof(BookSnapshotEntity.EventId),
            DeleteBehavior.Cascade,
            false);
    }

    [Fact]
    public void Model_ShouldMapTickSizeChangeAndBestBidAsk()
    {
        var tickSize = AssertColumns<TickSizeChangeEntity>(
            "tick_size_changes",
            (nameof(TickSizeChangeEntity.EventId), "event_id"),
            (nameof(TickSizeChangeEntity.OldTickSize), "old_tick_size"),
            (nameof(TickSizeChangeEntity.NewTickSize), "new_tick_size"));
        AssertSharedPrimaryKey(tickSize, nameof(TickSizeChangeEntity.EventId));
        AssertNumeric(tickSize, nameof(TickSizeChangeEntity.OldTickSize), 29, 28, false);
        AssertNumeric(tickSize, nameof(TickSizeChangeEntity.NewTickSize), 29, 28, false);
        AssertForeignKey<NormalizedEventRecord>(
            tickSize,
            nameof(TickSizeChangeEntity.EventId),
            nameof(NormalizedEventRecord.Id),
            DeleteBehavior.Cascade,
            true);

        var quote = AssertColumns<BestBidAskEntity>(
            "best_bid_asks",
            (nameof(BestBidAskEntity.EventId), "event_id"),
            (nameof(BestBidAskEntity.BestBid), "best_bid"),
            (nameof(BestBidAskEntity.BestAsk), "best_ask"),
            (nameof(BestBidAskEntity.Spread), "spread"));
        AssertSharedPrimaryKey(quote, nameof(BestBidAskEntity.EventId));
        AssertNumeric(quote, nameof(BestBidAskEntity.BestBid), 29, 28, false);
        AssertNumeric(quote, nameof(BestBidAskEntity.BestAsk), 29, 28, false);
        AssertNumeric(quote, nameof(BestBidAskEntity.Spread), 29, 18, false);
        AssertForeignKey<NormalizedEventRecord>(
            quote,
            nameof(BestBidAskEntity.EventId),
            nameof(NormalizedEventRecord.Id),
            DeleteBehavior.Cascade,
            true);
    }

    [Fact]
    public void Model_ShouldMapNewMarketAndOrderedAssets()
    {
        var market = AssertColumns<NewMarketEntity>(
            "new_markets",
            (nameof(NewMarketEntity.EventId), "event_id"),
            (nameof(NewMarketEntity.ExternalMarketId), "external_market_id"),
            (nameof(NewMarketEntity.Question), "question"),
            (nameof(NewMarketEntity.Slug), "slug"),
            (nameof(NewMarketEntity.Description), "description"),
            (nameof(NewMarketEntity.Active), "active"),
            (nameof(NewMarketEntity.SportsMarketType), "sports_market_type"),
            (nameof(NewMarketEntity.Line), "line"),
            (nameof(NewMarketEntity.GameStartTime), "game_start_time"),
            (nameof(NewMarketEntity.OrderPriceMinTickSize), "order_price_min_tick_size"),
            (nameof(NewMarketEntity.GroupItemTitle), "group_item_title"),
            (nameof(NewMarketEntity.TakerBaseFee), "taker_base_fee"),
            (nameof(NewMarketEntity.FeesEnabled), "fees_enabled"),
            (nameof(NewMarketEntity.EventMessageId), "event_message_id"),
            (nameof(NewMarketEntity.EventMessageTicker), "event_message_ticker"),
            (nameof(NewMarketEntity.EventMessageSlug), "event_message_slug"),
            (nameof(NewMarketEntity.EventMessageTitle), "event_message_title"),
            (nameof(NewMarketEntity.EventMessageDescription), "event_message_description"),
            (nameof(NewMarketEntity.FeeScheduleExponent), "fee_schedule_exponent"),
            (nameof(NewMarketEntity.FeeScheduleRate), "fee_schedule_rate"),
            (nameof(NewMarketEntity.FeeScheduleRebateRate), "fee_schedule_rebate_rate"),
            (nameof(NewMarketEntity.FeeScheduleTakerOnly), "fee_schedule_taker_only"));
        AssertSharedPrimaryKey(market, nameof(NewMarketEntity.EventId));
        AssertNumeric(market, nameof(NewMarketEntity.Line), 29, 18, true);
        AssertNumeric(market, nameof(NewMarketEntity.OrderPriceMinTickSize), 29, 28, false);
        AssertNumeric(market, nameof(NewMarketEntity.TakerBaseFee), 29, 18, false);
        AssertNumeric(market, nameof(NewMarketEntity.FeeScheduleExponent), 29, 18, false);
        AssertNumeric(market, nameof(NewMarketEntity.FeeScheduleRate), 29, 18, false);
        AssertNumeric(market, nameof(NewMarketEntity.FeeScheduleRebateRate), 29, 18, false);
        AssertForeignKey<NormalizedEventRecord>(
            market,
            nameof(NewMarketEntity.EventId),
            nameof(NormalizedEventRecord.Id),
            DeleteBehavior.Cascade,
            true);

        var asset = AssertColumns<NewMarketAssetEntity>(
            "new_market_assets",
            (nameof(NewMarketAssetEntity.Id), "id"),
            (nameof(NewMarketAssetEntity.EventId), "event_id"),
            (nameof(NewMarketAssetEntity.ItemIndex), "item_index"),
            (nameof(NewMarketAssetEntity.AssetId), "asset_id"),
            (nameof(NewMarketAssetEntity.Outcome), "outcome"));
        AssertGeneratedPrimaryKey(asset, nameof(NewMarketAssetEntity.Id));
        AssertIndex(
            asset,
            "ux_new_market_assets_event_id_item_index",
            true,
            nameof(NewMarketAssetEntity.EventId),
            nameof(NewMarketAssetEntity.ItemIndex));
        AssertForeignKey<NewMarketEntity>(
            asset,
            nameof(NewMarketAssetEntity.EventId),
            nameof(NewMarketEntity.EventId),
            DeleteBehavior.Cascade,
            false);
    }

    [Fact]
    public void Model_ShouldMapMarketResolutionAndOrderedAssets()
    {
        var resolution = AssertColumns<MarketResolutionEntity>(
            "market_resolutions",
            (nameof(MarketResolutionEntity.EventId), "event_id"),
            (nameof(MarketResolutionEntity.ExternalMarketId), "external_market_id"),
            (nameof(MarketResolutionEntity.WinningAssetId), "winning_asset_id"),
            (nameof(MarketResolutionEntity.WinningOutcome), "winning_outcome"));
        AssertSharedPrimaryKey(resolution, nameof(MarketResolutionEntity.EventId));
        AssertForeignKey<NormalizedEventRecord>(
            resolution,
            nameof(MarketResolutionEntity.EventId),
            nameof(NormalizedEventRecord.Id),
            DeleteBehavior.Cascade,
            true);

        var asset = AssertColumns<MarketResolutionAssetEntity>(
            "market_resolution_assets",
            (nameof(MarketResolutionAssetEntity.Id), "id"),
            (nameof(MarketResolutionAssetEntity.EventId), "event_id"),
            (nameof(MarketResolutionAssetEntity.ItemIndex), "item_index"),
            (nameof(MarketResolutionAssetEntity.AssetId), "asset_id"));
        AssertGeneratedPrimaryKey(asset, nameof(MarketResolutionAssetEntity.Id));
        AssertIndex(
            asset,
            "ux_market_resolution_assets_event_id_item_index",
            true,
            nameof(MarketResolutionAssetEntity.EventId),
            nameof(MarketResolutionAssetEntity.ItemIndex));
        AssertForeignKey<MarketResolutionEntity>(
            asset,
            nameof(MarketResolutionAssetEntity.EventId),
            nameof(MarketResolutionEntity.EventId),
            DeleteBehavior.Cascade,
            false);
    }

    private IEntityType AssertColumns<TEntity>(
        string tableName,
        params (string Property, string Column)[] columns)
    {
        var entity = _model.FindEntityType(typeof(TEntity));
        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be(tableName);
        entity.GetSchema().Should().Be("data_collection");

        var table = StoreObjectIdentifier.Table(tableName, "data_collection");
        entity.GetProperties()
            .ToDictionary(
                property => property.Name,
                property => property.GetColumnName(table)!)
            .Should()
            .BeEquivalentTo(columns.ToDictionary(column => column.Property, column => column.Column));

        return entity;
    }

    private static void AssertSharedPrimaryKey(IEntityType entity, string propertyName)
    {
        entity.FindPrimaryKey()!.Properties.Select(property => property.Name)
            .Should().Equal(propertyName);
        var property = entity.FindProperty(propertyName)!;
        property.GetColumnType().Should().Be("bigint");
        property.ValueGenerated.Should().Be(ValueGenerated.Never);
    }

    private static void AssertGeneratedPrimaryKey(IEntityType entity, string propertyName)
    {
        entity.FindPrimaryKey()!.Properties.Select(property => property.Name)
            .Should().Equal(propertyName);
        var property = entity.FindProperty(propertyName)!;
        property.GetColumnType().Should().Be("bigint");
        property.ValueGenerated.Should().Be(ValueGenerated.OnAdd);
    }

    private static void AssertNumeric(
        IEntityType entity,
        string propertyName,
        int precision,
        int scale,
        bool nullable)
    {
        var property = entity.FindProperty(propertyName)!;
        property.GetColumnType().Should().Be($"numeric({precision},{scale})");
        property.GetPrecision().Should().Be(precision);
        property.GetScale().Should().Be(scale);
        property.IsNullable.Should().Be(nullable);
    }

    private static void AssertIndex(
        IEntityType entity,
        string databaseName,
        bool unique,
        params string[] properties)
    {
        var index = entity.GetIndexes().Single(candidate =>
            candidate.GetDatabaseName() == databaseName);
        index.IsUnique.Should().Be(unique);
        index.Properties.Select(property => property.Name).Should().Equal(properties);
    }

    private static void AssertForeignKey<TPrincipal>(
        IEntityType entity,
        string propertyName,
        string principalPropertyName,
        DeleteBehavior deleteBehavior,
        bool unique)
    {
        var foreignKey = entity.GetForeignKeys().Single();
        foreignKey.Properties.Select(property => property.Name).Should().Equal(propertyName);
        foreignKey.PrincipalEntityType.ClrType.Should().Be(typeof(TPrincipal));
        foreignKey.PrincipalKey.Properties.Select(property => property.Name)
            .Should().Equal(principalPropertyName);
        foreignKey.DeleteBehavior.Should().Be(deleteBehavior);
        foreignKey.IsUnique.Should().Be(unique);
    }

    private static void AssertConverter<TModel, TProvider>(
        IEntityType entity,
        string propertyName)
    {
        var converter = entity.FindProperty(propertyName)!.GetTypeMapping().Converter;
        converter.Should().NotBeNull();
        converter!.ModelClrType.Should().Be(typeof(TModel));
        converter.ProviderClrType.Should().Be(typeof(TProvider));
    }

    private static DataCollectionDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DataCollectionDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=data_collection_model;Username=test;Password=test")
            .Options;

        return new DataCollectionDbContext(options);
    }
}
