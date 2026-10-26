using MediatR;
using System.Text.Json;
using PolymarketLab.DataCollection.Core.Application.Simulation;
using PolymarketLab.DataCollection.Core.Application.Simulation.RunExperiment;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Simulation;
using PolymarketLab.SharedKernel.DomainModels.Ids;

/// <summary>Локальный режим чтения архива без старта HTTP, collector и фоновых служб.</summary>
internal static class SimulationConsole
{
    /// <summary>Читает manifest либо выполняет закреплённый сценарий; результат выводится в stdout.</summary>
    public static async Task<int> RunAsync(IServiceProvider services, string path, CancellationToken cancellationToken)
    {
        FileStream file;
        try { file = File.OpenRead(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(SimulationJson.Serialize(new { Code = "simulation.file.open", error.Message }));
            return 1;
        }
        await using var input = file;
        var parsed = await SimulationRequestFile.ReadAsync(input, cancellationToken);
        if (parsed.IsFailure)
        {
            Console.Error.WriteLine(SimulationJson.Serialize(new { parsed.Error.Code, parsed.Error.Message }));
            return 1;
        }
        var request = parsed.Value;
        var id = CollectorSessionId.Create(request.SessionId);
        if (id.IsFailure) return 1;
        await using var scope = services.CreateAsyncScope();
        if (request.ExpectedManifest is null)
        {
            var dataset = await scope.ServiceProvider.GetRequiredService<IExperimentDatasetReader>()
                .ReadAsync(ExperimentRequestBuilder.Create(id.Value, request.ProjectionVersion, request.Definition), cancellationToken);
            if (dataset.IsFailure)
            {
                Console.Error.WriteLine(SimulationJson.Serialize(new { dataset.Error.Code, dataset.Error.Message }));
                return 1;
            }
            using var manifestOutput = JsonDocument.Parse(SimulationJson.Serialize(new
            {
                Mode = "ManifestOnly", Session = dataset.Value.Manifest.Session,
                ExpectedManifest = dataset.Value.Manifest, dataset.Value.Suitability
            }));
            Console.WriteLine(JsonSerializer.Serialize(manifestOutput.RootElement,
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
            new RunExperimentCommand(request.SessionId, request.ProjectionVersion, request.ExpectedManifest, request.Definition), cancellationToken);
        if (result.IsFailure)
        {
            Console.Error.WriteLine(SimulationJson.Serialize(result.Error));
            return 1;
        }
        Console.WriteLine(SimulationJson.Serialize(result.Value));
        Console.Error.WriteLine(SimulationJson.Serialize(new
        {
            Mode = "Completed", result.Value.SessionId, result.Value.DefinitionFingerprint,
            result.Value.JournalFingerprint, JournalEntries = result.Value.Journal.Length,
            result.Value.Balance, result.Value.SettlementStatus, result.Value.DataIssues
        }));
        return 0;
    }
}
