namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Сохранённое состояние обработки исходного сообщения нормализатором.</summary>
public enum NormalizationStatus
{
    /// <summary>Сообщение ожидает обработки.</summary>
    Pending = 1,

    /// <summary>Обработка сообщения выполняется.</summary>
    Processing = 2,

    /// <summary>Сообщение успешно преобразовано в нормализованные записи.</summary>
    Processed = 3,

    /// <summary>Для типа события нет поддерживаемого нормализатора.</summary>
    Unsupported = 4,

    /// <summary>Сообщение не соответствует ожидаемому внешнему контракту.</summary>
    Invalid = 5,

    /// <summary>Обработка завершилась непредвиденной технической ошибкой.</summary>
    Failed = 6
}
