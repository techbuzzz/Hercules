using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hercules.Config;
using Hercules.WebApi.Config;

namespace Hercules.WebApi.Auth;

/// <summary>
///     Персистентное хранилище API-ключей (task_097, ADR-0004).
///     При первом запуске, если <c>keys.json</c> не существует и ни один ключ
///     не сконфигурирован — генерирует пару (contribute + system) и сохраняет
///     в <c>DataRoot/security/keys.json</c>. В дальнейшем загружается из файла.
/// </summary>
public sealed class ApiKeyStore
{
    private const string SecuritySubdir = Hercules.BuiltIn.SecuritySubdir;
    private const string KeysFileName = Hercules.BuiltIn.ApiKeysFileName;
    private const int RandomBytes = 32; // 256 бит энтропии → ~43 Base64-символа

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly ILogger<ApiKeyStore> _logger;
    private readonly string _dataRoot;

    // Stage 6.3: the effective key set is a swappable immutable snapshot rather than a
    // constructor-time capture. ApiKeyMiddleware is constructed once per app lifetime,
    // so a role edit made through /api/auth/keys would otherwise only take effect after
    // a restart — a silent security failure for an operator who just demoted a key.
    private volatile KeyTable _active = KeyTable.Empty;

    public ApiKeyStore(StorageConfig storageCfg, ILogger<ApiKeyStore> logger)
    {
        _dataRoot = storageCfg.DataRoot;
        _logger = logger;
    }

    /// <summary>Путь к файлу <c>keys.json</c> (для отображения в логах / API).</summary>
    public string KeysFilePath => Path.Combine(_dataRoot, SecuritySubdir, KeysFileName);

    /// <summary>Currently effective entries. Never exposes the raw key to callers.</summary>
    public IReadOnlyList<ApiKeyEntry> Active => _active.Entries;

    /// <summary>
    /// True when at least one usable key is configured. The middleware treats
    /// <c>false</c> as "open access", so this must stay true in any deployed setup —
    /// see the last-system-key guard in <c>AuthController</c>.
    /// </summary>
    public bool HasKeys => _active.Entries.Length > 0;

    /// <summary>Publishes a new effective key set. Safe to call while requests are in flight.</summary>
    public void SetActive(IReadOnlyList<ApiKeyEntry>? entries)
    {
        _active = KeyTable.Build(entries);
    }

    /// <summary>
    /// Constant-time lookup of <paramref name="provided"/> against the active set.
    /// Reads the table once so a concurrent <see cref="SetActive"/> cannot tear the
    /// comparison across two different snapshots.
    /// </summary>
    public bool TryResolve(ReadOnlySpan<byte> provided, out ApiKeyEntry entry)
    {
        var table = _active;
        for (var i = 0; i < table.Bytes.Length; i++)
        {
            var expected = table.Bytes[i];
            if (expected is null || expected.Length == 0) continue;
            if (provided.Length != expected.Length) continue;
            if (!CryptographicOperations.FixedTimeEquals(provided, expected)) continue;
            entry = table.Entries[i];
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>
    /// Stable, non-reversible identifier for a key, so Studio can refer to a key it has
    /// never seen the value of. 12 hex chars of SHA-256 (48 bits) — enough to disambiguate
    /// a handful of keys, useless for recovering one.
    /// </summary>
    public static string Fingerprint(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    /// <summary>
    /// Overwrites <c>keys.json</c> and publishes the new set as active.
    /// Throws if the file cannot be written — the caller must not report success for a
    /// change that would silently vanish on restart.
    /// </summary>
    public void SaveAndActivate(IReadOnlyList<ApiKeyEntry> keys)
    {
        var snapshot = keys.ToList();
        SaveToFile(snapshot);
        SetActive(snapshot);
        _logger.LogInformation("API keys: saved {Count} key(s) to {Path}", snapshot.Count, KeysFilePath);
    }

    /// <summary>Immutable entry+bytes pair; swapped wholesale so readers never see a partial update.</summary>
    private sealed class KeyTable
    {
        public static readonly KeyTable Empty = new([], []);

        private KeyTable(ApiKeyEntry[] entries, byte[]?[] bytes)
        {
            Entries = entries;
            Bytes = bytes;
        }

        public ApiKeyEntry[] Entries { get; }
        public byte[]?[] Bytes { get; }

        public static KeyTable Build(IReadOnlyList<ApiKeyEntry>? source)
        {
            if (source is null || source.Count == 0) return Empty;

            var entries = source.ToArray();
            var bytes = new byte[]?[entries.Length];
            for (var i = 0; i < entries.Length; i++)
            {
                var key = entries[i]?.Key;
                bytes[i] = string.IsNullOrEmpty(key) ? null : Encoding.UTF8.GetBytes(key);
            }

            return new KeyTable(entries, bytes);
        }
    }

    /// <summary>
    ///     Возвращает финальный список ключей: сначала из уже сконфигурированного
    ///     <paramref name="configuredKeys" />, иначе — из файла, иначе — генерирует и сохраняет.
    ///     Метод идемпотентен: при наличии файла не перегенерирует ключи.
    /// </summary>
    public List<ApiKeyEntry> LoadOrGenerate(IReadOnlyList<ApiKeyEntry> configuredKeys)
    {
        // 1. Если в конфиге уже есть ключи — используем их, не трогаем файл.
        if (configuredKeys.Count > 0)
        {
            _logger.LogDebug("API keys: используется {Count} ключей из конфигурации", configuredKeys.Count);
            return configuredKeys.ToList();
        }

        // 2. Если файл есть — загружаем из него.
        if (File.Exists(KeysFilePath))
        {
            try
            {
                var fromFile = LoadFromFile();
                if (fromFile.Count > 0)
                {
                    _logger.LogInformation("API keys: загружено {Count} ключей из {Path}", fromFile.Count, KeysFilePath);
                    return fromFile;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API keys: не удалось прочитать {Path}, генерируем новые", KeysFilePath);
            }
        }

        // 3. Иначе — генерируем пару и сохраняем.
        var generated = Generate();
        try
        {
            SaveToFile(generated);
            _logger.LogInformation("API keys: сгенерированы и сохранены в {Path}", KeysFilePath);
        }
        catch (Exception ex)
        {
            // Не валим старт, если не удалось сохранить — ключи всё равно работают в этой сессии.
            _logger.LogError(ex, "API keys: не удалось сохранить в {Path}; ключи валидны только до перезапуска", KeysFilePath);
        }
        return generated;
    }

    /// <summary>Генерирует пару ключей (contribute + system) с криптостойкой энтропией.</summary>
    public static List<ApiKeyEntry> Generate()
    {
        return new List<ApiKeyEntry>
        {
            new()
            {
                Key = GenerateKey(ApiKeyRole.Contribute),
                Role = ApiKeyRole.Contribute,
                Description = "auto-generated contribute key"
            },
            new()
            {
                Key = GenerateKey(ApiKeyRole.System),
                Role = ApiKeyRole.System,
                Description = "auto-generated system key"
            }
        };
    }

    /// <summary>
    /// Generates a single key for the given role, using the same 256-bit base64url token
    /// and family prefix as <see cref="Generate"/>. Shared with the Stage 6.3 admin
    /// endpoints so a key created in Studio is indistinguishable from a boot-time one.
    /// </summary>
    public static string GenerateKey(ApiKeyRole role) =>
        (role == ApiKeyRole.System ? "hc_sys_" : "hc_contrib_") + GenerateRandomToken();

    private List<ApiKeyEntry> LoadFromFile()
    {
        var json = File.ReadAllText(KeysFilePath, Encoding.UTF8);
        var doc = JsonSerializer.Deserialize<KeysFile>(json, JsonOptions);
        if (doc is null || doc.ApiKeys is null || doc.ApiKeys.Count == 0)
        {
            return new List<ApiKeyEntry>();
        }
        return doc.ApiKeys;
    }

    private void SaveToFile(List<ApiKeyEntry> keys)
    {
        var dir = Path.GetDirectoryName(KeysFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        // Режим 0600 на Unix — keys.json не должен быть world-readable.
        var fileMode = OperatingSystem.IsWindows() ? FileMode.Create : FileMode.Create;
        using var stream = new FileStream(KeysFilePath, fileMode, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, new KeysFile { ApiKeys = keys }, JsonOptions);
    }

    private static string GenerateRandomToken()
    {
        // Base64Url без padding — компактнее и безопасно для логов.
        var bytes = RandomNumberGenerator.GetBytes(RandomBytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private sealed class KeysFile
    {
        [JsonPropertyName("apiKeys")]
        public List<ApiKeyEntry> ApiKeys { get; set; } = new();
    }
}
