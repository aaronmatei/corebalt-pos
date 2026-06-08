// Live back-office notifications over SignalR. The page already renders the current state on load; this
// keeps the reorder badge + a toast updating in real time while the manager has the back office open.
// Best-effort: if the client lib is missing or the user isn't an authenticated manager, it silently does
// nothing (the feed still works on the next page load).
(function () {
    if (typeof signalR === "undefined") return; // /lib/signalr/signalr.min.js not present

    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/notifications")
        .withAutomaticReconnect()
        .build();

    connection.on("notification", function (msg) {
        // Bump the reorder badge (SignalR serializes RealtimeNotification as camelCase).
        var badge = document.getElementById("lowstock-badge");
        if (badge && msg && typeof msg.unreadCount === "number") {
            badge.textContent = msg.unreadCount;
            badge.style.display = msg.unreadCount > 0 ? "" : "none";
        }
        showToast((msg && msg.title) || "Notification", (msg && msg.body) || "");
    });

    connection.start().catch(function () { /* not signed in / offline — ignore */ });

    function showToast(title, body) {
        var host = document.getElementById("toast-host");
        if (!host) {
            host = document.createElement("div");
            host.id = "toast-host";
            host.style.cssText = "position:fixed;right:16px;bottom:16px;z-index:9999;display:flex;flex-direction:column;gap:8px";
            document.body.appendChild(host);
        }
        var toast = document.createElement("div");
        toast.style.cssText = "background:#16223f;color:#fff;padding:10px 14px;border-radius:8px;box-shadow:0 4px 14px rgba(0,0,0,.25);max-width:320px;font-size:13px";
        var t = document.createElement("strong");
        t.textContent = title;
        toast.appendChild(t);
        if (body) {
            var b = document.createElement("div");
            b.style.cssText = "margin-top:2px;opacity:.85";
            b.textContent = body;
            toast.appendChild(b);
        }
        host.appendChild(toast);
        setTimeout(function () { toast.remove(); }, 6000);
    }
})();
