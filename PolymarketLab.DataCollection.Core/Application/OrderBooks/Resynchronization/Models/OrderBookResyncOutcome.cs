namespace PolymarketLab.DataCollection.Core.Application.OrderBooks.Resynchronization.Models;

/// <summary>Итог попытки восстановления локального стакана.</summary>
public enum OrderBookResyncOutcome
{
    /// <summary>Проверенный полный снимок успешно опубликован.</summary>
    Synchronized = 1,

    /// <summary>Восстановление не завершилось публикацией REST-снимка.</summary>
    Failed = 2
}
