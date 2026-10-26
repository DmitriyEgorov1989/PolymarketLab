using System.Collections.Immutable;

namespace PolymarketLab.DataCollection.Core.Application.Simulation.Reporting;

/// <summary>Каноническая запись без реального времени, случайных ID и raw payload.</summary>
/// <param name="Sequence">Порядковый номер записи.</param>
/// <param name="At">Виртуальный момент наблюдения.</param>
/// <param name="Kind">Устойчивый тип записи.</param>
/// <param name="Data">Канонические JSON-данные записи.</param>
public sealed record SimulationJournalEntry(long Sequence, DateTimeOffset At, string Kind, string Data);

/// <summary>Последовательный журнал одного запуска; не раскрывается сценарию.</summary>
public sealed class SimulationJournal
{
    private readonly List<SimulationJournalEntry> _entries = [];

    /// <summary>Возвращает неизменяемую копию накопленного журнала.</summary>
    public ImmutableArray<SimulationJournalEntry> Entries => _entries.ToImmutableArray();

    internal void Add<T>(DateTimeOffset at, string kind, T data) =>
        _entries.Add(new SimulationJournalEntry(_entries.Count + 1L, at, kind, SimulationJson.Serialize(data)));
}
