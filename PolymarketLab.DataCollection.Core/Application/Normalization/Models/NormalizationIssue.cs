namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

/// <summary>Структурированное описание ожидаемой ошибки декодирования или нормализации.</summary>
public sealed record NormalizationIssue
{
    /// <summary>Создаёт описание ошибки без включения исходного payload.</summary>
    /// <param name="code">Стабильный машинный код ошибки.</param>
    /// <param name="message">Безопасное диагностическое сообщение.</param>
    /// <param name="field">Имя поля или JSON path, связанный с ошибкой.</param>
    public NormalizationIssue(string code, string message, string? field = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Issue code is required.", nameof(code));

        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Issue message is required.", nameof(message));

        Code = code;
        Message = message;
        Field = field;
    }

    /// <summary>Стабильный машинный код ошибки.</summary>
    public string Code { get; }

    /// <summary>Безопасное диагностическое сообщение без полного исходного JSON.</summary>
    public string Message { get; }

    /// <summary>Имя поля или JSON path; <see langword="null" />, если ошибка относится ко всему сообщению.</summary>
    public string? Field { get; }
}
