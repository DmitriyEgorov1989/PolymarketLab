using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Application.OrderBooks.Projection;
using PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;
using PolymarketLab.DataCollection.Core.Application.Simulation.Execution;
using PolymarketLab.DataCollection.Core.Application.Simulation.ExternalData;
using PolymarketLab.DataCollection.Core.Application.Simulation.Replay;
using PolymarketLab.DataCollection.Core.Application.Simulation.Reporting;
using PolymarketLab.DataCollection.Core.Application.Simulation.Rules;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.RunExperiment;

/// <summary>Последовательно исполняет один сценарий с виртуальным временем; внешнего I/O нет.</summary>
public sealed class ExperimentRunner(IOrderBookProjector projector)
{
    /// <summary>Создаёт новый счёт, replay и журнал; ошибки конфигурации возвращаются до торговли.</summary>
    public Result<ExperimentReport, Error> Run(ExperimentDataset dataset, ExperimentDefinition definition,
        CancellationToken cancellationToken = default)
    {
        try { return RunCore(dataset, definition, cancellationToken); }
        catch (OverflowException)
        {
            return SimulationErrors.Invalid("amount", "Experiment amounts or dates exceed supported decimal/UTC ranges.");
        }
    }

    private Result<ExperimentReport, Error> RunCore(ExperimentDataset dataset, ExperimentDefinition definition,
        CancellationToken cancellationToken)
    {
        var valid = Validate(dataset, definition);
        if (valid.IsFailure) return valid.Error;
        var journal = new SimulationJournal();
        var replay = new CausalReplayEngine(dataset, projector, journal);
        var execution = new ImmediateExecutionEngine(definition.InitialCash, dataset.Assets.Select(a => a.AssetId), journal);
        var times = new SortedSet<DateTimeOffset> { dataset.EventStartsAt, dataset.EventEndsAt, definition.EpisodeEndsAt };
        var origin = replay.Clock.GetUtcNow();
        foreach (var order in definition.Orders) times.Add(order.At);
        foreach (var point in definition.Btc)
        {
            times.Add(point.AvailableAt < origin ? origin : point.AvailableAt);
            if (definition.MaximumBtcAge is { } age && age <= DateTimeOffset.MaxValue - point.AvailableAt)
                times.Add(point.AvailableAt + age < origin ? origin : point.AvailableAt + age);
            if (definition.MaximumBtcAge is { } sourceAge && point.SourceAt is { } source && sourceAge <= DateTimeOffset.MaxValue - source)
                times.Add(source + sourceAge < origin ? origin : source + sourceAge);
        }
        foreach (var rules in definition.Rules)
            times.Add(Max(origin, Max(rules.AvailableAt, rules.EffectiveAt)));
        foreach (var evidence in definition.Settlement) times.Add(Max(origin, evidence.AvailableAt));
        times.RemoveWhere(t => t > definition.EpisodeEndsAt);
        var arrivals = new SortedDictionary<DateTimeOffset, List<long>>();
        BtcDataState? previousBtc = null;
        string? previousRule = null;
        var ended = false;
        var settlementStatus = "Unconfirmed";
        DateTimeOffset? confirmedAt = null;
        ReplayObservation? final = null;
        ReplayObservation? endObservation = null;
        TradingRulesProfile? endRules = null;
        var externalIssues = new HashSet<string>(StringComparer.Ordinal);

        while (times.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var at = times.Min;
            times.Remove(at);
            final = replay.AdvanceTo(at, cancellationToken).Value;
            if (at == dataset.EventStartsAt)
            {
                journal.Add(at, "TradingStarted", new { final.BooksReady, final.RawMessageId, final.ConnectionEpoch });
                if (!final.BooksReady) externalIssues.Add("InitialBooksUnavailable");
            }
            var rules = RulesAt(definition, at);
            var btc = BtcAvailabilityTimeline.Observe(definition.Btc, at, definition.MaximumBtcAge);
            if (btc != previousBtc)
            {
                journal.Add(at, "BtcState", btc);
                if (btc.Status != BtcDataStatus.Fresh) externalIssues.Add($"Btc{btc.Status}");
                previousBtc = btc;
            }
            if (rules?.Id != previousRule)
            {
                journal.Add(at, "RulesAvailable", new { RulesId = rules?.Id });
                previousRule = rules?.Id;
            }
            var resolution = ResolutionAt(dataset, definition, at);
            if (resolution.Status != settlementStatus)
            {
                settlementStatus = resolution.Status;
                if (settlementStatus == "Confirmed") confirmedAt = at;
                journal.Add(at, "SettlementState", new { Status = settlementStatus, resolution.Winner });
            }
            if (!ended && at >= dataset.EventEndsAt)
            {
                ended = true;
                execution.CancelAll(at);
                endObservation = final;
                endRules = rules;
                journal.Add(at, "TradingEnded", new { dataset.EventEndsAt });
            }
            // Доступные данные одного времени предшествуют ранее запланированным прибытиям.
            if (arrivals.Remove(at, out var due) && !ended)
                foreach (var sequence in due.Order())
                {
                    if (definition.RequireFreshBtc && btc.Status != BtcDataStatus.Fresh)
                        execution.Cancel(sequence, at, "BtcUnavailable");
                    else execution.Execute(sequence, final, rules);
                }
            foreach (var action in definition.Orders.Where(o => o.At == at).OrderBy(o => o.Order.Sequence))
            {
                journal.Add(at, "DecisionObservation", new { action.Order.Sequence, Observation = final, Btc = btc, Balance = execution.Balance });
                if (definition.RequireFreshBtc && btc.Status != BtcDataStatus.Fresh)
                {
                    journal.Add(at, "OrderRejected", new { action.Order.Sequence, Code = "simulation.btc_unavailable" });
                    continue;
                }
                if (execution.Submit(action.Order, final, rules).IsFailure) continue;
                if (action.Order.Latency == TimeSpan.Zero)
                    execution.Execute(action.Order.Sequence, final, rules);
                else
                {
                    var arrival = at + action.Order.Latency;
                    if (!arrivals.TryGetValue(arrival, out var list)) arrivals.Add(arrival, list = []);
                    list.Add(action.Order.Sequence);
                    if (arrival <= definition.EpisodeEndsAt) times.Add(arrival);
                }
            }
        }
        execution.CancelAll(definition.EpisodeEndsAt);
        var valuations = ValuePositions(execution, endObservation!, endRules);
        DateTimeOffset? appliedAt = null;
        var finalResolution = ResolutionAt(dataset, definition, definition.EpisodeEndsAt);
        if (finalResolution.Status != "Confirmed") externalIssues.Add($"Settlement{finalResolution.Status}");
        if (endRules is null) externalIssues.Add("UnknownTradingRules");
        if (!endObservation!.BooksReady) externalIssues.Add("IncompleteFinalBooks");
        if (finalResolution.Status == "Confirmed")
        {
            execution.Settle(finalResolution.Winner!, definition.SettlementPolicy!.WinnerPayout);
            appliedAt = definition.EpisodeEndsAt;
            journal.Add(appliedAt.Value, "SettlementApplied", new { finalResolution.Winner, definition.SettlementPolicy.WinnerPayout });
        }
        journal.Add(definition.EpisodeEndsAt, "EpisodeEnded", new { Balance = execution.Balance, Valuations = valuations, Settlement = finalResolution.Status });
        var entries = journal.Entries;
        var issues = entries.Where(e => e.Kind is "ClockAdjusted" or "ConnectionGap" or "MissingSnapshot" or "BookRejected")
            .Select(e => e.Kind).Concat(externalIssues).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        return new ExperimentReport(dataset.SessionId, dataset.ProjectionVersion, dataset.EventStartsAt,
            dataset.EventEndsAt, dataset.ArchiveManifest, dataset.Manifest, definition, SimulationJson.Hash(definition), SimulationJson.Hash(entries),
            execution.Balance, valuations, finalResolution.Status, confirmedAt, appliedAt,
            BtcAvailabilityTimeline.Observe(definition.Btc, definition.EpisodeEndsAt, definition.MaximumBtcAge), issues,
            ["Experimental quote-currency fees, cumulative order rounding; not historical Polymarket fees.",
             "FAK/IOC; historical market does not react to simulated actions; no queue or market-impact model.",
             "Consumed liquidity debt survives snapshots, level removal and connection changes.",
             "Raw/ledger equality and reconstructed books do not prove absence of unobservable external feed gaps.",
             "Equal-time data precedes arrivals and script decisions; zero-latency fills follow their decision.",
             "Valuation uses the book at trading end; payout is modeled at episode end, not real redemption."], entries);
    }

    private static TradingRulesProfile? RulesAt(ExperimentDefinition definition, DateTimeOffset at) =>
        definition.Rules.Where(r => r.AvailableAt <= at && r.EffectiveAt <= at)
            .OrderBy(r => r.EffectiveAt).ThenBy(r => r.AvailableAt).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();

    private static (string Status, string? Winner) ResolutionAt(ExperimentDataset dataset, ExperimentDefinition definition, DateTimeOffset at)
    {
        if (definition.SettlementPolicy is not { } policy) return ("Unconfirmed", null);
        var known = definition.Settlement.Where(e => e.AvailableAt <= at && e.ConfirmedAt <= at
            && policy.RequiredSources.Contains(e.Source, StringComparer.Ordinal)).ToArray();
        if (known.Any(e => e.SessionId != dataset.SessionId || e.ConditionId != dataset.ConditionId
            || !dataset.Assets.Any(a => a.AssetId == e.WinningAssetId))
            || known.Select(e => e.WinningAssetId).Distinct(StringComparer.Ordinal).Count() > 1)
            return ("Conflicting", null);
        return policy.RequiredSources.All(source => known.Any(e => e.Source == source))
            ? ("Confirmed", known[0].WinningAssetId) : ("Unconfirmed", null);
    }

    private static ImmutableArray<PositionValuation> ValuePositions(ImmediateExecutionEngine execution,
        ReplayObservation observation, TradingRulesProfile? rules) => execution.Balance.Positions.Select(position =>
    {
        var book = observation.Books.Single(b => b.AssetId == position.AssetId);
        decimal remaining = position.Quantity, raw = 0;
        if (observation.BooksReady && rules is not null)
            foreach (var level in book.Bids)
            {
                var quantity = Math.Min(remaining, Quantization.Floor(execution.Liquidity.Available(
                    position.AssetId, TradeSide.Sell, level.Price, level.Size), rules.QuantityQuantum));
                raw += quantity * level.Price;
                remaining -= quantity;
                if (remaining == 0) break;
            }
        var quoted = position.Quantity - remaining;
        var net = rules is null ? 0 : Quantization.Floor(raw, rules.MoneyQuantum) - Quantization.Ceiling(rules.Fee(raw), rules.MoneyQuantum);
        if (net < 0) return new PositionValuation(position.AssetId, 0, position.Quantity, 0, null);
        return new PositionValuation(position.AssetId, quoted, remaining, net,
            quoted == 0 ? null : net - position.CostBasis * (quoted / position.Quantity));
    }).ToImmutableArray();

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second) => first > second ? first : second;

    private static UnitResult<Error> Validate(ExperimentDataset dataset, ExperimentDefinition d)
    {
        if (d.Version != 1 || d.InitialCash < 0 || d.EpisodeEndsAt < dataset.EventEndsAt
            || d.Orders.IsDefault || d.Rules.IsDefault || d.Btc.IsDefault || d.Settlement.IsDefault)
            return SimulationErrors.Invalid("definition", "Invalid experiment version, arrays, initial cash or episode horizon.");
        var origin = dataset.Messages[0].ReceivedAt < dataset.EventStartsAt ? dataset.Messages[0].ReceivedAt : dataset.EventStartsAt;
        if (d.Orders.Select(o => o.Order.Sequence).Distinct().Count() != d.Orders.Length
            || d.Orders.Any(o => o.At < origin || o.At > d.EpisodeEndsAt || o.Order.Latency < TimeSpan.Zero
                || o.At.UtcTicks > DateTimeOffset.MaxValue.UtcTicks - o.Order.Latency.Ticks))
            return SimulationErrors.Invalid("script", "Script has duplicate identifiers, invalid times or latency.");
        if (d.Rules.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != d.Rules.Length)
            return SimulationErrors.Invalid("rules", "Rule version identifiers must be unique.");
        if ((!d.Btc.IsEmpty && d.MaximumBtcAge is null) || d.MaximumBtcAge <= TimeSpan.Zero
            || d.Btc.Select(o => o.Sequence).Distinct().Count() != d.Btc.Length
            || d.Btc.Any(o => o.Sequence <= 0 || string.IsNullOrWhiteSpace(o.Source) || string.IsNullOrWhiteSpace(o.Pair)
                || o.AvailableAt == default || (o.IsGap ? o.Price is not null : o.Price is null or <= 0))
            || d.Btc.Select(o => (o.Source, o.Pair)).Distinct().Count() > 1)
            return SimulationErrors.Invalid("btc", "BTC source, availability, price or maximum age is invalid.");
        if (d.Settlement.Any(e => string.IsNullOrWhiteSpace(e.Source) || string.IsNullOrWhiteSpace(e.Reference)
                || e.ConfirmedAt < dataset.EventEndsAt || e.AvailableAt < e.ConfirmedAt)
            || d.SettlementPolicy is { } p && (p.RequiredSources.IsDefaultOrEmpty || p.WinnerPayout is <= 0 or > 1
                || p.RequiredSources.Any(string.IsNullOrWhiteSpace)
                || p.RequiredSources.Distinct(StringComparer.Ordinal).Count() != p.RequiredSources.Length))
            return SimulationErrors.Invalid("settlement", "Settlement evidence or explicit payout policy is invalid.");
        return UnitResult.Success<Error>();
    }
}
