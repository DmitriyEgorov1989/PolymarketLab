using System.Collections.Concurrent;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorScheduling;

/// <summary>Запоминает успешно пройденные boundary checks текущего процесса.</summary>
public sealed class CollectorBoundaryCheckRegistry
{
    private readonly ConcurrentDictionary<CollectorSessionId, byte> _readinessChecks = new();

    /// <summary>Проверяет, подтверждена ли readiness boundary для session в текущем процессе.</summary>
    /// <param name="sessionId">Идентификатор session.</param>
    /// <returns><see langword="true"/>, если boundary уже подтверждена.</returns>
    public bool IsReadinessVerified(CollectorSessionId sessionId) =>
        _readinessChecks.ContainsKey(sessionId);

    /// <summary>Фиксирует успешную readiness boundary check текущего процесса.</summary>
    /// <param name="sessionId">Идентификатор session.</param>
    public void MarkReadinessVerified(CollectorSessionId sessionId) =>
        _readinessChecks.TryAdd(sessionId, 0);
}
