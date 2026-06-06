using Microsoft.AspNetCore.Http;
using Pos.Application.Abstractions;

namespace Pos.Api.Auth;

/// <summary>
/// Forces ICurrentContext to materialize before any business handler runs. Without this,
/// the 401 only fires when a handler happens to inject ICurrentContext via parameter
/// binding — touching it here means routes without that parameter (e.g. a GET that only
/// uses a repository) still get the header check.
/// </summary>
internal sealed class AuthEndpointFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // Touch the identity so a route that doesn't inject ICurrentContext still gets the header
        // check. Reading TenantId forces HeaderCurrentContext to validate all three headers (it
        // loads them lazily now), throwing MissingContextHeaderException → 401 when absent/invalid.
        _ = context.HttpContext.RequestServices.GetRequiredService<ICurrentContext>().TenantId;
        return next(context);
    }
}
