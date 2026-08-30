using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

internal sealed class ProjectionVersionProvider(IOptions<NormalizerOptions> options)
    : IProjectionVersionProvider
{
    public int ProjectionVersion { get; } = options.Value.ProjectionVersion;
}
