namespace PolymarketLab.DataCollection.Infrastructure.Adapters.CollectorRuntime.WebSockets;

/// <summary>
/// Описывает WebSocket-соединение и не зависит от конкретной реализации транспорта.
/// Метод Dispose принудительно разрывает соединение, если операция не завершилась за отведённое время.
/// </summary>
internal interface ICollectorWebSocketConnection : IDisposable
{
    /// <summary>Устанавливает соединение с указанным адресом WebSocket.</summary>
    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);

    /// <summary>Отправляет цельное текстовое сообщение с данными подписки.</summary>
    Task SendTextAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken);

    /// <summary>
    /// Записывает следующий фрагмент сообщения в переданный буфер и возвращает сведения о нём.
    /// </summary>
    ValueTask<CollectorWebSocketReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken);

    /// <summary>Штатно закрывает WebSocket-соединение.</summary>
    Task CloseAsync(CancellationToken cancellationToken);
}
