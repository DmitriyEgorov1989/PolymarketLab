using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Simulation;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.Simulation;

public sealed class SimulationRequestFileTests
{
    private static JsonObject Input() => JsonNode.Parse("""
        {
          "version": 1,
          "sessionId": "33a17de5-2b29-4de2-929c-2d1c0ee46588",
          "projectionVersion": 1,
          "initialCash": "100.00",
          "episodeEndsAt": "2026-10-16T09:25:00Z",
          "orders": [], "rules": [], "btc": [], "settlement": []
        }
        """)!.AsObject();

    [Fact]
    public async Task Read_ExplicitEmptyInputs_ShouldKeepUnknownFeeBtcAndOutcome()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Input().ToJsonString()));
        var request = (await SimulationRequestFile.ReadAsync(stream, default)).Value;
        request.ExpectedManifest.Should().BeNull();
        request.Definition.Rules.Should().BeEmpty();
        request.Definition.Btc.Should().BeEmpty();
        request.Definition.SettlementPolicy.Should().BeNull();
    }

    [Fact]
    public async Task Read_BtcWithoutAvailability_ShouldNotSubstituteSourceTime()
    {
        var input = Input();
        input["btc"] = JsonNode.Parse("""[{"sequence":1,"source":"synthetic","pair":"BTC/USD","sourceAt":"2026-10-16T09:20:00Z","price":"100000"}]""");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input.ToJsonString()));
        (await SimulationRequestFile.ReadAsync(stream, default)).Error.Code.Should().Be("simulation.file.btc");
    }

    [Fact]
    public async Task Read_AbsentFee_ShouldNotTreatUnknownAsZero()
    {
        var input = Input();
        input["rules"] = JsonNode.Parse("""[{"id":"test","source":"synthetic","availableAt":"2026-10-16T09:19:00Z","effectiveAt":"2026-10-16T09:19:00Z","minimumQuantity":1,"quantityQuantum":1,"moneyQuantum":"0.000001"}]""");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input.ToJsonString()));
        (await SimulationRequestFile.ReadAsync(stream, default)).Error.Code.Should().Be("simulation.file.rules");
    }

    [Fact]
    public async Task Read_UnknownProperty_ShouldReturnContractError()
    {
        var input = Input();
        input["futurePrice"] = 100000;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input.ToJsonString()));
        (await SimulationRequestFile.ReadAsync(stream, default)).Error.Code.Should().Be("simulation.file.json");
    }
}
