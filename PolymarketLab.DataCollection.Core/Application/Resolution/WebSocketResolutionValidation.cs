using PolymarketLab.DataCollection.Core.Domain.Models.Resolution;

namespace PolymarketLab.DataCollection.Core.Application.Resolution;

/// <summary>Результат строгой проверки WebSocket resolution observation.</summary>
public enum WebSocketResolutionObservationStatus
{
    /// <summary>Наблюдение не подтверждает resolution, но не является terminal conflict.</summary>
    Rejected = 0,

    /// <summary>Наблюдение подтверждает единственного победителя текущей session.</summary>
    Terminal = 1
}

/// <summary>Проверенное безопасное WebSocket resolution observation.</summary>
/// <param name="Status">Результат проверки.</param>
/// <param name="Winner">Проверенный победитель либо <see langword="null" /> для отклонённого наблюдения.</param>
/// <param name="RejectionCode">Безопасная причина отклонения либо <see langword="null" /> для terminal observation.</param>
public sealed record WebSocketResolutionValidation(
    WebSocketResolutionObservationStatus Status,
    ResolutionWinner? Winner,
    string? RejectionCode)
{
    /// <summary>Создаёт terminal observation с проверенным победителем.</summary>
    public static WebSocketResolutionValidation Terminal(ResolutionWinner winner) =>
        new(WebSocketResolutionObservationStatus.Terminal, winner, null);

    /// <summary>Создаёт non-confirming observation с безопасной причиной.</summary>
    public static WebSocketResolutionValidation Rejected(string rejectionCode) =>
        new(WebSocketResolutionObservationStatus.Rejected, null, rejectionCode);
}
