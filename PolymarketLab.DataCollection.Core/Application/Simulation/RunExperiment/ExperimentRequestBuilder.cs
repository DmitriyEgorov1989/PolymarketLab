using System.Collections.Immutable;
using PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.RunExperiment;

/// <summary>Связывает настройки #56 с единым паспортом входа #57.</summary>
public static class ExperimentRequestBuilder
{
    /// <summary>Закрепляет все настройки и объявленные внешние входы; null expected разрешает первое чтение.</summary>
    public static ExperimentDatasetRequest Create(CollectorSessionId sessionId, int version,
        ExperimentDefinition definition, ExperimentDatasetManifest? expected = null) => new(sessionId, version)
    {
        Inputs = new ExperimentDatasetInputs
        {
            Settings = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal)
                .Add("simulation.definition.sha256", SimulationJson.Hash(definition)),
            Btc = definition.Btc.IsEmpty ? null : new("simulation-file:btc", SimulationJson.Hash(definition.Btc)),
            FeeProfile = definition.Rules.IsEmpty ? null : new("experimental:quote-fees", SimulationJson.Hash(definition.Rules)),
            MinimumOrderSizeProfile = definition.Rules.IsEmpty ? null : new("experimental:order-size", SimulationJson.Hash(definition.Rules))
        },
        ExpectedRawFingerprint = expected?.RawFingerprint,
        ExpectedManifestFingerprint = expected?.ManifestFingerprint
    };
}
