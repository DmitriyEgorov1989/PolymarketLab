using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorNormalizationSuitability;

/// <summary>Доказывает пригодность normalized dataset snapshot-версии session.</summary>
public interface ICollectorNormalizationSuitabilityCoordinator
{
    /// <summary>
    /// Ожидает незавершённую нормализацию до deadline, инвалидирует недоказанный
    /// dataset и завершает session только при полном доказательстве пригодности.
    /// </summary>
    /// <param name="sessionId">Идентификатор session в <c>Stopping/AwaitingNormalization</c>.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Успех ожидания/завершения либо исходная ожидаемая ошибка.</returns>
    Task<UnitResult<Error>> EvaluateAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);
}
