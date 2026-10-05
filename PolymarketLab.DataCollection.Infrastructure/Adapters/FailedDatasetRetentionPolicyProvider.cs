using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters;

internal sealed class FailedDatasetRetentionPolicyProvider(
    IOptions<FailedDatasetRetentionOptions> options)
    : IFailedDatasetRetentionPolicyProvider
{
    public FailedDatasetRetentionPolicy Policy { get; } = new(
        options.Value.Enabled,
        options.Value.RetentionPeriod,
        options.Value.MaximumRetainedSessions);
}
