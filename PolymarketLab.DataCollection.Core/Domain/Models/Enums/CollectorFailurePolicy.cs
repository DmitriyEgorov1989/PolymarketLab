namespace PolymarketLab.DataCollection.Core.Domain.Models.Enums;

/// <summary>Определяет неизменяемую политику обработки набора данных после отказа сессии.</summary>
public enum CollectorFailurePolicy
{
    /// <summary>Удалить набор данных при финализации отказа.</summary>
    DeleteOnFailure = 0,

    /// <summary>Сохранить набор данных на ограниченный срок для диагностики.</summary>
    RetainOnFailure = 1
}
