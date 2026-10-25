using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Simulation;

/// <summary>Версионированная канонизация JSON и потоковый SHA-256 строк без завершающего LF.</summary>
internal static class ExperimentDatasetFingerprintBuilder
{
    internal static string Canonicalize<T>(T value) => Canonicalize(JsonSerializer.SerializeToElement(value));

    internal static string Canonicalize(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            Write(writer, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    Write(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number when value.TryGetDecimal(out var number):
                writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    internal sealed class Lines : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _first = true;

        internal void Append(string line)
        {
            if (!_first)
                _hash.AppendData(new byte[] { 0x0A });
            _hash.AppendData(Encoding.UTF8.GetBytes(line));
            _first = false;
        }

        internal string Finish() => Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();

        public void Dispose() => _hash.Dispose();
    }
}
