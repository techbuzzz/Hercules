using Hercules.Backup;
using Microsoft.AspNetCore.Mvc;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Backup and restore endpoints (task_063).
/// </summary>
[ApiController]
[Route("api/backups")]
public sealed class BackupController : ControllerBase
{
    private readonly IBackupService _backup;

    public BackupController(IBackupService backup)
    {
        _backup = backup ?? throw new ArgumentNullException(nameof(backup));
    }

    /// <summary>Creates a new backup archive.</summary>
    [HttpPost]
    public async Task<ActionResult<BackupResult>> CreateBackup(
        [FromBody] CreateBackupRequest? request,
        CancellationToken ct)
    {
        var result = await _backup.CreateBackupAsync(request?.Passphrase, ct);
        return Ok(result);
    }

    /// <summary>Lists all available backups.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BackupSummary>>> ListBackups(CancellationToken ct)
    {
        var backups = await _backup.ListBackupsAsync(ct);
        return Ok(backups);
    }

    /// <summary>Restores a backup archive.</summary>
    [HttpPost("{backupId}/restore")]
    public async Task<ActionResult<RestoreResult>> Restore(
        string backupId,
        [FromBody] RestoreBackupRequest? request,
        CancellationToken ct)
    {
        var result = await _backup.RestoreAsync(backupId, request?.Passphrase, request?.TargetDir, ct);
        if (!result.Success && result.Errors.Count > 0)
            return UnprocessableEntity(result);
        return Ok(result);
    }

    /// <summary>Verifies integrity of a backup archive.</summary>
    [HttpGet("{backupId}/verify")]
    public async Task<ActionResult<VerifyResult>> Verify(string backupId, [FromQuery] string? passphrase, CancellationToken ct)
    {
        var result = await _backup.VerifyBackupAsync(backupId, passphrase, ct);
        if (!result.Valid)
            return UnprocessableEntity(result);
        return Ok(result);
    }

    /// <summary>Deletes a backup archive.</summary>
    [HttpDelete("{backupId}")]
    public async Task<ActionResult> Delete(string backupId, CancellationToken ct)
    {
        await _backup.DeleteBackupAsync(backupId, ct);
        return NoContent();
    }
}

public sealed record CreateBackupRequest(string? Passphrase);

public sealed record RestoreBackupRequest(string? Passphrase, string? TargetDir);
