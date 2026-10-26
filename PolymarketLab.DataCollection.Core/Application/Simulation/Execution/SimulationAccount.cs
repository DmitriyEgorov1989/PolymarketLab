using System.Collections.Immutable;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.Execution;

/// <summary>Состояние позиции с денежной себестоимостью.</summary>
/// <param name="AssetId">Токен исхода.</param>
/// <param name="Quantity">Полное количество, включая резерв.</param>
/// <param name="Reserved">Зарезервированные для продаж токены.</param>
/// <param name="CostBasis">Денежная себестоимость с комиссиями покупок.</param>
public sealed record PositionBalance(string AssetId, decimal Quantity, decimal Reserved, decimal CostBasis);

/// <summary>Неизменяемый финансовый снимок.</summary>
/// <param name="FreeCash">Свободные деньги.</param>
/// <param name="ReservedCash">Деньги незавершённых заявок.</param>
/// <param name="FeesPaid">Сумма денежных комиссий.</param>
/// <param name="RealizedPnl">Реализованный результат после комиссий и списания себестоимости.</param>
/// <param name="Positions">Позиции в порядке исходов.</param>
public sealed record AccountBalance(decimal FreeCash, decimal ReservedCash, decimal FeesPaid,
    decimal RealizedPnl, ImmutableArray<PositionBalance> Positions);

internal sealed class SimulationAccount(decimal initialCash, IEnumerable<string> assets)
{
    private readonly ImmutableArray<string> _assetOrder = assets.ToImmutableArray();
    private readonly Dictionary<string, PositionBalance> _positions = assets.ToDictionary(
        a => a, a => new PositionBalance(a, 0, 0, 0), StringComparer.Ordinal);
    public decimal FreeCash { get; private set; } = initialCash;
    public decimal ReservedCash { get; private set; }
    public decimal FeesPaid { get; private set; }
    public decimal RealizedPnl { get; private set; }

    public PositionBalance Position(string asset) => _positions[asset];
    public AccountBalance Snapshot() => new(FreeCash, ReservedCash, FeesPaid, RealizedPnl,
        _assetOrder.Select(a => _positions[a]).ToImmutableArray());

    public void ReserveCash(decimal cash) { FreeCash -= cash; ReservedCash += cash; }
    public void ReleaseCash(decimal cash) { FreeCash += cash; ReservedCash -= cash; }
    public void ReserveTokens(string asset, decimal quantity) =>
        _positions[asset] = Position(asset) with { Reserved = Position(asset).Reserved + quantity };
    public void ReleaseTokens(string asset, decimal quantity) =>
        _positions[asset] = Position(asset) with { Reserved = Position(asset).Reserved - quantity };

    public void Buy(string asset, decimal quantity, decimal debit, decimal fee)
    {
        var position = Position(asset);
        var updated = position with { Quantity = position.Quantity + quantity, CostBasis = position.CostBasis + debit };
        var fees = FeesPaid + fee;
        ReservedCash -= debit;
        FeesPaid = fees;
        _positions[asset] = updated;
    }

    public void Sell(string asset, decimal quantity, decimal credit, decimal fee)
    {
        var position = Position(asset);
        var cost = quantity == position.Quantity ? position.CostBasis : position.CostBasis * (quantity / position.Quantity);
        var cash = FreeCash + credit;
        var fees = FeesPaid + fee;
        var realized = RealizedPnl + credit - cost;
        var updated = position with
        {
            Quantity = position.Quantity - quantity,
            Reserved = position.Reserved - quantity,
            CostBasis = position.CostBasis - cost
        };
        FreeCash = cash;
        FeesPaid = fees;
        RealizedPnl = realized;
        _positions[asset] = updated;
    }

    public void Settle(string winner, decimal payout)
    {
        foreach (var asset in _assetOrder)
        {
            var position = _positions[asset];
            var cash = position.AssetId == winner ? position.Quantity * payout : 0;
            FreeCash += cash;
            RealizedPnl += cash - position.CostBasis;
            _positions[position.AssetId] = position with { Quantity = 0, Reserved = 0, CostBasis = 0 };
        }
    }
}
