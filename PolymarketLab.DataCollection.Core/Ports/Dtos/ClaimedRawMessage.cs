using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Исходное сообщение и поколение его захвата для нормализации.</summary>
/// <param name="Message">Захваченное сохранённое сообщение.</param>
/// <param name="ProjectionVersion">Версия нормализованной проекции.</param>
/// <param name="AttemptCount">Поколение захвата для защиты от устаревшего обработчика.</param>
public sealed record ClaimedRawMessage(
    RawMessageEnvelope Message,
    int ProjectionVersion,
    int AttemptCount);
