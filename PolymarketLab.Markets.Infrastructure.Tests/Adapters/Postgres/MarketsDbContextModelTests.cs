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
        AssertConverter<ExternalMarketId, string>(market, nameof(MarketAggregate.ExternalId));
        AssertConverter<MarketSlug, string>(market, nameof(MarketAggregate.Slug));
        AssertConverter<ConditionId, string>(market, nameof(MarketAggregate.ConditionId));
        market.GetIndexes()
            .Select(index => index.GetDatabaseName())
            .Should()
            .Contain([
                "ux_markets_slug",
                "ux_markets_external_id",
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
            .OrderBy(market => market.Slug)
            .ToQueryString();

        sql.Should().Contain("ORDER BY m.slug");
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
