namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

/// <summary>Безопасный token/outcome одного resolution observation.</summary>
internal sealed class ResolutionObservationOutcomeEntity
{
    private ResolutionObservationOutcomeEntity()
    {
    }

    /// <summary>Создаёт проверенный исход source observation.</summary>
    public ResolutionObservationOutcomeEntity(
        int outcomeIndex,
        string tokenId,
        string? outcome,
        decimal? price,
        bool isWinner)
    {
        OutcomeIndex = outcomeIndex;
        TokenId = tokenId;
        Outcome = outcome;
        Price = price;
        IsWinner = isWinner;
    }

    /// <summary>Идентификатор родительского observation.</summary>
    public long ObservationId { get; private set; }
    /// <summary>Позиция исхода в проверенном source.</summary>
    public int OutcomeIndex { get; private set; }
    /// <summary>Внешний token id.</summary>
    public string TokenId { get; private set; } = string.Empty;
    /// <summary>Название исхода либо <see langword="null" />, если source его не предоставил.</summary>
    public string? Outcome { get; private set; }
    /// <summary>Цена от <c>0.00</c> до <c>1.00</c> либо <see langword="null" /> для WebSocket.</summary>
    public decimal? Price { get; private set; }
    /// <summary>Признак проверенного победителя.</summary>
    public bool IsWinner { get; private set; }
}
