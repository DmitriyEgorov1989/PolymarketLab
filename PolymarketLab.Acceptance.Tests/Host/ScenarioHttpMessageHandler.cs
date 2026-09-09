namespace PolymarketLab.Acceptance.Tests.Host;

internal sealed class ScenarioHttpMessageHandler(AcceptanceScenario scenario)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(scenario.Respond(request));
}
