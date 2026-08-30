namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Предоставляет активную версию нормализации для нового session snapshot.</summary>
public interface IProjectionVersionProvider
{
    /// <summary>Получает положительную версию создаваемых normalized projections.</summary>
    int ProjectionVersion { get; }
}
