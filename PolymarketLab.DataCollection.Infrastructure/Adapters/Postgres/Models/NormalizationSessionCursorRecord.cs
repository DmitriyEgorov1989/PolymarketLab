using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Models;

/// <summary>Сохранённая верхняя граница захваченных raw-сообщений session и версии проекции.</summary>
internal sealed class NormalizationSessionCursorRecord
{
    /// <summary>Создаёт экземпляр для материализации EF Core.</summary>
    private NormalizationSessionCursorRecord()
    {
    }

    /// <summary>Идентификатор collector session; значение не может быть <see langword="null"/>.</summary>
    public CollectorSessionId SessionId { get; private set; } = null!;

    /// <summary>Положительная версия нормализованной проекции.</summary>
    public int ProjectionVersion { get; private set; }

    /// <summary>Положительный максимальный идентификатор raw-сообщения, захваченного в claim.</summary>
    public long LastClaimedRawMessageId { get; private set; }
}
