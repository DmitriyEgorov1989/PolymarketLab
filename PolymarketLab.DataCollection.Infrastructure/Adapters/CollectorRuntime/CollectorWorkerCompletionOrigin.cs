namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime;

/// <summary>
/// Определяет причину завершения обработчика независимо от результата его работы.
/// Только автономная ошибка должна переводить сохранённую сессию сбора данных в состояние Failed.
/// </summary>
internal enum CollectorWorkerCompletionOrigin
{
    /// <summary>Обработчик завершился во время подключения или отправки сообщения подписки.</summary>
    Startup,

    /// <summary>Обработчик завершился без внешнего запроса из-за ошибки получения данных или нарушения протокола.</summary>
    Autonomous,

    /// <summary>Обработчик завершился после явного вызова StopAsync.</summary>
    RequestedStop,

    /// <summary>Обработчик завершился при остановке приложения.</summary>
    ApplicationShutdown,

    /// <summary>Обработчик уже сохранил invalidation и завершился без повторного failure dispatch.</summary>
    Invalidated
}
