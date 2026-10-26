using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;

/// <summary>Исход из неизменяемого снимка сессии.</summary>
/// <param name="AssetId">Идентификатор токена.</param>
/// <param name="Outcome">Название исхода.</param>
/// <param name="OutcomeIndex">Позиция исхода.</param>
public sealed record ReplayAsset(string AssetId, string Outcome, int OutcomeIndex);

/// <summary>Раздельная внешняя идентичность события и дочернего рынка.</summary>
/// <param name="ExternalEventId">Внешнее событие.</param>
/// <param name="EventSlug">Slug события.</param>
/// <param name="ExternalMarketId">Внешний дочерний рынок.</param>
/// <param name="MarketSlug">Slug дочернего рынка.</param>
public sealed record ReplayMarketIdentity(string ExternalEventId, string EventSlug, string ExternalMarketId, string MarketSlug);

/// <summary>Все raw-заголовки, включая сообщения без событий стакана.</summary>
/// <param name="RawMessageId">Идентификатор строки архива.</param>
/// <param name="ConnectionEpoch">Положительная эпоха соединения.</param>
/// <param name="ReceivedAt">Исходное время полного получения.</param>
/// <param name="PayloadSha256">Отпечаток исходных bytes без раскрытия payload.</param>
/// <param name="Events">События стакана в архивном порядке; может быть пустым.</param>
public sealed record ReplayRawMessage(
    long RawMessageId, long ConnectionEpoch, DateTimeOffset ReceivedAt,
    string PayloadSha256, ImmutableArray<NormalizedOrderBookEvent> Events);

/// <summary>Версионируемая фиксация входа; hash нормализации не равен hash raw.</summary>
/// <param name="FormatVersion">Версия канонизации.</param>
/// <param name="RawFingerprint">Отпечаток всех исходных заголовков и payload hashes.</param>
/// <param name="ProjectionFingerprint">Отпечаток typed-событий и provenance.</param>
/// <param name="SessionFingerprint">Отпечаток identity, окна и исходов.</param>
public sealed record ReplayDatasetFingerprint(
    int FormatVersion, string RawFingerprint, string ProjectionFingerprint, string SessionFingerprint);

/// <summary>Проверенный вход одной двоичной сессии; не передаётся торговому сценарию.</summary>
public sealed class ExperimentDataset
{
    private ExperimentDataset(Guid sessionId, int version, string conditionId,
        DateTimeOffset startsAt, DateTimeOffset endsAt, ImmutableArray<ReplayAsset> assets,
        ImmutableArray<ReplayRawMessage> messages, ReplayMarketIdentity? identity)
    {
        SessionId = sessionId;
        ProjectionVersion = version;
        ConditionId = conditionId;
        EventStartsAt = startsAt;
        EventEndsAt = endsAt;
        Assets = assets;
        Messages = messages;
        Identity = identity;
        Manifest = new ReplayDatasetFingerprint(1,
            SimulationJson.Hash(messages.Select(m => new
                { m.RawMessageId, m.ConnectionEpoch, m.ReceivedAt, m.PayloadSha256 })),
            SimulationJson.Hash(messages.SelectMany(m => m.Events).Select(e =>
                SimulationJson.Serialize(e, e.GetType()))),
            SimulationJson.Hash(new { sessionId, version, conditionId, startsAt, endsAt, assets, identity }));
    }

    /// <summary>Точная сохранённая сессия.</summary>
    public Guid SessionId { get; }
    /// <summary>Зафиксированная версия проекции.</summary>
    public int ProjectionVersion { get; }
    /// <summary>Условие рынка.</summary>
    public string ConditionId { get; }
    /// <summary>Внешняя identity; null разрешён только для синтетического in-memory входа.</summary>
    public ReplayMarketIdentity? Identity { get; }
    /// <summary>Включительная граница торговли.</summary>
    public DateTimeOffset EventStartsAt { get; }
    /// <summary>Исключительная граница торговли, не момент расчёта.</summary>
    public DateTimeOffset EventEndsAt { get; }
    /// <summary>Два исхода в порядке снимка.</summary>
    public ImmutableArray<ReplayAsset> Assets { get; }
    /// <summary>Все исходные сообщения в порядке raw.id.</summary>
    public ImmutableArray<ReplayRawMessage> Messages { get; }
    /// <summary>Контрольные суммы входа.</summary>
    public ReplayDatasetFingerprint Manifest { get; }
    /// <summary>Авторитетный манифест #57; null только у синтетического входа.</summary>
    public ExperimentDatasetManifest? ArchiveManifest { get; private set; }

    /// <summary>Адаптирует материализованный снимок #57, не перечитывая архив и не используя сведения о будущем winner.</summary>
    public static Result<ExperimentDataset, Error> FromSnapshot(ExperimentDatasetSnapshot snapshot)
    {
        var blocking = snapshot.Suitability.Diagnostics.FirstOrDefault(d => d.BlocksReplay);
        if (blocking is not null) return new Error(blocking.Code, blocking.Message, ErrorType.Conflict);
        var session = snapshot.Manifest.Session;
        if (snapshot.RawMessages.IsDefault || session.ConditionId is null
            || session.EventStartsAt is null || session.EventEndsAt is null)
            return SimulationErrors.Invalid("dataset.headers", "A materialized snapshot with all raw headers is required.");
        var events = snapshot.OrderBookEvents.ToLookup(e => BookEventMetadata.Position(e).RawMessageId);
        var result = Create(session.SessionId, snapshot.Manifest.ProjectionVersion, session.ConditionId,
            session.EventStartsAt.Value, session.EventEndsAt.Value,
            session.Tokens.Select(t => new ReplayAsset(t.TokenId, t.Outcome, t.OutcomeIndex)),
            snapshot.RawMessages.Select(r => new ReplayRawMessage(r.RawMessageId, r.ConnectionEpoch,
                r.ReceivedAt, r.PayloadSha256, events[r.RawMessageId].ToImmutableArray())),
            new ReplayMarketIdentity(session.ExternalEventId!, session.EventSlug!, session.ExternalMarketId!, session.MarketSlug!));
        if (result.IsSuccess) result.Value.ArchiveManifest = snapshot.Manifest;
        return result;
    }

    /// <summary>Проверяет identity, порядок и provenance без изменения исходных коллекций.</summary>
    public static Result<ExperimentDataset, Error> Create(Guid sessionId, int version,
        string conditionId, DateTimeOffset startsAt, DateTimeOffset endsAt,
        IEnumerable<ReplayAsset> assets, IEnumerable<ReplayRawMessage> messages, ReplayMarketIdentity? identity = null)
    {
        var tokens = assets.OrderBy(a => a.OutcomeIndex).ToImmutableArray();
        var rows = messages.ToImmutableArray();
        if (sessionId == Guid.Empty || version <= 0 || string.IsNullOrWhiteSpace(conditionId)
            || startsAt == default || endsAt <= startsAt || tokens.Length != 2
            || tokens.Any(a => a.OutcomeIndex < 0 || string.IsNullOrWhiteSpace(a.AssetId) || string.IsNullOrWhiteSpace(a.Outcome))
            || tokens.Select(a => a.AssetId).Distinct(StringComparer.Ordinal).Count() != 2
            || tokens.Select(a => a.OutcomeIndex).Distinct().Count() != 2 || rows.IsEmpty
            || identity is not null && (string.IsNullOrWhiteSpace(identity.ExternalEventId)
                || string.IsNullOrWhiteSpace(identity.EventSlug) || string.IsNullOrWhiteSpace(identity.ExternalMarketId)
                || string.IsNullOrWhiteSpace(identity.MarketSlug)))
            return SimulationErrors.Invalid("dataset", "Session snapshot is incomplete or not binary.");

        long previousId = 0, previousEpoch = 0;
        foreach (var row in rows)
        {
            if (row.RawMessageId <= previousId || row.ConnectionEpoch <= 0
                || row.ConnectionEpoch < previousEpoch || row.ReceivedAt == default
                || row.PayloadSha256.Length != 64 || !row.PayloadSha256.All(Uri.IsHexDigit)
                || row.Events.IsDefault || row.ReceivedAt >= endsAt)
                return SimulationErrors.Invalid("dataset.order", "Raw archive order or provenance is invalid.");
            previousId = row.RawMessageId;
            previousEpoch = row.ConnectionEpoch;
            var previousItem = -1;
            foreach (var item in row.Events)
            {
                var records = BookEventMetadata.Records(item);
                var position = BookEventMetadata.Position(item);
                if (position.RawMessageId != row.RawMessageId || position.RawItemIndex <= previousItem
                    || records.Any(r => r.Epoch != row.ConnectionEpoch || r.ReceivedAt != row.ReceivedAt
                        || !tokens.Any(a => a.AssetId == r.AssetId))
                    || item is NormalizedOrderBookEvent.BookSnapshot snapshot
                        && snapshot.Record.MarketConditionId != conditionId)
                    return SimulationErrors.Invalid("dataset.event", "Typed event provenance disagrees with raw/session.");
                if (item is NormalizedOrderBookEvent.PriceChanges changes
                    && (changes.Records.Any(r => r.Position != position)
                        || changes.Records.Select(r => r.ItemIndex).Distinct().Count() != changes.Records.Count
                        || changes.Records.GroupBy(r => r.AssetId).Any(g => g.Select(r => r.SourceTimestamp).Distinct().Count() != 1)))
                    return SimulationErrors.Invalid("dataset.items", "Price change group is not atomic.");
                previousItem = position.RawItemIndex;
            }
        }
        var ownedRows = rows.Select(row => row with
        {
            Events = row.Events.Select(CopyEvent).ToImmutableArray()
        }).ToImmutableArray();
        return new ExperimentDataset(sessionId, version, conditionId,
            startsAt.ToUniversalTime(), endsAt.ToUniversalTime(), tokens, ownedRows, identity);
    }

    private static NormalizedOrderBookEvent CopyEvent(NormalizedOrderBookEvent item) => item switch
    {
        NormalizedOrderBookEvent.BookSnapshot snapshot => new NormalizedOrderBookEvent.BookSnapshot(
            new BookSnapshotRecord(snapshot.Record.Position.RawMessageId, snapshot.Record.Position.RawItemIndex,
                snapshot.Record.NormalizedEventId, snapshot.Record.AssetId, snapshot.Record.MarketConditionId,
                snapshot.Record.SourceTimestamp, snapshot.Record.Hash, snapshot.Record.TickSize,
                snapshot.Record.Bids, snapshot.Record.Asks, snapshot.Record.ConnectionEpoch, snapshot.Record.ReceivedAt)),
        NormalizedOrderBookEvent.PriceChanges changes => new NormalizedOrderBookEvent.PriceChanges(changes.Records),
        _ => item
    };
}

internal static class BookEventMetadata
{
    public static OrderBookEventPosition Position(NormalizedOrderBookEvent item) => item switch
    {
        NormalizedOrderBookEvent.BookSnapshot e => e.Record.Position,
        NormalizedOrderBookEvent.PriceChanges e => e.Records[0].Position,
        NormalizedOrderBookEvent.TickSizeChange e => e.Record.Position,
        NormalizedOrderBookEvent.BestBidAsk e => e.Record.Position,
        _ => throw new ArgumentOutOfRangeException(nameof(item))
    };

    public static IEnumerable<(string AssetId, long? Epoch, DateTimeOffset? ReceivedAt)> Records(
        NormalizedOrderBookEvent item) => item switch
    {
        NormalizedOrderBookEvent.BookSnapshot e => [(e.Record.AssetId, e.Record.ConnectionEpoch, e.Record.ReceivedAt)],
        NormalizedOrderBookEvent.PriceChanges e => e.Records.Select(r => (r.AssetId, r.ConnectionEpoch, r.ReceivedAt)),
        NormalizedOrderBookEvent.TickSizeChange e => [(e.Record.AssetId, e.Record.ConnectionEpoch, e.Record.ReceivedAt)],
        NormalizedOrderBookEvent.BestBidAsk e => [(e.Record.AssetId, e.Record.ConnectionEpoch, e.Record.ReceivedAt)],
        _ => throw new ArgumentOutOfRangeException(nameof(item))
    };
}
