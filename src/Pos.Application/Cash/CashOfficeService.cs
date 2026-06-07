using Pos.Application.Abstractions;
using Pos.Application.Tenancy;
using Pos.Domain.Cash;
using Pos.Domain.Identity;
using Pos.SharedKernel;

namespace Pos.Application.Cash;

/// <summary>Closing a shift whose variance exceeds the configured threshold needs a Manager to
/// acknowledge it — a cashier alone cannot wave through a big over/short (anti-skimming).</summary>
public sealed class VarianceAcknowledgementRequiredException : InvalidOperationException
{
    public VarianceAcknowledgementRequiredException(decimal variance, decimal threshold)
        : base($"Cash variance of {variance:0.00} exceeds the {threshold:0.00} threshold; a manager must acknowledge the close.") { }
}

/// <summary>
/// Cash-office write side: open a shift (float), record drawer movements, and close (cash-up). One Open
/// session per register; a closed session is immutable. Expected cash at close is computed from the
/// session's facts (the read projection), then frozen with the counted amount + variance.
/// </summary>
public sealed class CashOfficeService
{
    private readonly ICurrentContext _ctx;
    private readonly IRegisterSessionRepository _sessions;
    private readonly ICashMovementRepository _movements;
    private readonly IRegisterRepository _registers;
    private readonly IMerchantProfileRepository _merchants;
    private readonly CashOfficeReportService _reports;
    private readonly CashOfficeOptions _options;
    private readonly ISetupGuard _setup;
    private readonly IUnitOfWork _uow;

    public CashOfficeService(ICurrentContext ctx, IRegisterSessionRepository sessions, ICashMovementRepository movements,
        IRegisterRepository registers, IMerchantProfileRepository merchants, CashOfficeReportService reports,
        CashOfficeOptions options, ISetupGuard setup, IUnitOfWork uow)
    {
        _ctx = ctx;
        _sessions = sessions;
        _movements = movements;
        _registers = registers;
        _merchants = merchants;
        _reports = reports;
        _options = options;
        _setup = setup;
        _uow = uow;
    }

    public Task<RegisterSession?> GetOpenAsync(Guid registerId, CancellationToken ct = default) =>
        _sessions.GetOpenAsync(_ctx.TenantId, _ctx.StoreId, registerId, ct);

    /// <summary>Open a shift on a register with its opening float. Fails if one is already open.</summary>
    public async Task<RegisterSession> OpenAsync(Guid registerId, decimal openingFloat, CancellationToken ct = default)
    {
        await _setup.EnsureConfiguredAsync(ct);
        if (await _sessions.GetOpenAsync(_ctx.TenantId, _ctx.StoreId, registerId, ct) is not null)
            throw new InvalidOperationException("A session is already open for this register; close it first.");

        var currency = await CurrencyAsync(ct);
        var register = await _registers.GetOrCreateAsync(_ctx.TenantId, _ctx.StoreId, registerId, ct);
        var session = RegisterSession.Open(_ctx.TenantId, _ctx.StoreId, registerId, register.DisplayLabel,
            _ctx.UserId, _ctx.UserName, new Money(openingFloat, currency));
        await _sessions.AddAsync(session, ct);
        await _uow.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>Record a drawer movement (Supervisor+ at the endpoint). Opening float is set at open,
    /// never here; sales cash and refunds are Tender/CreditNote facts, never movements.</summary>
    public async Task<CashMovement> RecordMovementAsync(Guid registerId, CashMovementType type, decimal amount,
        string? reason, CancellationToken ct = default)
    {
        if (type == CashMovementType.OpeningFloat)
            throw new ArgumentException("Opening float is recorded when the shift opens, not as a movement.", nameof(type));

        var session = await _sessions.GetOpenAsync(_ctx.TenantId, _ctx.StoreId, registerId, ct)
            ?? throw new InvalidOperationException("No open session for this register; open a shift first.");

        var movement = CashMovement.Record(_ctx.TenantId, _ctx.StoreId, registerId, session.Id, type,
            new Money(amount, session.OpeningFloat.Currency), reason, _ctx.UserId, _ctx.UserName);
        await _movements.AddAsync(movement, ct);
        await _uow.SaveChangesAsync(ct);
        return movement;
    }

    /// <summary>Close a shift: compute expected from facts, freeze counted + variance. A variance beyond
    /// the threshold requires a Manager (acknowledged). Returns the final Z report.</summary>
    public async Task<ShiftReport> CloseAsync(Guid sessionId, decimal countedCash, bool acknowledged, CancellationToken ct = default)
    {
        var session = await _sessions.GetAsync(_ctx.TenantId, _ctx.StoreId, sessionId, ct)
            ?? throw new InvalidOperationException("Session not found in this store.");
        if (!session.IsOpen) throw new InvalidOperationException("Session is already closed.");

        var report = await _reports.BuildAsync(session, ct);
        var expected = report.Cash.Expected;
        var variance = countedCash - expected;

        var overThreshold = Math.Abs(variance) > _options.VarianceAckThreshold;
        if (overThreshold && !(acknowledged && _ctx.Role == UserRole.Manager))
            throw new VarianceAcknowledgementRequiredException(variance, _options.VarianceAckThreshold);

        session.Close(_ctx.UserId, _ctx.UserName, new Money(countedCash, session.OpeningFloat.Currency),
            new Money(expected, session.OpeningFloat.Currency), varianceAcknowledged: overThreshold);
        await _uow.SaveChangesAsync(ct);

        return await _reports.BuildAsync(session, ct); // now a Z report with counted/variance
    }

    private async Task<string> CurrencyAsync(CancellationToken ct) =>
        (await _merchants.GetAsync(_ctx.TenantId, ct))?.Currency ?? "KES";
}
