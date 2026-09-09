using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorOrderBookIntegrity;

/// <summary>Проверяет последовательную целостность сохранённой проекции стаканов collector session.</summary>
public interface ICollectorOrderBookIntegrityCoordinator
{
    /// <summary>Воспроизводит typed-события во временных состояниях и возвращает первую проблему.</summary>
    /// <param name="sessionId">Идентификатор проверяемой collector session.</param>
    /// <param name="projectionVersion">Положительная snapshot-версия нормализации.</param>
    /// <param name="tokenIds">Непустой immutable набор token IDs session snapshot.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Успех при отсутствии проблем или безопасная ошибка целостности.</returns>
    Task<UnitResult<Error>> EvaluateAsync(
        CollectorSessionId sessionId,
        int projectionVersion,
        IReadOnlyCollection<TokenId> tokenIds,
        CancellationToken cancellationToken);
}
