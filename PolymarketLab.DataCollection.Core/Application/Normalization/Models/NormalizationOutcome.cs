namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Ожидаемый результат нормализации одного логического события.</summary>
public enum NormalizationOutcome
{
    /// <summary>Событие успешно нормализовано.</summary>
    Processed = 1,

    /// <summary>Тип события не поддерживается.</summary>
    Unsupported = 2,

    /// <summary>Событие имеет известный тип, но содержит недопустимые данные.</summary>
    Invalid = 3
}
