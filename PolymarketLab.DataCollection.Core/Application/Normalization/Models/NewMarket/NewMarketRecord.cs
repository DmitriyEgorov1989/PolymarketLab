namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Подтверждённые поля события <c>new_market</c>.</summary>
public sealed record NewMarketRecord : NormalizedRecord
{
    /// <summary>Создаёт запись подтверждённых данных нового рынка.</summary>
    /// <param name="externalId">Внешний идентификатор рынка.</param>
    /// <param name="question">Вопрос рынка.</param>
    /// <param name="slug">Slug рынка.</param>
    /// <param name="description">Описание рынка.</param>
    /// <param name="active">Признак активности рынка.</param>
    /// <param name="sportsMarketType">Тип спортивного рынка, включая пустую строку.</param>
    /// <param name="line">Спортивная линия или <see langword="null" /> для пустой строки.</param>
    /// <param name="gameStartTime">Внешнее значение времени начала, включая пустую строку.</param>
    /// <param name="orderPriceMinTickSize">Минимальный шаг цены заявки.</param>
    /// <param name="groupItemTitle">Заголовок элемента группы, включая пустую строку.</param>
    /// <param name="takerBaseFee">Базовая комиссия тейкера.</param>
    /// <param name="feesEnabled">Признак включённых комиссий.</param>
    /// <param name="eventMessage">Подтверждённые данные внешнего события.</param>
    /// <param name="feeSchedule">Подтверждённое расписание комиссий.</param>
    public NewMarketRecord(
        string externalId,
        string question,
        string slug,
        string description,
        bool active,
        string sportsMarketType,
        decimal? line,
        string gameStartTime,
        decimal orderPriceMinTickSize,
        string groupItemTitle,
        decimal takerBaseFee,
        bool feesEnabled,
        NewMarketEventMessage eventMessage,
        NewMarketFeeSchedule feeSchedule)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            throw new ArgumentException("External id is required.", nameof(externalId));
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question is required.", nameof(question));
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug is required.", nameof(slug));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.", nameof(description));

        ExternalId = externalId;
        Question = question;
        Slug = slug;
        Description = description;
        Active = active;
        SportsMarketType = sportsMarketType ?? throw new ArgumentNullException(nameof(sportsMarketType));
        Line = line;
        GameStartTime = gameStartTime ?? throw new ArgumentNullException(nameof(gameStartTime));
        OrderPriceMinTickSize = orderPriceMinTickSize;
        GroupItemTitle = groupItemTitle ?? throw new ArgumentNullException(nameof(groupItemTitle));
        TakerBaseFee = takerBaseFee;
        FeesEnabled = feesEnabled;
        EventMessage = eventMessage ?? throw new ArgumentNullException(nameof(eventMessage));
        FeeSchedule = feeSchedule ?? throw new ArgumentNullException(nameof(feeSchedule));
    }

    /// <summary>Внешний идентификатор рынка.</summary>
    public string ExternalId { get; }

    /// <summary>Вопрос рынка.</summary>
    public string Question { get; }

    /// <summary>Slug рынка.</summary>
    public string Slug { get; }

    /// <summary>Описание рынка.</summary>
    public string Description { get; }

    /// <summary>Признак активности рынка.</summary>
    public bool Active { get; }

    /// <summary>Тип спортивного рынка; пустая строка сохраняется.</summary>
    public string SportsMarketType { get; }

    /// <summary>Спортивная линия или <see langword="null" /> для пустого внешнего значения.</summary>
    public decimal? Line { get; }

    /// <summary>Внешнее значение времени начала; пустая строка сохраняется.</summary>
    public string GameStartTime { get; }

    /// <summary>Минимальный шаг цены заявки.</summary>
    public decimal OrderPriceMinTickSize { get; }

    /// <summary>Заголовок элемента группы; пустая строка сохраняется.</summary>
    public string GroupItemTitle { get; }

    /// <summary>Базовая комиссия тейкера.</summary>
    public decimal TakerBaseFee { get; }

    /// <summary>Признак включённых комиссий.</summary>
    public bool FeesEnabled { get; }

    /// <summary>Подтверждённые данные внешнего события.</summary>
    public NewMarketEventMessage EventMessage { get; }

    /// <summary>Подтверждённое расписание комиссий.</summary>
    public NewMarketFeeSchedule FeeSchedule { get; }
}
