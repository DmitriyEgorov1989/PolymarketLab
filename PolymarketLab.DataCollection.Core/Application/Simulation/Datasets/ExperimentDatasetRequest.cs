using System.Collections.Immutable;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;

/// <summary>Точный выбор архивного входа без поиска последней сессии рынка.</summary>
/// <param name="SessionId">Идентификатор выбранной сессии.</param>
/// <param name="ProjectionVersion">Версия проекции, совпадающая со снимком сессии.</param>
public sealed record ExperimentDatasetRequest(CollectorSessionId SessionId, int ProjectionVersion)
{
    /// <summary>Настройки и ссылки; отсутствие ссылки сохраняет неизвестность источника.</summary>
    public ExperimentDatasetInputs Inputs { get; init; } = new();

    /// <summary>Ожидаемый SHA-256 исходного архива; <see langword="null" /> отключает сравнение.</summary>
    public string? ExpectedRawFingerprint { get; init; }

    /// <summary>Ожидаемый SHA-256 полного манифеста; <see langword="null" /> отключает сравнение.</summary>
    public string? ExpectedManifestFingerprint { get; init; }
}

/// <summary>Объявленные входы эксперимента; ссылка сама по себе не доказывает пригодность данных.</summary>
public sealed record ExperimentDatasetInputs
{
    /// <summary>Строковые настройки с точной семантикой значений, упорядочиваемые по имени.</summary>
    public ImmutableSortedDictionary<string, string> Settings { get; init; } =
        ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);

    /// <summary>Закреплённый набор BTC; <see langword="null" /> означает отсутствие набора.</summary>
    public ExperimentDatasetReference? Btc { get; init; }

    /// <summary>Профиль правил комиссии; <see langword="null" /> означает отсутствие профиля.</summary>
    public ExperimentDatasetReference? FeeProfile { get; init; }

    /// <summary>Профиль минимального размера; <see langword="null" /> означает отсутствие профиля.</summary>
    public ExperimentDatasetReference? MinimumOrderSizeProfile { get; init; }
}

/// <summary>Закреплённая ссылка без загрузки внешнего источника и без учётных данных.</summary>
/// <param name="Name">Устойчивое логическое имя набора или профиля.</param>
/// <param name="Fingerprint">SHA-256 его содержимого; проверка содержимого выполняется потребителем.</param>
public sealed record ExperimentDatasetReference(string Name, string Fingerprint);
