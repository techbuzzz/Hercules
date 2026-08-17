namespace Hercules;

/// <summary>
///     Built-in global constants for the Hercules agent.
///     Any path here is intentionally relative or derived from the runtime data root
///     so the project repository stays clean of generated/operational files.
/// </summary>
public static class BuiltIn
{
    // -------------------------------------------------------------------------
    // Root data directory
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Default name of the agent data directory.
    ///     The absolute path is resolved at runtime to a location outside the
    ///     repository root (see <see cref="ResolveDataRoot"/>).
    /// </summary>
    public const string DataDirectoryName = "HerculesData";

    /// <summary>
    ///     Environment variable that can override the data root.
    ///     If set, it takes precedence over the default resolution logic.
    /// </summary>
    public const string DataRootEnvironmentVariable = "HERCULES_DATA_ROOT";

    // -------------------------------------------------------------------------
    // Subdirectories under the data root
    // -------------------------------------------------------------------------

    public const string SkillsSubdir = "Skills";
    public const string MemorySubdir = "Memory";
    public const string ToolsSubdir = "Tools";
    public const string TemplatesSubdir = "Templates";
    public const string FleetTemplatesSubdir = "FleetTemplates";
    public const string MarketplaceSubdir = "marketplace";
    public const string ExportsSubdir = "exports";
    public const string SecuritySubdir = "security";
    public const string CertsSubdir = "certs";
    public const string IdentitySubdir = "identity";
    public const string SigningSubdir = "signing";
    public const string VulnsSubdir = "vulns";
    public const string RolloutSubdir = "rollout";
    public const string MeshProfilesSubdir = "mesh-profiles";
    public const string MeshAuditSubdir = "mesh-audit";
    public const string MeshEvalSubdir = "mesh-eval";
    public const string ScenariosSubdir = "scenarios";
    public const string ResultsSubdir = "results";
    public const string LogsSubdir = "logs";

    // -------------------------------------------------------------------------
    // File names
    // -------------------------------------------------------------------------

    public const string SqliteDatabaseFileName = "sessions.db";
    public const string GrantsDatabaseFileName = "grants.db";
    public const string RuntimeConfigFileName = "runtime-config.json";
    public const string RestartStateFileName = "restart-state.json";
    public const string AgentCardFileName = "agent-card.json";
    public const string AgentManifestFileName = "agent.manifest.json";
    public const string ApiKeysFileName = "keys.json";
    public const string FleetIdentityFileName = "fleet-identity.json";
    public const string CertificatesFileName = "certificates.json";
    public const string TrustedSignersFileName = "trusted-signers.json";
    public const string SigningKeyFileName = "signing-key.key";
    public const string VulnerabilitiesFileName = "vulnerabilities.json";
    public const string EnrollmentFileName = "enrollment.json";
    public const string RolloutStateFileName = "rollout-state.json";
    public const string RolloutSigningKeyFileName = "rollout-signing-key.key";

    // -------------------------------------------------------------------------
    // Secondary / derived paths
    // -------------------------------------------------------------------------

    public const string ProposalsSubdir = ".proposals";
    public const string BaselinesSubdir = ".baselines";
    public const string DurableFactsSubdir = "DurableFacts";
    public const string EpisodesSubdir = "Episodes";
    public const string SharedMemorySubdir = "shared";
    public const string SharedFactsFileName = "shared_facts.json";

    // -------------------------------------------------------------------------
    // Runtime path resolution helpers
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Resolves the absolute path of the agent data root.
    ///     Priority:
    ///     1. <see cref="DataRootEnvironmentVariable"/> if set and not empty.
    ///     2. A sibling directory next to the repository root named <see cref="DataDirectoryName"/>.
    ///     3. Fallback: user profile / home directory / <see cref="DataDirectoryName"/>.
    /// </summary>
    public static string ResolveDataRoot()
    {
        var env = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return Path.GetFullPath(env);
        }

        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        if (!string.IsNullOrEmpty(repoRoot))
        {
            var sibling = Path.Combine(repoRoot, "..", DataDirectoryName);
            return Path.GetFullPath(sibling);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetEnvironmentVariable("HOME")
                   ?? Environment.GetEnvironmentVariable("USERPROFILE")
                   ?? Path.GetTempPath();
        }

        return Path.GetFullPath(Path.Combine(home, DataDirectoryName));
    }

    /// <summary>
    ///     Combines the resolved data root with a relative sub-path.
    /// </summary>
    public static string DataPath(params string[] parts)
    {
        var segments = new List<string> { ResolveDataRoot() };
        segments.AddRange(parts);
        return Path.Combine(segments.ToArray());
    }

    /// <summary>
    ///     Resolves a path that may be absolute or relative to the data root.
    ///     If <paramref name="path"/> is already rooted, returns it as-is.
    ///     Otherwise returns <paramref name="dataRoot"/> + <paramref name="path"/>.
    /// </summary>
    public static string ResolvePathUnderDataRoot(string dataRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return dataRoot;
        }

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(dataRoot, path));
    }

    /// <summary>
    ///     Walks up from <paramref name="startPath"/> looking for a .git folder,
    ///     returning the directory that contains it (repository root) or null.
    /// </summary>
    private static string? FindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            var gitDir = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitDir))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
