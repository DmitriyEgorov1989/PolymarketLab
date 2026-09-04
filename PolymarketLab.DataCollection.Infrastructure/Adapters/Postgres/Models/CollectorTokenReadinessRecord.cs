using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

/// <summary>Хранит compact durable-наблюдение готовности токена одной connection epoch.</summary>
internal sealed class CollectorTokenReadinessRecord
{
    private CollectorTokenReadinessRecord()
    {
    }

    /// <summary>Создаёт persistence-запись из наблюдения готовности.</summary>
    public CollectorTokenReadinessRecord(CollectorTokenReadiness readiness)
    {
        SessionId = readiness.SessionId;
        ConnectionEpoch = readiness.ConnectionEpoch;
        TokenId = readiness.TokenId;
        InitialBookEnqueuedAt = readiness.InitialBookEnqueuedAt;
    }

    /// <summary>Идентификатор сессии.</summary>
    public CollectorSessionId SessionId { get; private set; } = null!;

    /// <summary>Connection epoch наблюдения.</summary>
    public long ConnectionEpoch { get; private set; }

    /// <summary>Внешний идентификатор токена.</summary>
    public TokenId TokenId { get; private set; } = null!;

    /// <summary>UTC-момент успешной постановки initial book.</summary>
    public DateTimeOffset InitialBookEnqueuedAt { get; private set; }

    /// <summary>Преобразует persistence-запись в DTO порта.</summary>
    public CollectorTokenReadiness ToReadiness() => new(
        SessionId,
        ConnectionEpoch,
        TokenId,
        InitialBookEnqueuedAt);
}
