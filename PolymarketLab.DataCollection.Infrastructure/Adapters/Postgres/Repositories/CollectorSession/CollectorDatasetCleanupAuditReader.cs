using Microsoft.EntityFrameworkCore;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.CollectorSession;

internal sealed class CollectorDatasetCleanupAuditReader(DataCollectionDbContext dbContext)
    : ICollectorDatasetCleanupAuditReader
{
    public async Task<CollectorDatasetCleanupAudit?> GetBySessionIdAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.CollectorDatasetCleanupAudits
            .AsNoTracking()
            .SingleOrDefaultAsync(audit => audit.SessionId == sessionId, cancellationToken);
        return record?.ToAudit();
    }
}
