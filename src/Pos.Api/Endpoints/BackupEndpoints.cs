using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Ops;

namespace Pos.Api.Endpoints;

/// <summary>
/// Backups + restore. Viewing/listing/health is Supervisor+; taking a backup and the destructive RESTORE
/// are Manager-only. Restore always takes a safety backup first (handled in the service).
/// </summary>
internal static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackups(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/backups").WithTags("Backups");

        g.MapGet("/", async (IBackupService backups, CancellationToken ct) =>
            Results.Ok(await backups.ListAsync(ct))).RequireAuthorization("SupervisorOrAbove");

        g.MapGet("/health", async (IBackupService backups, CancellationToken ct) =>
            Results.Ok(await backups.GetHealthAsync(ct))).RequireAuthorization("SupervisorOrAbove");

        g.MapPost("/", async (IBackupService backups, CancellationToken ct) =>
        {
            try { return Results.Ok(await backups.BackupNowAsync("manual", ct)); }
            catch (Exception ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError); }
        }).RequireAuthorization("Manager");

        g.MapPost("/restore", async (RestoreRequest req, IBackupService backups, CancellationToken ct) =>
        {
            var result = await backups.RestoreAsync(req.FileName, ct);
            return result.Ok ? Results.Ok(result) : Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict);
        }).RequireAuthorization("Manager");

        return app;
    }
}
