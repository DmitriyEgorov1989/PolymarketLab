using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Предоставляет снимок политики хранения набора данных после ошибки.</summary>
public interface IFailedDatasetRetentionPolicyProvider
{
    /// <summary>Возвращает неизменяемый снимок политики.</summary>
    FailedDatasetRetentionPolicy Policy { get; }
}
