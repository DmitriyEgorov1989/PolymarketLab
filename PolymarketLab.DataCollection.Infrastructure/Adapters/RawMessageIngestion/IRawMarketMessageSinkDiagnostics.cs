namespace PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;

/// <summary>Предоставляет безопасные локальные показатели bounded raw-message queue.</summary>
internal interface IRawMarketMessageSinkDiagnostics
{
    /// <summary>Текущее приблизительное количество сообщений в очереди.</summary>
    int QueueDepth { get; }

    /// <summary>Максимальная вместимость очереди в сообщениях.</summary>
    int Capacity { get; }
}
