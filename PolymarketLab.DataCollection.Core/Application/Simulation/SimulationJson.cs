using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Simulation;

/// <summary>Канонизация собственных упорядоченных моделей эксперимента, версия 1.</summary>
public static class SimulationJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Сериализует модель; словари предварительно преобразуются вызывающим кодом в упорядоченные массивы.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Сохраняет поля конкретного типа закрытого объединения событий.</summary>
    public static string Serialize(object value, Type type) => JsonSerializer.Serialize(value, type, Options);

    /// <summary>Вычисляет SHA-256 канонического UTF-8 представления.</summary>
    public static string Hash<T>(T value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value))));

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new DecimalConverter());
        options.Converters.Add(new UtcConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class DecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture);
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("G29", CultureInfo.InvariantCulture));
    }

    private sealed class UtcConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture);
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }
}

internal static class SimulationErrors
{
    public static Error Invalid(string field, string message) =>
        new($"simulation.{field}", message, ErrorType.Conflict, field);
}
