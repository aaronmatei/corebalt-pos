using Microsoft.AspNetCore.Http;
using Pos.Application.Abstractions;

namespace Pos.Api.Auth;

/// <summary>
/// Reads tenant / store / user from trusted request headers (X-Tenant-Id, X-Store-Id,
/// X-User-Id). Real auth (JWT bearer with claims) lands later when the chain/SaaS tier
/// is wired up; until then headers keep the surface curlable and the tests trivial.
/// </summary>
public sealed class HeaderCurrentContext : ICurrentContext
{
    public const string TenantHeader = "X-Tenant-Id";
    public const string StoreHeader  = "X-Store-Id";
    public const string UserHeader   = "X-User-Id";

    public Guid TenantId { get; }
    public Guid StoreId { get; }
    public Guid UserId { get; }

    public HeaderCurrentContext(IHttpContextAccessor accessor)
    {
        var http = accessor.HttpContext
            ?? throw new InvalidOperationException("HeaderCurrentContext requires an active HTTP request.");

        TenantId = RequireGuid(http, TenantHeader);
        StoreId  = RequireGuid(http, StoreHeader);
        UserId   = RequireGuid(http, UserHeader);
    }

    private static Guid RequireGuid(HttpContext http, string name)
    {
        if (!http.Request.Headers.TryGetValue(name, out var values) || values.Count == 0)
            throw new MissingContextHeaderException(name);
        if (!Guid.TryParse(values[0], out var id) || id == Guid.Empty)
            throw new MissingContextHeaderException(name);
        return id;
    }
}

public sealed class MissingContextHeaderException(string headerName)
    : Exception($"Required identity header is missing or invalid: {headerName}.")
{
    public string HeaderName { get; } = headerName;
}
