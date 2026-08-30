using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;

/// <summary>Токен исхода, зафиксированный в неизменяемом snapshot сессии.</summary>
public sealed class CollectorSessionToken
{
    private CollectorSessionToken()
    {
    }

    internal CollectorSessionToken(
        CollectorSessionId sessionId,
        CollectorSessionTokenDefinition definition)
    {
        SessionId = sessionId;
        TokenId = definition.TokenId;
        Outcome = definition.Outcome;
        OutcomeIndex = definition.OutcomeIndex;
    }

    /// <summary>Идентификатор владеющей сессии.</summary>
    public CollectorSessionId SessionId { get; private set; } = null!;

    /// <summary>Внешний идентификатор токена.</summary>
    public TokenId TokenId { get; private set; } = null!;

    /// <summary>Название исхода в момент создания сессии.</summary>
    public string Outcome { get; private set; } = null!;

    /// <summary>Позиция исхода во внешнем рынке.</summary>
    public int OutcomeIndex { get; private set; }
}
