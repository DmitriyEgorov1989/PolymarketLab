namespace PolymarketLab.DataCollection.Core.Domain.Models.Enums
{
    /// <summary>Durable lifecycle-состояние session сбора данных.</summary>
    public enum CollectorSessionStatus
    {
        /// <summary>
        /// Сессия создана и подготавливается к запуску.
        /// </summary>
        Starting = 0,

        /// <summary>
        /// Сессия запущена и выполняет сбор данных.
        /// </summary>
        Running = 1,

        /// <summary>
        /// Сессия завершает сбор данных и освобождает ресурсы.
        /// </summary>
        Stopping = 2,

        /// <summary>
        /// Сессия успешно остановлена.
        /// </summary>
        Stopped = 3,

        /// <summary>
        /// Сессия завершилась из-за ошибки.
        /// </summary>
        Failed = 4,

        /// <summary>
        /// Работа сессии была прервана до штатного завершения.
        /// </summary>
        Interrupted = 5,

        /// <summary>
        /// Сессия ожидает начала подготовки к предметному окну рынка.
        /// </summary>
        Scheduled = 6,

        /// <summary>
        /// Сессия запрещает новые записи и очищает непригодный dataset.
        /// </summary>
        Invalidating = 7
    }
}
