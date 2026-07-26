using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

internal interface ICollectorWorkerFactory
{
    ICollectorWorker Create(CollectorRuntimeStartRequest request);
}
