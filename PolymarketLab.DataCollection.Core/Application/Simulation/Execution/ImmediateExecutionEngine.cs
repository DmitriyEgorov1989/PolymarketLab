using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Application.Simulation.Replay;
using PolymarketLab.DataCollection.Core.Application.Simulation.Reporting;
using PolymarketLab.DataCollection.Core.Application.Simulation.Rules;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.Execution;

/// <summary>Немедленная заявка с лимитом; неисполненный остаток отменяется.</summary>
/// <param name="Sequence">Уникальный положительный номер действия сценария.</param>
/// <param name="AssetId">Токен исхода.</param>
/// <param name="Side">Покупка либо продажа.</param>
/// <param name="Quantity">Количество токенов.</param>
/// <param name="LimitPrice">Предельная цена.</param>
/// <param name="Latency">Неотрицательная виртуальная задержка.</param>
public sealed record SimulatedOrder(long Sequence, string AssetId, TradeSide Side,
    decimal Quantity, decimal LimitPrice, TimeSpan Latency);

/// <summary>Одно исполнение; комиссия и gross округлены накопительно по заявке.</summary>
/// <param name="OrderSequence">Номер действия.</param>
/// <param name="FillIndex">Порядок исполнения.</param>
/// <param name="AssetId">Токен исхода.</param>
/// <param name="Side">Сторона.</param>
/// <param name="Price">Цена уровня.</param>
/// <param name="Quantity">Исполненное количество.</param>
/// <param name="Gross">Округлённая часть стоимости/выручки.</param>
/// <param name="Fee">Денежная комиссия.</param>
public sealed record SimulatedFill(long OrderSequence, int FillIndex, string AssetId,
    TradeSide Side, decimal Price, decimal Quantity, decimal Gross, decimal Fee);

/// <summary>Результат FAK/IOC без ожидающего остатка.</summary>
/// <param name="Fills">Исполнения в порядке цен.</param>
/// <param name="CancelledQuantity">Неисполненный отменённый остаток.</param>
public sealed record OrderExecutionResult(ImmutableArray<SimulatedFill> Fills, decimal CancelledQuantity);

/// <summary>Долг потребления сохраняется на весь эпизод, включая snapshots и reconnect.</summary>
public sealed class ConsumedLiquidityOverlay
{
    private readonly Dictionary<(string Asset, TradeSide Side, decimal Price), decimal> _consumed = [];

    /// <summary>Возвращает доступный исторический объём без неявного восстановления.</summary>
    public decimal Available(string asset, TradeSide side, decimal price, decimal historicalSize) =>
        Math.Max(0, historicalSize - _consumed.GetValueOrDefault((asset, side, price)));

    internal void Consume(string asset, TradeSide side, decimal price, decimal quantity) =>
        _consumed[(asset, side, price)] = _consumed.GetValueOrDefault((asset, side, price)) + quantity;
}

/// <summary>Изолированные FAK/IOC исполнения и резервы; не изменяет историческую книгу.</summary>
public sealed class ImmediateExecutionEngine
{
    private sealed record Reservation(SimulatedOrder Order, decimal Cash, DateTimeOffset ArrivesAt);
    private readonly Dictionary<long, Reservation> _pending = [];
    private readonly HashSet<long> _seen = [];
    private readonly SimulationJournal _journal;
    private readonly SimulationAccount _account;
    private readonly ConsumedLiquidityOverlay _liquidity = new();

    /// <summary>Создаёт новый счёт без начальных токенов и независимый overlay ликвидности.</summary>
    public ImmediateExecutionEngine(decimal initialCash, IEnumerable<string> assets, SimulationJournal journal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCash);
        _account = new SimulationAccount(initialCash, assets);
        _journal = journal;
    }

    /// <summary>Копия свободных и зарезервированных денег, позиций и результата.</summary>
    public AccountBalance Balance => _account.Snapshot();
    internal ConsumedLiquidityOverlay Liquidity => _liquidity;

    /// <summary>Проверяет заявку и резервирует средства; null профиля означает неизвестные правила.</summary>
    public UnitResult<Error> Submit(SimulatedOrder order, ReplayObservation observation, TradingRulesProfile? rules)
    {
        if (!_seen.Add(order.Sequence))
            return Reject(observation.At, order.Sequence, "order.duplicate", "Order sequence was already used.");
        var validation = Validate(order, observation, rules);
        if (validation.IsFailure)
            return Reject(observation.At, order.Sequence, validation.Error);
        decimal reserve;
        try { reserve = order.Side == TradeSide.Buy ? rules!.BuyReserve(order.Quantity, order.LimitPrice) : 0; }
        catch (OverflowException) { return Reject(observation.At, order.Sequence, "order.amount", "Order amount overflows decimal."); }
        var position = _account.Position(order.AssetId);
        if (reserve > _account.FreeCash || order.Side == TradeSide.Sell && order.Quantity > position.Quantity - position.Reserved)
            return Reject(observation.At, order.Sequence, "order.funds", "Insufficient free cash or tokens.");
        if (order.Side == TradeSide.Buy) _account.ReserveCash(reserve);
        else _account.ReserveTokens(order.AssetId, order.Quantity);
        _pending.Add(order.Sequence, new Reservation(order, reserve, observation.At + order.Latency));
        _journal.Add(observation.At, "OrderReserved", new { Order = order, ReservedCash = reserve, Rules = rules!.Id });
        return UnitResult.Success<Error>();
    }

    /// <summary>Исполняет доступную часть один раз; при отказе и успехе резерв освобождается.</summary>
    public Result<OrderExecutionResult, Error> Execute(long sequence, ReplayObservation observation, TradingRulesProfile? rules)
    {
        if (!_pending.TryGetValue(sequence, out var reservation))
            return SimulationErrors.Invalid("order.missing", "Order is not pending.");
        if (observation.At < reservation.ArrivesAt)
            return SimulationErrors.Invalid("order.not_due", "Order latency has not elapsed; reservation remains active.");
        _pending.Remove(sequence);
        var order = reservation.Order;
        var remainingCash = reservation.Cash;
        var remainingTokens = order.Quantity;
        try
        {
            var validation = Validate(order, observation, rules);
            if (validation.IsFailure)
                return Reject(observation.At, sequence, validation.Error).Error;
            if (order.Side == TradeSide.Buy && rules!.BuyReserve(order.Quantity, order.LimitPrice) > reservation.Cash)
                return Reject(observation.At, sequence, "order.rules_changed", "Updated rules exceed the reserved budget.").Error;
            var book = observation.Books.Single(b => b.AssetId == order.AssetId);
            var levels = order.Side == TradeSide.Buy ? book.Asks : book.Bids;
            var fills = ImmutableArray.CreateBuilder<SimulatedFill>();
            decimal remaining = order.Quantity, rawGross = 0, previousGross = 0, previousFee = 0;
            foreach (var level in levels)
            {
                if (order.Side == TradeSide.Buy ? level.Price > order.LimitPrice : level.Price < order.LimitPrice)
                    break;
                var available = Quantization.Floor(_liquidity.Available(order.AssetId, order.Side, level.Price, level.Size), rules!.QuantityQuantum);
                var quantity = Math.Min(remaining, available);
                if (quantity <= 0) continue;
                rawGross += level.Price * quantity;
                var gross = order.Side == TradeSide.Buy
                    ? Quantization.Ceiling(rawGross, rules.MoneyQuantum)
                    : Quantization.Floor(rawGross, rules.MoneyQuantum);
                var fee = Quantization.Ceiling(rules.Fee(rawGross), rules.MoneyQuantum);
                fills.Add(new SimulatedFill(sequence, fills.Count, order.AssetId, order.Side, level.Price,
                    quantity, gross - previousGross, fee - previousFee));
                previousGross = gross; previousFee = fee;
                remaining -= quantity;
                if (remaining == 0) break;
            }
            var quantityFilled = order.Quantity - remaining;
            if (order.Side == TradeSide.Sell && previousGross < previousFee)
                return Reject(observation.At, sequence, "order.rounding", "Rounded proceeds cannot cover the fee.").Error;
            // План рассчитан целиком до изменения счёта и overlay.
            if (order.Side == TradeSide.Buy)
            {
                var debit = previousGross + previousFee;
                _account.Buy(order.AssetId, quantityFilled, debit, previousFee);
                remainingCash -= debit;
            }
            else if (quantityFilled > 0)
            {
                _account.Sell(order.AssetId, quantityFilled, previousGross - previousFee, previousFee);
                remainingTokens -= quantityFilled;
            }
            foreach (var fill in fills)
            {
                _liquidity.Consume(order.AssetId, order.Side, fill.Price, fill.Quantity);
                _journal.Add(observation.At, "Fill", fill);
            }
            _journal.Add(observation.At, "RemainderCancelled", new { sequence, Quantity = remaining });
            return new OrderExecutionResult(fills.ToImmutable(), remaining);
        }
        catch (OverflowException)
        {
            return Reject(observation.At, sequence, "order.amount", "Order calculation overflows decimal.").Error;
        }
        finally
        {
            if (order.Side == TradeSide.Buy) _account.ReleaseCash(remainingCash);
            else _account.ReleaseTokens(order.AssetId, remainingTokens);
            _journal.Add(observation.At, "ReservationReleased", new { sequence });
        }
    }

    internal void CancelAll(DateTimeOffset at)
    {
        foreach (var sequence in _pending.Keys.Order().ToArray()) Cancel(sequence, at, "TradingEnded");
    }

    internal void Cancel(long sequence, DateTimeOffset at, string reason)
    {
        if (!_pending.Remove(sequence, out var reservation)) return;
        if (reservation.Order.Side == TradeSide.Buy) _account.ReleaseCash(reservation.Cash);
        else _account.ReleaseTokens(reservation.Order.AssetId, reservation.Order.Quantity);
        _journal.Add(at, "OrderCancelled", new { sequence, Reason = reason });
    }

    internal void Settle(string winner, decimal payout) => _account.Settle(winner, payout);

    private static UnitResult<Error> Validate(SimulatedOrder order, ReplayObservation observation, TradingRulesProfile? rules)
    {
        if (!observation.WithinTradingWindow || !observation.BooksReady)
            return SimulationErrors.Invalid("order.book", "Trading window or synchronized baseline is unavailable.");
        if (rules is null || rules.AvailableAt > observation.At || rules.EffectiveAt > observation.At)
            return SimulationErrors.Invalid("order.rules_unknown", "Trading rules are not yet available.");
        var book = observation.Books.SingleOrDefault(b => b.AssetId == order.AssetId);
        if (book?.TickSize is null)
            return SimulationErrors.Invalid("order.tick_unknown", "Asset or price tick is unknown.");
        if (order.Sequence <= 0 || !Enum.IsDefined(order.Side) || order.Latency < TimeSpan.Zero
            || observation.At.UtcTicks > DateTimeOffset.MaxValue.UtcTicks - order.Latency.Ticks
            || order.Quantity < rules.MinimumQuantity || order.Quantity % rules.QuantityQuantum != 0
            || order.LimitPrice is <= 0 or >= 1 || order.LimitPrice % book.TickSize.Value != 0)
            return SimulationErrors.Invalid("order.validation", "Order price, quantity, side or latency is invalid.");
        return UnitResult.Success<Error>();
    }

    private UnitResult<Error> Reject(DateTimeOffset at, long sequence, string code, string message) =>
        Reject(at, sequence, SimulationErrors.Invalid(code, message));

    private UnitResult<Error> Reject(DateTimeOffset at, long sequence, Error error)
    {
        _journal.Add(at, "OrderRejected", new { sequence, error.Code, error.Message });
        return error;
    }
}
