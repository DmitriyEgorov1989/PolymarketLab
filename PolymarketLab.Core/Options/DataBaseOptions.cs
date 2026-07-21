namespace PolymarketLab.Core.Options;

public sealed class DataBaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; init; } = string.Empty;
}
