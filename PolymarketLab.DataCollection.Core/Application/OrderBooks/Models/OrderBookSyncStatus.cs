namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Models;

/// <summary>Степень доверия к актуальности текущего состояния стакана.</summary>
public enum OrderBookSyncStatus
{
    /// <summary>Полный снимок стакана ещё не получен.</summary>
    Uninitialized = 1,

    /// <summary>Состояние построено на полном снимке, нарушений синхронизации не обнаружено.</summary>
    Synchronized = 2,

    /// <summary>Обнаружено возможное расхождение с внешним источником.</summary>
    Suspect = 3,

    /// <summary>Выполняется восстановление состояния через REST.</summary>
    Resynchronizing = 4,

    /// <summary>Состояние нельзя считать актуальным после разрыва или большого отставания.</summary>
    Stale = 5
}
