namespace PolymarketLab.DataCollection.Core.Domain.Models.Enums;

/// <summary>Определяет подтверждённую судьбу набора данных завершившейся с ошибкой сессии.</summary>
public enum CollectorDatasetDisposition
{
    /// <summary>Набор данных сохранён на ограниченный срок для диагностики.</summary>
    Retained = 0,

    /// <summary>Набор данных удалён.</summary>
    Deleted = 1
}
