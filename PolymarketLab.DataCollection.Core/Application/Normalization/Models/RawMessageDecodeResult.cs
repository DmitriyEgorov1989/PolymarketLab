namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Результат декодирования одного исходного payload в упорядоченные логические элементы.</summary>
public sealed record RawMessageDecodeResult
{
    private RawMessageDecodeResult(
        IReadOnlyCollection<RawMessageItemDecodeResult> items,
        NormalizationIssue? issue)
    {
        Items = items.ToArray();
        Issue = issue;
    }

    /// <summary>Результаты элементов в порядке их появления в исходном сообщении.</summary>
    public IReadOnlyList<RawMessageItemDecodeResult> Items { get; }

    /// <summary>Ошибка всего payload или <see langword="null" />, если JSON удалось декодировать.</summary>
    public NormalizationIssue? Issue { get; }

    /// <summary>Показывает, что payload удалось декодировать, включая допустимый пустой массив.</summary>
    public bool IsDecoded => Issue is null;

    /// <summary>Создаёт успешный результат с собственной копией списка элементов.</summary>
    /// <param name="items">Упорядоченные результаты декодирования элементов.</param>
    public static RawMessageDecodeResult Decoded(
        IReadOnlyCollection<RawMessageItemDecodeResult> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new RawMessageDecodeResult(items, null);
    }

    /// <summary>Создаёт результат с ошибкой всего payload и без элементов.</summary>
    /// <param name="issue">Ошибка UTF-8, JSON или корневой формы сообщения.</param>
    public static RawMessageDecodeResult Invalid(NormalizationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return new RawMessageDecodeResult([], issue);
    }
}
