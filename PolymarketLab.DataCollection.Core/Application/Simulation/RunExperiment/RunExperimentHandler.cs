using CSharpFunctionalExtensions;
using MediatR;
using PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;
using PolymarketLab.DataCollection.Core.Application.Simulation.Reporting;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using static PolymarketLab.SharedKernel.Errors.Error;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.RunExperiment;

/// <summary>Внутренняя команда без публичного HTTP endpoint, с обязательной фиксацией входа.</summary>
/// <param name="SessionId">Точная сессия.</param>
/// <param name="ProjectionVersion">Версия проекции.</param>
/// <param name="ExpectedManifest">Ранее зафиксированные отпечатки; null недопустим.</param>
/// <param name="Definition">Явные настройки и сценарий; null недопустим.</param>
public sealed record RunExperimentCommand(Guid SessionId, int ProjectionVersion,
    ExperimentDatasetManifest ExpectedManifest, ExperimentDefinition Definition)
    : IRequest<Result<ExperimentReport, ErrorList>>;

/// <summary>Читает архив через read-only порт и запускает изолированный эксперимент.</summary>
public sealed class RunExperimentHandler(IExperimentDatasetReader reader, ExperimentRunner runner)
    : IRequestHandler<RunExperimentCommand, Result<ExperimentReport, ErrorList>>
{
    /// <inheritdoc />
    public async Task<Result<ExperimentReport, ErrorList>> Handle(RunExperimentCommand request, CancellationToken cancellationToken)
    {
        var id = CollectorSessionId.Create(request.SessionId);
        if (id.IsFailure) return Result.Failure<ExperimentReport, ErrorList>(id.Error);
        if (request.ExpectedManifest is null || request.Definition is null
            || request.ExpectedManifest.FormatVersion != 1
            || request.ExpectedManifest.ProjectionVersion != request.ProjectionVersion
            || request.ExpectedManifest.Session?.SessionId != request.SessionId
            || !IsHash(request.ExpectedManifest.RawFingerprint)
            || !IsHash(request.ExpectedManifest.ManifestFingerprint))
            return Result.Failure<ExperimentReport, ErrorList>(SimulationErrors.Invalid("request", "Pinned manifest and experiment definition are required."));
        var snapshot = await reader.ReadAsync(ExperimentRequestBuilder.Create(id.Value,
            request.ProjectionVersion, request.Definition, request.ExpectedManifest), cancellationToken);
        if (snapshot.IsFailure) return Result.Failure<ExperimentReport, ErrorList>(snapshot.Error);
        var dataset = ExperimentDataset.FromSnapshot(snapshot.Value);
        if (dataset.IsFailure) return Result.Failure<ExperimentReport, ErrorList>(dataset.Error);
        var result = runner.Run(dataset.Value, request.Definition, cancellationToken);
        return result.IsSuccess ? result.Value : Result.Failure<ExperimentReport, ErrorList>(result.Error);
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
