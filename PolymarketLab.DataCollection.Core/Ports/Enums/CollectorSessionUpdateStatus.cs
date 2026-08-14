namespace PolymarketLab.DataCollection.Core.Ports.Enums;

/// <summary>Результат условного обновления сессии сборщика.</summary>
public enum CollectorSessionUpdateStatus
{
    /// <summary>Сессия успешно обновлена.</summary>
    Updated,

    /// <summary>Сохранённое состояние изменилось конкурентно.</summary>
    ConcurrencyConflict
}
