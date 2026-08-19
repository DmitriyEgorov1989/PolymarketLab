namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

/// <summary>Тип нарушения целостности текущего состояния стакана.</summary>
public enum OrderBookIntegrityIssueType
{
    /// <summary>Лучшая цена покупки не совпала с контрольным значением.</summary>
    BestBidMismatch = 1,

    /// <summary>Лучшая цена продажи не совпала с контрольным значением.</summary>
    BestAskMismatch = 2,

    /// <summary>Вычисленный спред не совпал с контрольным значением.</summary>
    SpreadMismatch = 3,

    /// <summary>Локальный шаг цены не совпал с предыдущим значением события.</summary>
    TickSizeMismatch = 4,

    /// <summary>Лучшая цена покупки оказалась выше лучшей цены продажи.</summary>
    CrossedBook = 5,

    /// <summary>Событие или снимок относится к другому активу.</summary>
    UnexpectedAsset = 6,

    /// <summary>Нарушен порядок времени или архивной позиции событий.</summary>
    EventOrderViolation = 7,

    /// <summary>Обнаружен пропуск последовательности событий.</summary>
    GapDetected = 8,

    /// <summary>Внешние hash состояния не совпали при диагностической проверке.</summary>
    SnapshotHashMismatch = 9
}
