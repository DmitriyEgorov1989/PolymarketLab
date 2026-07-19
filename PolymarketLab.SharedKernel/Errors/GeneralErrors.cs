namespace PolymarketLab.SharedKernel.Errors;

/// <summary>
///     Общие ошибки
/// </summary>
public static class GeneralErrors
{
    public static Error NotFound(string? id = null)
    {
        var forId = id == null ? "" : $" for Id '{id}'";
        return new Error("record.not.found", $"Record not found{forId}", ErrorType.NotFound);
    }

    public static Error Validation(string name, string invalidField)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException(name);
        return new Error("field.is.not.valid", $"Field {name} is not valid", ErrorType.Validation, invalidField);
    }

    public static Error ValueIsInvalid(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException(name);

        return new Error("value.is.invalid", $"Value is invalid for {name}", ErrorType.ValueIsInvalid);
    }

    public static Error ValueIsRequired(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException(name);

        return new Error("value.is.required", $"Value is required for {name}", ErrorType.ValueIsRequired);
    }

    public static Error InvalidLength(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException(name);

        return new Error("invalid.string.length", $"Invalid {name} length", ErrorType.InvalidLength);
    }

    public static Error InvalidSize(string size)
    {
        return new Error("invalid.size", $"Invalid {size} Size", ErrorType.InvalidSize);
    }

    public static Error CollectionIsTooSmall(int min, int current)
    {
        return new Error(
            "collection.is.too.small",
            $"The collection must contain {min} items or more. It contains {current} items.",
            ErrorType.CollectionIsTooSmall);
    }

    public static Error IdentityUser(string code, string description)
    {
        return new Error(
            code,
            description, ErrorType.IdentityUser);
    }

    public static Error CollectionIsTooLarge(int max, int current)
    {
        return new Error(
            "collection.is.too.large",
            $"The collection must contain {max} items or more. It contains {current} items.",
            ErrorType.CollectionIsTooLarge);
    }

    public static Error InternalServerError(string message)
    {
        return new Error("internal.server.error", message, ErrorType.InternalServerError);
    }

    public static Error InvalidCredentials()
    {
        return new Error("invalid.password.or.login",
            "Invalid password or login",
            ErrorType.IdentityUser);
    }
    public static Error Failure(string message)
    {
        if (string.IsNullOrEmpty(message)) throw new ArgumentException(message);

        return new Error("invalid.operation", message, ErrorType.Failure);
    }
}
