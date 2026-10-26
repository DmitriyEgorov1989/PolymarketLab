using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection.Models;
using PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;
using PolymarketLab.DataCollection.Core.Application.Simulation.Reporting;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.Replay;

/// <summary>Неизменяемая копия книги; null шага и позиции означает отсутствие знания.</summary>
/// <param name="AssetId">Токен исхода.</param>
/// <param name="HasSnapshot">Получен ли baseline текущей epoch.</param>
/// <param name="Status">Степень доверия проектора.</param>
/// <param name="TickSize">Шаг либо null.</param>
/// <param name="Position">Последняя применённая позиция либо null.</param>
/// <param name="Bids">Уровни покупки по убыванию цены.</param>
/// <param name="Asks">Уровни продажи по возрастанию цены.</param>
/// <param name="Issue">Проблема либо null.</param>
public sealed record ReplayBookView(string AssetId, bool HasSnapshot, OrderBookSyncStatus Status,
    decimal? TickSize, OrderBookEventPosition? Position, ImmutableArray<OrderBookLevel> Bids,
    ImmutableArray<OrderBookLevel> Asks, OrderBookIntegrityIssue? Issue);

/// <summary>Только уже доступное состояние; сведения о будущих событиях отсутствуют.</summary>
/// <param name="At">Текущее виртуальное время.</param>
/// <param name="RawMessageId">Последний просмотренный raw.id; ноль до первого сообщения.</param>
/// <param name="ConnectionEpoch">Известная epoch; ноль до первого сообщения.</param>
/// <param name="WithinTradingWindow">Находится ли время внутри полуоткрытого окна.</param>
/// <param name="BooksReady">Пригодны ли оба baseline и их состояния.</param>
/// <param name="Books">Копии обоих исходов в порядке снимка сессии.</param>
public sealed record ReplayObservation(DateTimeOffset At, long RawMessageId, long ConnectionEpoch,
    bool WithinTradingWindow, bool BooksReady, ImmutableArray<ReplayBookView> Books);

/// <summary>Воспроизводит архив по доступности, не используя terminal source-order planner или REST.</summary>
public sealed class CausalReplayEngine
{
    private readonly ExperimentDataset _dataset;
    private readonly IOrderBookProjector _projector;
    private readonly SimulationJournal _journal;
    private readonly ImmutableArray<DateTimeOffset> _availability;
    private readonly Dictionary<string, OrderBookState> _states = new(StringComparer.Ordinal);
    private readonly HashSet<string> _initialized = new(StringComparer.Ordinal);
    private int _next;
    private long _epoch;
    private long _rawId;

    /// <summary>Создаёт изолированное воспроизведение; все зависимости обязательны, null недопустим.</summary>
    public CausalReplayEngine(ExperimentDataset dataset, IOrderBookProjector projector, SimulationJournal journal)
    {
        _dataset = dataset;
        _projector = projector;
        _journal = journal;
        var builder = ImmutableArray.CreateBuilder<DateTimeOffset>(dataset.Messages.Length);
        var previous = DateTimeOffset.MinValue;
        foreach (var message in dataset.Messages)
        {
            previous = message.ReceivedAt > previous ? message.ReceivedAt : previous;
            builder.Add(previous);
        }
        _availability = builder.MoveToImmutable();
        Clock = new ReplayTimeProvider(_availability[0] < dataset.EventStartsAt
            ? _availability[0] : dataset.EventStartsAt);
        ResetBooks();
    }

    internal ReplayTimeProvider Clock { get; }

    /// <summary>Продвигает префикс до времени включительно; обратный запрос возвращает ошибку.</summary>
    public Result<ReplayObservation, Error> AdvanceTo(DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (at < Clock.GetUtcNow())
            return SimulationErrors.Invalid("replay.time", "An observation cannot move replay backwards.");
        while (_next < _dataset.Messages.Length && _availability[_next] <= at)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = _dataset.Messages[_next];
            var availableAt = _availability[_next++];
            Clock.AdvanceTo(availableAt);
            _rawId = message.RawMessageId;
            if (availableAt > message.ReceivedAt)
                _journal.Add(availableAt, "ClockAdjusted", new
                    { message.RawMessageId, message.ReceivedAt, CorrectionTicks = (availableAt - message.ReceivedAt).Ticks });
            if (_epoch != message.ConnectionEpoch)
            {
                if (_epoch != 0)
                    _journal.Add(availableAt, "ConnectionGap", new { PreviousEpoch = _epoch, message.ConnectionEpoch });
                _epoch = message.ConnectionEpoch;
                ResetBooks();
            }
            _journal.Add(availableAt, "RawObserved", new { message.RawMessageId, message.ConnectionEpoch });
            foreach (var item in message.Events)
            {
                // Наблюдение публикуется только после обработки обоих активов события.
                foreach (var asset in _dataset.Assets)
                {
                    if (!BookEventMetadata.Records(item).Any(r => r.AssetId == asset.AssetId))
                        continue;
                    var state = _states[asset.AssetId];
                    if (item is not NormalizedOrderBookEvent.BookSnapshot && !_initialized.Contains(asset.AssetId))
                    {
                        _journal.Add(availableAt, "MissingSnapshot", new { message.RawMessageId, asset.AssetId, _epoch });
                        continue;
                    }
                    var result = _projector.Apply(state, item);
                    if (item is NormalizedOrderBookEvent.BookSnapshot
                        && result.Outcome == OrderBookProjectionOutcome.Applied)
                        _initialized.Add(asset.AssetId);
                    _journal.Add(availableAt, result.IntegrityIssue is null ? "BookApplied" : "BookRejected",
                        new { Position = BookEventMetadata.Position(item), asset.AssetId, result.Outcome, result.IntegrityIssue });
                }
            }
        }
        Clock.AdvanceTo(at);
        return Observe();
    }

    private void ResetBooks()
    {
        _states.Clear();
        _initialized.Clear();
        foreach (var asset in _dataset.Assets)
            _states.Add(asset.AssetId, new OrderBookState(asset.AssetId, Clock));
    }

    private ReplayObservation Observe()
    {
        var views = _dataset.Assets.Select(asset =>
        {
            var state = _states[asset.AssetId];
            return new ReplayBookView(asset.AssetId, _initialized.Contains(asset.AssetId), state.Status,
                state.TickSize, state.EventPosition,
                state.Bids.Values.OrderByDescending(l => l.Price).ToImmutableArray(),
                state.Asks.Values.OrderBy(l => l.Price).ToImmutableArray(), state.IntegrityIssue);
        }).ToImmutableArray();
        return new ReplayObservation(Clock.GetUtcNow(), _rawId, _epoch,
            Clock.GetUtcNow() >= _dataset.EventStartsAt && Clock.GetUtcNow() < _dataset.EventEndsAt,
            views.All(v => v.HasSnapshot && v.Status == OrderBookSyncStatus.Synchronized && v.Issue is null), views);
    }
}
