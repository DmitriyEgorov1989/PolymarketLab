namespace PolymarketLab.DataCollection.Core.Ports.Enums;

/// <summary>Результат атомарной записи нормализованного сообщения.</summary>
public enum NormalizationWriteStatus
{
    /// <summary>Проекция и терминальный статус успешно записаны.</summary>
    Written = 1,

    /// <summary>Сообщение этой версии уже имеет терминальный статус.</summary>
    AlreadyCompleted = 2,

    /// <summary>Поколение захвата больше не принадлежит вызывающему обработчику.</summary>
    ClaimLost = 3
}
