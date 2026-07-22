namespace PolymarketLab.DataCollection.Core.Domain.Models.Enums
{
    public enum CollectorStopReason
    {
        /// <summary>
        /// Остановка явно запрошена пользователем или вызывающим компонентом.
        /// </summary>
        Requested = 1,

        /// <summary>
        /// Сессия остановлена из-за завершения работы приложения.
        /// </summary>
        ApplicationShutdown = 2,

        /// <summary>
        /// Сбор данных остановлен после закрытия рынка.
        /// </summary>
        MarketClosed = 3,

        /// <summary>
        /// Сессия завершена из-за неустранимой ошибки WebSocket-соединения.
        /// </summary>
        FatalWebSocketError = 4,

        /// <summary>
        /// Сессия завершена из-за ошибки сохранения собранных данных.
        /// </summary>
        PersistenceFailure = 5,

        /// <summary>
        /// Восстановление сессии не завершилось за допустимое время.
        /// </summary>
        RecoveryTimeout = 6
    }
}
