using System.Text.Json;

namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Результат структурного декодирования одного элемента исходного сообщения.</summary>
public sealed record RawMessageItemDecodeResult
{
    private RawMessageItemDecodeResult(
        int rawItemIndex,
        JsonElement? json,
        NormalizationIssue? issue)
    {
        if (rawItemIndex < 0)
            throw new ArgumentOutOfRangeException(
                nameof(rawItemIndex),
                "Raw item index cannot be negative.");

        RawItemIndex = rawItemIndex;
        Json = json;
        Issue = issue;
    }

    /// <summary>Исходная позиция элемента; для корневого объекта всегда равна нулю.</summary>
    public int RawItemIndex { get; }

    /// <summary>Копия декодированного JSON-объекта или <see langword="null" /> при ошибке элемента.</summary>
    public JsonElement? Json { get; }

    /// <summary>Ошибка формы элемента или <see langword="null" /> для декодированного объекта.</summary>
    public NormalizationIssue? Issue { get; }

    /// <summary>Показывает, что элемент успешно декодирован в JSON-объект.</summary>
    public bool IsDecoded => Json.HasValue;

    /// <summary>Создаёт успешный результат и сохраняет собственную копию JSON-объекта.</summary>
    /// <param name="rawItemIndex">Исходная позиция элемента.</param>
    /// <param name="json">Декодированный JSON-объект.</param>
    public static RawMessageItemDecodeResult Decoded(
        int rawItemIndex,
        JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Decoded item must be a JSON object.", nameof(json));

        return new RawMessageItemDecodeResult(rawItemIndex, json.Clone(), null);
    }

    /// <summary>Создаёт неуспешный результат для элемента с сохранением его исходной позиции.</summary>
    /// <param name="rawItemIndex">Исходная позиция элемента.</param>
    /// <param name="issue">Структурированная ошибка декодирования.</param>
    public static RawMessageItemDecodeResult Invalid(
        int rawItemIndex,
        NormalizationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return new RawMessageItemDecodeResult(rawItemIndex, null, issue);
    }
}
