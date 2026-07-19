using CSharpFunctionalExtensions;
using System.Diagnostics.CodeAnalysis;

namespace PolymarketLab.SharedKernel.Errors;

[ExcludeFromCodeCoverage]
public sealed partial class Error : ValueObject
{
    private const string Separator = "||";

    private Error()
    {
    }

    public Error(string code, string message, ErrorType type, string? invalidField = null)
    {
        Code = code;
        Message = message;
        Type = type;
        if (invalidField != null) InvalidField = invalidField;
    }

    /// <summary>
    ///     Код ошибки
    /// </summary>
    public string Code { get; }

    /// <summary>
    ///     Текст ошибки
    /// </summary>
    public string Message { get; }

    /// <summary>
    ///     Типы ошибок
    /// </summary>
    public ErrorType Type { get; }

    public string? InvalidField { get; set; }

    protected override IEnumerable<IComparable> GetEqualityComponents()
    {
        yield return Code;
        yield return Message;
        yield return Type;
        if (InvalidField != null) yield return InvalidField;
    }

    public string Serialize()
    {
        return $"{Code}{Separator}{Message}{Separator}{Type}{Separator}{InvalidField}";
    }

    public static Error Deserialize(string serialized)
    {
        if (serialized == "A non-empty request body is required.")
            return GeneralErrors.ValueIsRequired(nameof(serialized));

        var data = serialized.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries);

        if (data.Length < 4)
            throw new FormatException($"Invalid error serialization: '{serialized}'");

        var type = Enum.Parse<ErrorType>(data[2]);
        var invalidField = string.IsNullOrWhiteSpace(data[3]) ? null : data[3];

        return new Error(data[0], data[1], type, invalidField);
    }
}
