using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;
using PolymarketLab.DataCollection.Core.Application.Simulation.Execution;
using PolymarketLab.DataCollection.Core.Application.Simulation.ExternalData;
using PolymarketLab.DataCollection.Core.Application.Simulation.Rules;
using PolymarketLab.DataCollection.Core.Application.Simulation.RunExperiment;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Simulation;

/// <summary>Разобранный файл запуска; null manifest разрешает только чтение контрольных сумм.</summary>
/// <param name="SessionId">Выбранная сохранённая сессия.</param>
/// <param name="ProjectionVersion">Версия проекции.</param>
/// <param name="ExpectedManifest">Фиксация входа либо null для первого ознакомления.</param>
/// <param name="Definition">Сценарий с явно экспериментальными правилами и независимыми внешними данными.</param>
public sealed record SimulationFileRequest(Guid SessionId, int ProjectionVersion,
    ExperimentDatasetManifest? ExpectedManifest, ExperimentDefinition Definition);

/// <summary>Антикоррупционный импорт пользовательского JSON, не транспортного payload Polymarket.</summary>
public static class SimulationRequestFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>Читает файл; отсутствие availableAt не заменяется временем источника.</summary>
    public static async Task<Result<SimulationFileRequest, Error>> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        RequestDto? dto;
        try { dto = await JsonSerializer.DeserializeAsync<RequestDto>(stream, Options, cancellationToken); }
        catch (JsonException) { return Failure("json", "Simulation input does not match the JSON file contract."); }
        if (dto is null || dto.Version != 1 || dto.SessionId == Guid.Empty || dto.ProjectionVersion <= 0
            || dto.InitialCash is null || dto.EpisodeEndsAt is null || dto.Orders is null || dto.Rules is null
            || dto.Btc is null || dto.Settlement is null)
            return Failure("fields", "Version, session, money, horizon and all input arrays must be explicit.");
        var rules = ImmutableArray.CreateBuilder<TradingRulesProfile>();
        foreach (var item in dto.Rules)
        {
            if (item is null || item.AvailableAt is null || item.EffectiveAt is null || item.MinimumQuantity is null
                || item.QuantityQuantum is null || item.MoneyQuantum is null || item.FeeBps is null)
                return Failure("rules", "Unknown fee or size cannot be substituted with zero.");
            var result = TradingRulesProfile.CreateExperimental(item.Id ?? "", item.Source ?? "", item.AvailableAt.Value,
                item.EffectiveAt.Value, item.MinimumQuantity.Value, item.QuantityQuantum.Value, item.MoneyQuantum.Value, item.FeeBps.Value);
            if (result.IsFailure) return result.Error;
            rules.Add(result.Value);
        }
        var orders = ImmutableArray.CreateBuilder<ScheduledOrder>();
        foreach (var item in dto.Orders)
        {
            if (item is null || item.At is null || item.Latency is null || item.Quantity is null || item.LimitPrice is null
                || item.AssetId is null || !Enum.TryParse<TradeSide>(item.Side, true, out var side) || !Enum.IsDefined(side))
                return Failure("order", "Order time, latency, side, asset, quantity and limit must be explicit.");
            orders.Add(new ScheduledOrder(item.At.Value,
                new SimulatedOrder(item.Sequence, item.AssetId, side, item.Quantity.Value, item.LimitPrice.Value, item.Latency.Value)));
        }
        var btc = ImmutableArray.CreateBuilder<BtcObservation>();
        foreach (var item in dto.Btc)
        {
            if (item is null || item.AvailableAt is null || string.IsNullOrWhiteSpace(item.Source) || string.IsNullOrWhiteSpace(item.Pair))
                return Failure("btc", "BTC source, pair and availability are required, including for gaps.");
            btc.Add(new BtcObservation(item.Sequence, item.Source, item.Pair, item.SourceAt, item.AvailableAt.Value, item.Price, item.IsGap));
        }
        var settlement = ImmutableArray.CreateBuilder<SettlementEvidence>();
        foreach (var item in dto.Settlement)
        {
            if (item is null || item.AvailableAt is null || item.ConfirmedAt is null || item.Source is null
                || item.Reference is null || item.ConditionId is null || item.WinningAssetId is null)
                return Failure("settlement", "Outcome provenance and availability must be explicit.");
            settlement.Add(new SettlementEvidence(item.Source, item.Reference, item.SessionId, item.ConditionId,
                item.WinningAssetId, item.ConfirmedAt.Value, item.AvailableAt.Value));
        }
        SettlementPolicy? policy = null;
        if (dto.SettlementPolicy is { } p)
        {
            if (p.RequiredSources is null || p.WinnerPayout is null)
                return Failure("settlement_policy", "Required sources and payout must be explicit.");
            policy = new SettlementPolicy(p.RequiredSources.ToImmutableArray(), p.WinnerPayout.Value);
        }
        return new SimulationFileRequest(dto.SessionId, dto.ProjectionVersion, dto.ExpectedManifest,
            new ExperimentDefinition(dto.Version, dto.InitialCash.Value, dto.EpisodeEndsAt.Value,
                orders.ToImmutable(), rules.ToImmutable(), btc.ToImmutable(), dto.MaximumBtcAge,
                dto.RequireFreshBtc, settlement.ToImmutable(), policy));
    }

    private static Error Failure(string code, string message) => new($"simulation.file.{code}", message, ErrorType.Conflict);

    private sealed record RequestDto(int Version, Guid SessionId, int ProjectionVersion,
        ExperimentDatasetManifest? ExpectedManifest, decimal? InitialCash, DateTimeOffset? EpisodeEndsAt,
        OrderDto?[]? Orders, RulesDto?[]? Rules, BtcDto?[]? Btc, TimeSpan? MaximumBtcAge,
        bool RequireFreshBtc, EvidenceDto?[]? Settlement, PolicyDto? SettlementPolicy);
    private sealed record OrderDto(long Sequence, DateTimeOffset? At, string? AssetId, string? Side,
        decimal? Quantity, decimal? LimitPrice, TimeSpan? Latency);
    private sealed record RulesDto(string? Id, string? Source, DateTimeOffset? AvailableAt, DateTimeOffset? EffectiveAt,
        decimal? MinimumQuantity, decimal? QuantityQuantum, decimal? MoneyQuantum, decimal? FeeBps);
    private sealed record BtcDto(long Sequence, string? Source, string? Pair, DateTimeOffset? SourceAt,
        DateTimeOffset? AvailableAt, decimal? Price, bool IsGap);
    private sealed record EvidenceDto(string? Source, string? Reference, Guid SessionId, string? ConditionId,
        string? WinningAssetId, DateTimeOffset? ConfirmedAt, DateTimeOffset? AvailableAt);
    private sealed record PolicyDto(string[]? RequiredSources, decimal? WinnerPayout);
}
