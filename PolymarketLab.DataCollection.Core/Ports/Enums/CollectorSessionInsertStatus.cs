namespace PolymarketLab.DataCollection.Core.Ports.Enums;

/// <summary>Результат попытки вставки сессии сборщика.</summary>
public enum CollectorSessionInsertStatus
{
    /// <summary>Сессия успешно вставлена.</summary>
    Inserted = 1,

    /// <summary>Для рынка уже существует активная сессия.</summary>
    ActiveSessionConflict = 2
}
