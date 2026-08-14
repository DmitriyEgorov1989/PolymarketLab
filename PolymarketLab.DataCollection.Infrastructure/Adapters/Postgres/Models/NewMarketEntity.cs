using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

internal sealed class NewMarketEntity
{
    private NewMarketEntity()
    {
    }

    public NewMarketEntity(long eventId, NewMarketRecord record)
    {
        EventId = eventId;
        ExternalMarketId = record.ExternalId;
        Question = record.Question;
        Slug = record.Slug;
        Description = record.Description;
        Active = record.Active;
        SportsMarketType = record.SportsMarketType;
        Line = record.Line;
        GameStartTime = record.GameStartTime;
        OrderPriceMinTickSize = record.OrderPriceMinTickSize;
        GroupItemTitle = record.GroupItemTitle;
        TakerBaseFee = record.TakerBaseFee;
        FeesEnabled = record.FeesEnabled;
        EventMessageId = record.EventMessage.Id;
        EventMessageTicker = record.EventMessage.Ticker;
        EventMessageSlug = record.EventMessage.Slug;
        EventMessageTitle = record.EventMessage.Title;
        EventMessageDescription = record.EventMessage.Description;
        FeeScheduleExponent = record.FeeSchedule.Exponent;
        FeeScheduleRate = record.FeeSchedule.Rate;
        FeeScheduleRebateRate = record.FeeSchedule.RebateRate;
        FeeScheduleTakerOnly = record.FeeSchedule.TakerOnly;
    }

    public long EventId { get; private set; }
    public string ExternalMarketId { get; private set; } = string.Empty;
    public string Question { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public bool Active { get; private set; }
    public string SportsMarketType { get; private set; } = string.Empty;
    public decimal? Line { get; private set; }
    public string GameStartTime { get; private set; } = string.Empty;
    public decimal OrderPriceMinTickSize { get; private set; }
    public string GroupItemTitle { get; private set; } = string.Empty;
    public decimal TakerBaseFee { get; private set; }
    public bool FeesEnabled { get; private set; }
    public string EventMessageId { get; private set; } = string.Empty;
    public string EventMessageTicker { get; private set; } = string.Empty;
    public string EventMessageSlug { get; private set; } = string.Empty;
    public string EventMessageTitle { get; private set; } = string.Empty;
    public string EventMessageDescription { get; private set; } = string.Empty;
    public decimal FeeScheduleExponent { get; private set; }
    public decimal FeeScheduleRate { get; private set; }
    public decimal FeeScheduleRebateRate { get; private set; }
    public bool FeeScheduleTakerOnly { get; private set; }
}
