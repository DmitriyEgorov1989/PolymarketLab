namespace PolymarketLab.Acceptance.Tests.Host;

internal sealed class ScenarioHttpMessageHandler(IReadOnlyCollection<AcceptanceScenario> scenarios)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(scenarios.Single(scenario => scenario.CanRespond(request)).Respond(request));
}
