using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PolymarketLab.Markets.Core.Domain.Models.Market.Entity;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.Markets.Infrastructure.Adapters.Postgres;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;
using MarketAggregate = PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate.Market;

namespace PolymarketLab.Markets.Infrastructure.Tests.Adapters.Postgres;

public sealed class MarketsDbContextModelTests
{
    private readonly IModel _model = CreateContext().Model;

    [Fact]
    public void Model_ShouldMapMarketValueObjectsAndIdentityIndexes()
    {
        var market = _model.FindEntityType(typeof(MarketAggregate));

        market.Should().NotBeNull();
        market!.GetTableName().Should().Be("markets");
        AssertConverter<MarketId, Guid>(market, nameof(MarketAggregate.Id));
        AssertConverter<ExternalEventId, string>(market, nameof(MarketAggregate.ExternalEventId));
        AssertConverter<EventSlug, string>(market, nameof(MarketAggregate.EventSlug));
        AssertConverter<ExternalMarketId, string>(market, nameof(MarketAggregate.ExternalMarketId));
        AssertConverter<MarketSlug, string>(market, nameof(MarketAggregate.MarketSlug));
        AssertConverter<ConditionId, string>(market, nameof(MarketAggregate.ConditionId));
        market.GetIndexes()
            .Select(index => index.GetDatabaseName())
            .Should()
            .Contain([
                "ux_markets_external_event_id",
                "ux_markets_event_slug",
                "ux_markets_market_slug",
                "ux_markets_external_market_id",
                "ux_markets_condition_id"
            ]);
    }

    [Fact]
    public void Model_ShouldMapTokensThroughBackingFieldAndCascadeDelete()
    {
        var market = _model.FindEntityType(typeof(MarketAggregate));
        var token = _model.FindEntityType(typeof(MarketToken));

        token.Should().NotBeNull();
        token!.GetTableName().Should().Be("market_tokens");
        AssertConverter<MarketId, Guid>(token, nameof(MarketToken.MarketId));
        AssertConverter<TokenId, string>(token, nameof(MarketToken.ExternalTokenId));
        token.GetIndexes()
            .Select(index => index.GetDatabaseName())
            .Should()
            .Contain([
                "ux_market_tokens_external_token_id",
                "ux_market_tokens_market_id_external_token_id",
                "ux_market_tokens_market_id_outcome_index"
            ]);

        var navigation = market!.FindNavigation(nameof(MarketAggregate.Tokens));
        navigation.Should().NotBeNull();
        navigation!.GetPropertyAccessMode().Should().Be(PropertyAccessMode.Field);
        navigation.ForeignKey.DeleteBehavior.Should().Be(DeleteBehavior.Cascade);
    }

    [Fact]
    public void MarketListQuery_ShouldTranslateSlugOrderingForNpgsql()
    {
        using var context = CreateContext();

        var sql = context.Markets
            .AsNoTracking()
            .Include(market => market.Tokens)
            .OrderBy(market => market.MarketSlug)
            .ToQueryString();

        sql.Should().Contain("ORDER BY m.market_slug");
    }

    private static MarketsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MarketsDbContext>()
            .UseNpgsql("Host=localhost;Database=markets_model;Username=test;Password=test")
            .Options;

        return new MarketsDbContext(options);
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
