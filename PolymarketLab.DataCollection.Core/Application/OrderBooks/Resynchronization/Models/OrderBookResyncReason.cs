namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization.Models;

/// <summary>Причина восстановления локального стакана.</summary>
public enum OrderBookResyncReason
{
    /// <summary>Проверка явно запрошена оператором или вызывающим кодом.</summary>
    Manual = 1,

    /// <summary>Состояние проверяется после восстановления подключения.</summary>
    Reconnect = 2,

    /// <summary>Лучшая цена покупки не совпала с контрольным событием.</summary>
    BestBidMismatch = 3,

    /// <summary>Лучшая цена продажи не совпала с контрольным событием.</summary>
    BestAskMismatch = 4,

    /// <summary>Вычисленный спред не совпал с контрольным событием.</summary>
    SpreadMismatch = 5,

    /// <summary>Обнаружено расхождение шага цены.</summary>
    TickSizeMismatch = 6,

    /// <summary>Локальный стакан содержит пересекающиеся лучшие цены.</summary>
    CrossedBook = 7,

    /// <summary>Обнаружен пропуск последовательности событий.</summary>
    GapDetected = 8,

    /// <summary>Состояние признано устаревшим без более точной причины.</summary>
    StaleState = 9,

    /// <summary>Внешний hash используется как диагностический признак расхождения.</summary>
    HashMismatch = 10
}
