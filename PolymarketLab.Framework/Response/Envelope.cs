namespace PolymarketLab.Framework.Response;

public record ResponseError(string? ErrorCode, string? ErrorMessage, string? InvalidField);

/// <summary>
///     Шаблон ответа
/// </summary>
public record Envelope
{
    public Envelope(object? result, IEnumerable<ResponseError> errors)
    {
        Result = result;
        ListErrors = errors.ToList();
        CreatedOtc = DateTime.UtcNow;
    }

    /// <summary>
    ///     Результат если есть
    /// </summary>
    public object? Result { get; }

    /// <summary>
    ///     Если есть ошибка,то статус код ошибки
    /// </summary>
    public List<ResponseError> ListErrors { get; }

    /// <summary>
    ///     Дата запроса
    /// </summary>
    public DateTime CreatedOtc { get; }

    public static Envelope Ok(object? result = null)
    {
        return new Envelope(result, []);
    }

    public static Envelope Errors(IEnumerable<ResponseError> errors)
    {
        return new Envelope(null, errors);
    }
}
