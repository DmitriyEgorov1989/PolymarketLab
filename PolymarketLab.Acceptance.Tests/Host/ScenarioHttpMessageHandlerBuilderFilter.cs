using Microsoft.Extensions.Http;

namespace PolymarketLab.Acceptance.Tests.Host;

internal sealed class ScenarioHttpMessageHandlerBuilderFilter(params AcceptanceScenario[] scenarios)
    : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
        builder =>
        {
            next(builder);
            builder.PrimaryHandler = new ScenarioHttpMessageHandler(scenarios);
        };
}
