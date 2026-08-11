using System.Globalization;
using System.Text.Json;
using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;

internal static class PolymarketJsonReader
{
    private const string RequiredCode = "normalization.field.required";
    private const string InvalidStringCode = "normalization.field.string.invalid";
    private const string InvalidDecimalCode = "normalization.field.decimal.invalid";
    private const string InvalidTimestampCode = "normalization.field.timestamp.invalid";
    private const string InvalidTradeSideCode = "normalization.field.trade_side.invalid";

    private const NumberStyles DecimalStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public static Result<string, NormalizationIssue> ReadRequiredString(
        JsonElement json,
        string field)
    {
        ValidateArguments(json, field);

        if (!json.TryGetProperty(field, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return Required(field);
        }

        if (value.ValueKind != JsonValueKind.String)
            return InvalidString(field);

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text)
            ? Required(field)
            : text;
    }

    public static Result<string?, NormalizationIssue> ReadOptionalString(
        JsonElement json,
        string field)
    {
        ValidateArguments(json, field);

        if (!json.TryGetProperty(field, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return Result.Success<string?, NormalizationIssue>(null);
        }

        if (value.ValueKind != JsonValueKind.String)
            return InvalidString(field);

        return value.GetString();
    }

    public static Result<decimal, NormalizationIssue> ReadRequiredDecimal(
        JsonElement json,
        string field)
    {
        var textResult = ReadRequiredString(json, field);
        if (textResult.IsFailure)
            return textResult.Error;

        return decimal.TryParse(
            textResult.Value,
            DecimalStyles,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : new NormalizationIssue(
                InvalidDecimalCode,
                "Field must be a valid invariant decimal string.",
                field);
    }

    public static Result<decimal?, NormalizationIssue> ReadOptionalDecimal(
        JsonElement json,
        string field)
    {
        var textResult = ReadOptionalString(json, field);
        if (textResult.IsFailure)
            return textResult.Error;

        if (textResult.Value is null)
            return Result.Success<decimal?, NormalizationIssue>(null);

        return decimal.TryParse(
            textResult.Value,
            DecimalStyles,
            CultureInfo.InvariantCulture,
            out var value)
            ? Result.Success<decimal?, NormalizationIssue>(value)
            : new NormalizationIssue(
                InvalidDecimalCode,
                "Field must be a valid invariant decimal string.",
                field);
    }

    public static Result<long, NormalizationIssue> ReadRequiredEpochMilliseconds(
        JsonElement json,
        string field)
    {
        var textResult = ReadRequiredString(json, field);
        if (textResult.IsFailure)
            return textResult.Error;

        return long.TryParse(
            textResult.Value,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : new NormalizationIssue(
                InvalidTimestampCode,
                "Field must be a valid epoch-millisecond string.",
                field);
    }

    public static Result<long?, NormalizationIssue> ReadOptionalEpochMilliseconds(
        JsonElement json,
        string field)
    {
        var textResult = ReadOptionalString(json, field);
        if (textResult.IsFailure)
            return textResult.Error;

        if (textResult.Value is null)
            return Result.Success<long?, NormalizationIssue>(null);

        return long.TryParse(
            textResult.Value,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var value)
            ? Result.Success<long?, NormalizationIssue>(value)
            : new NormalizationIssue(
                InvalidTimestampCode,
                "Field must be a valid epoch-millisecond string.",
                field);
    }

    public static Result<TradeSide, NormalizationIssue> ReadRequiredTradeSide(
        JsonElement json,
        string field)
    {
        var textResult = ReadRequiredString(json, field);
        if (textResult.IsFailure)
            return textResult.Error;

        return textResult.Value switch
        {
            "BUY" => TradeSide.Buy,
            "SELL" => TradeSide.Sell,
            _ => new NormalizationIssue(
                InvalidTradeSideCode,
                "Field must be either 'BUY' or 'SELL'.",
                field)
        };
    }

    private static void ValidateArguments(JsonElement json, string field)
    {
        if (json.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("JSON value must be an object.", nameof(json));

        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field name is required.", nameof(field));
    }

    private static NormalizationIssue Required(string field)
    {
        return new NormalizationIssue(
            RequiredCode,
            "Required field is missing or empty.",
            field);
    }

    private static NormalizationIssue InvalidString(string field)
    {
        return new NormalizationIssue(
            InvalidStringCode,
            "Field must be a JSON string.",
            field);
    }
}
