namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Суммарное время фаз одного прохода нормализатора.</summary>
/// <param name="Claim">Время захвата исходных сообщений из хранилища.</param>
/// <param name="Build">Время декодирования и построения результатов нормализации.</param>
/// <param name="Write">Время сохранения результатов и завершения захватов.</param>
public sealed record NormalizationPhaseDurations(
    TimeSpan Claim,
    TimeSpan Build,
    TimeSpan Write);
