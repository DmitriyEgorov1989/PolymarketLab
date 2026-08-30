namespace PolymarketLab.DataCollection.Core.Ports.Enums;

/// <summary>Результат попытки вставки сессии сборщика.</summary>
public enum CollectorSessionInsertStatus
{
    /// <summary>Сессия успешно вставлена.</summary>
    Inserted = 1,

    /// <summary>Глобальный exclusive slot уже занят другой нетерминальной сессией.</summary>
    ExclusiveSessionConflict = 2
}
