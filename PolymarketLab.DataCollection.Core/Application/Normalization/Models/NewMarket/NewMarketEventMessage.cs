namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Подтверждённые поля сообщения о создании внешнего события.</summary>
public sealed record NewMarketEventMessage
{
    /// <summary>Создаёт подтверждённое описание внешнего события.</summary>
    /// <param name="id">Внешний идентификатор события.</param>
    /// <param name="ticker">Тикер события.</param>
    /// <param name="slug">Slug события.</param>
    /// <param name="title">Заголовок события.</param>
    /// <param name="description">Описание события.</param>
    public NewMarketEventMessage(
        string id,
        string ticker,
        string slug,
        string title,
        string description)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(ticker)) throw new ArgumentException("Ticker is required.", nameof(ticker));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Slug is required.", nameof(slug));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Description is required.", nameof(description));

        Id = id;
        Ticker = ticker;
        Slug = slug;
        Title = title;
        Description = description;
    }

    /// <summary>Внешний идентификатор события.</summary>
    public string Id { get; }

    /// <summary>Тикер события.</summary>
    public string Ticker { get; }

    /// <summary>Slug события.</summary>
    public string Slug { get; }

    /// <summary>Заголовок события.</summary>
    public string Title { get; }

    /// <summary>Описание события.</summary>
    public string Description { get; }
}
