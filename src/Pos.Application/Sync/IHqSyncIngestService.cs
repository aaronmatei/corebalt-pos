namespace Pos.Application.Sync;

/// <summary>
/// HQ/cloud side of store→cloud sync: durably accept a batch of changes (idempotent on change id) and
/// project the ones it knows how to (e.g. SaleCompleted → the hq_sales read-model). The caller (the
/// ingest endpoint) has already authenticated the store's sync token and resolved the tenant — this runs
/// with that VERIFIED tenant id, never the client-claimed one.
/// </summary>
public interface IHqSyncIngestService
{
    Task<SyncIngestResponse> IngestAsync(Guid tenantId, SyncIngestRequest request, CancellationToken ct = default);
}
