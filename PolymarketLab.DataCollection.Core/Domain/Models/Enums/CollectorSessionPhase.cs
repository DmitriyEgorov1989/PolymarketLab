namespace PolymarketLab.DataCollection.Core.Domain.Models.Enums;

/// <summary>Точная фаза работы внутри нетерминального состояния сессии сборщика.</summary>
public enum CollectorSessionPhase
{
    /// <summary>Сессия ожидает границы начала подготовки.</summary>
    WaitingForPreparation = 0,

    /// <summary>Runtime устанавливает WebSocket-соединение и отправляет подписку.</summary>
    Connecting = 1,

    /// <summary>Подписка ожидает initial book каждого snapshot-токена.</summary>
    AwaitingInitialBooks = 2,

    /// <summary>Подписка ожидает подтверждающий heartbeat.</summary>
    AwaitingHeartbeat = 3,

    /// <summary>Подписка готова до начала предметного окна.</summary>
    ReadyBeforeWindow = 4,

    /// <summary>Идёт сбор сообщений предметного окна.</summary>
    CollectingWindow = 5,

    /// <summary>Окно завершено, сессия ожидает подтверждение resolution.</summary>
    AwaitingResolution = 6,

    /// <summary>Producer остановлен, очередь raw-сообщений осушается.</summary>
    DrainingRaw = 7,

    /// <summary>Raw dataset сохранён и ожидает завершения нормализации.</summary>
    AwaitingNormalization = 8,

    /// <summary>Непригодный dataset очищается после установки write fences.</summary>
    Cleaning = 9
}
