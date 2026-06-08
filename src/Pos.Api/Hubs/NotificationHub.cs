using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Pos.Application.Identity;

namespace Pos.Api.Hubs;

/// <summary>
/// Real-time back-office channel. A connected manager joins their (tenant, store) group on connect, so a
/// broadcast reaches only that store's clients. Cookie-authenticated (the back-office is a cookie app);
/// the browser sends the auth cookie on the WebSocket handshake automatically. The hub itself is push-only
/// — clients listen for "notification"; they never invoke server methods here.
/// </summary>
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public sealed class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenant = Context.User?.FindFirstValue(PosClaims.TenantId);
        var store = Context.User?.FindFirstValue(PosClaims.StoreId);
        if (tenant is not null && store is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(Guid.Parse(tenant), Guid.Parse(store)));
        await base.OnConnectedAsync();
    }

    /// <summary>The SignalR group a (tenant, store) shares — broadcasts are scoped to it.</summary>
    public static string GroupName(Guid tenantId, Guid storeId) => $"store:{tenantId}:{storeId}";
}
