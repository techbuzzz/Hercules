using Hercules.Backup;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Backup and restore endpoints (task_063).
/// </summary>
public static class BackupController
{
    public static void MapBackups(this IEndpointRouteBuilder app)
    {
        // POST /api/backups — создать новый backup archive.
        app.MapPost("/api/backups", async (CreateBackupRequest? request, IBackupService backup, CancellationToken ct) =>
        {
            var result = await backup.CreateBackupAsync(request?.Passphrase, ct);
            return Results.Ok(result);
        }).WithName("CreateBackup");

        // GET /api/backups — список всех доступных backups.
        app.MapGet("/api/backups", async (IBackupService backup, CancellationToken ct) =>
        {
            var backups = await backup.ListBackupsAsync(ct);
            return Results.Ok(backups);
        }).WithName("ListBackups");

        // POST /api/backups/{backupId}/restore — восстановить из backup archive.
        app.MapPost("/api/backups/{backupId}/restore", async (string backupId, RestoreBackupRequest? request, IBackupService backup, CancellationToken ct) =>
        {
            var result = await backup.RestoreAsync(backupId, request?.Passphrase, request?.TargetDir, ct);
            if (!result.Success && result.Errors.Count > 0)
            {
                return Results.UnprocessableEntity(result);
            }

            return Results.Ok(result);
        }).WithName("RestoreBackup");

        // GET /api/backups/{backupId}/verify — проверить целостность backup archive.
        app.MapGet("/api/backups/{backupId}/verify", async (string backupId, string? passphrase, IBackupService backup, CancellationToken ct) =>
        {
            var result = await backup.VerifyBackupAsync(backupId, passphrase, ct);
            if (!result.Valid)
            {
                return Results.UnprocessableEntity(result);
            }

            return Results.Ok(result);
        }).WithName("VerifyBackup");

        // DELETE /api/backups/{backupId} — удалить backup archive.
        app.MapDelete("/api/backups/{backupId}", async (string backupId, IBackupService backup, CancellationToken ct) =>
        {
            await backup.DeleteBackupAsync(backupId, ct);
            return Results.NoContent();
        }).WithName("DeleteBackup");
    }
}

public sealed record CreateBackupRequest(string? Passphrase);

public sealed record RestoreBackupRequest(string? Passphrase, string? TargetDir);
