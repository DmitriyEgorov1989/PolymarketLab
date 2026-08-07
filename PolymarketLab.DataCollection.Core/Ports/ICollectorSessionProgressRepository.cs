using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports;

public interface ICollectorSessionProgressRepository
{
    Task<CollectorSessionProgress> GetAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);

    Task CheckpointAsync(
        CollectorSessionProgressCheckpoint checkpoint,
        CancellationToken cancellationToken);
}
