using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.ResolutionConsensus;

/// <summary>Согласует устойчивые terminal-наблюдения resolution активной collector session.</summary>
public interface IResolutionConsensusCoordinator
{
    /// <summary>Обрабатывает временные границы, новые наблюдения и consensus текущей exclusive session.</summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Успех либо ожидаемая ошибка orchestration или persistence.</returns>
    Task<UnitResult<Error>> TickAsync(CancellationToken cancellationToken);
}
