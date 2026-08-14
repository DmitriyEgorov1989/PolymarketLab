using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Декодирует сохранённый UTF-8 payload в логические исходные события.</summary>
public interface IRawMessageDecoder
{
    /// <summary>Декодирует одно исходное сообщение без изменения его содержимого.</summary>
    /// <param name="message">Сохранённое исходное сообщение.</param>
    /// <returns>Результат декодирования с логическими событиями или структурированной ошибкой.</returns>
    RawMessageDecodeResult Decode(RawMessageEnvelope message);
}
