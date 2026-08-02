using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal sealed record CollectorRuntimeShutdownEntry(
    CollectorSessionId SessionId,
    Lazy<CollectorRuntimeEntry> EntryHolder);
