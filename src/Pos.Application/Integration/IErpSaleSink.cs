namespace Pos.Application.Integration;

/// <summary>
/// Outbound port for shipping a completed sale to the ERP. The adapter throws on a non-success
/// response so the forwarder can leave the outbox row for retry.
/// </summary>
public interface IErpSaleSink
{
    Task SendSaleAsync(ErpSaleDto sale, CancellationToken ct = default);
}
