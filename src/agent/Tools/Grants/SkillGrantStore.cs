using Hercules.Tools.Policy;
using Microsoft.Data.Sqlite;

namespace Hercules.Tools.Grants;

/// <summary>
///     SQLite-backed persistent store for skill grants.
/// </summary>
public sealed class SkillGrantStore : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _conn;

    public SkillGrantStore(string dbPath)
    {
        _connectionString = $"Data Source={dbPath};Pooling=false";
        _conn = new SqliteConnection(_connectionString);
        _conn.Open();
        InitSchema(_conn);
    }

    private static void InitSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS skill_grants (
                id TEXT PRIMARY KEY,
                skill_id TEXT NOT NULL,
                permission INTEGER NOT NULL,
                scope INTEGER NOT NULL,
                session_id TEXT,
                grantor TEXT NOT NULL,
                granted_at TEXT NOT NULL,
                expires_at TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_grants_skill ON skill_grants(skill_id);
            CREATE INDEX IF NOT EXISTS ix_grants_session ON skill_grants(session_id);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    ///     Upsert a grant (replace if same skill_id + permission + scope + session_id).
    /// </summary>
    public void UpsertGrant(SkillGrant grant)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO skill_grants (id, skill_id, permission, scope, session_id, grantor, granted_at, expires_at)
            VALUES ($id, $skillId, $permission, $scope, $sessionId, $grantor, $grantedAt, $expiresAt)
            ON CONFLICT(id) DO UPDATE SET
                permission = excluded.permission,
                scope = excluded.scope,
                session_id = excluded.session_id,
                grantor = excluded.grantor,
                granted_at = excluded.granted_at,
                expires_at = excluded.expires_at;
            """;
        cmd.Parameters.AddWithValue("$id", grant.Id);
        cmd.Parameters.AddWithValue("$skillId", grant.SkillId);
        cmd.Parameters.AddWithValue("$permission", (int)grant.Permission);
        cmd.Parameters.AddWithValue("$scope", (int)grant.Scope);
        cmd.Parameters.AddWithValue("$sessionId", grant.SessionId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$grantor", grant.Grantor);
        cmd.Parameters.AddWithValue("$grantedAt", grant.GrantedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$expiresAt", grant.ExpiresAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    ///     Get all active grants for a skill (merges Skill, Session, and Global scope).
    /// </summary>
    public SkillGrant[] GetGrantsForSkill(string skillId, string? sessionId = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, skill_id, permission, scope, session_id, grantor, granted_at, expires_at
            FROM skill_grants
            WHERE skill_id = $skillId AND (
               scope = $skill
               OR (scope = $session AND session_id = $sessionId)
               OR scope = $global
            )
            """;
        cmd.Parameters.AddWithValue("$skillId", skillId);
        cmd.Parameters.AddWithValue("$skill", (int)GrantScope.Skill);
        cmd.Parameters.AddWithValue("$global", (int)GrantScope.Global);
        cmd.Parameters.AddWithValue("$session", (int)GrantScope.Session);
        cmd.Parameters.AddWithValue("$sessionId", sessionId ?? (object)DBNull.Value);

        var grants = new List<SkillGrant>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var expiresAt = reader.IsDBNull(7) ? (DateTime?)null : DateTime.Parse(reader.GetString(7));
            if (expiresAt is not null && expiresAt <= DateTime.UtcNow) continue;
            grants.Add(new SkillGrant
            {
                Id = reader.GetString(0),
                SkillId = reader.GetString(1),
                Permission = (ToolPermission)reader.GetInt32(2),
                Scope = (GrantScope)reader.GetInt32(3),
                SessionId = reader.IsDBNull(4) ? null : reader.GetString(4),
                Grantor = reader.GetString(5),
                GrantedAt = DateTime.Parse(reader.GetString(6)),
                ExpiresAt = expiresAt
            });
        }
        return grants.ToArray();
    }

    /// <summary>
    ///     Revoke all grants for a skill (optionally only a specific permission).
    /// </summary>
    public void RevokeGrants(string skillId, ToolPermission? permission = null)
    {
        using var cmd = _conn.CreateCommand();
        if (permission.HasValue)
        {
            cmd.CommandText = "DELETE FROM skill_grants WHERE skill_id = $skillId AND permission = $perm";
            cmd.Parameters.AddWithValue("$perm", (int)permission.Value);
        }
        else
        {
            cmd.CommandText = "DELETE FROM skill_grants WHERE skill_id = $skillId";
        }
        cmd.Parameters.AddWithValue("$skillId", skillId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    ///     Revoke grants by ID.
    /// </summary>
    public void RevokeGrantById(string grantId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM skill_grants WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", grantId);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _conn.Dispose();
    }
}
