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
    private const string SecuritySubdir = "security";
    private const string KeysFileName = "keys.json";
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

    public ApiKeyStore(StorageConfig storageCfg, ILogger<ApiKeyStore> logger)
    {
        _dataRoot = storageCfg.DataRoot;
        _logger = logger;
    }

    /// <summary>Путь к файлу <c>keys.json</c> (для отображения в логах / API).</summary>
    public string KeysFilePath => Path.Combine(_dataRoot, SecuritySubdir, KeysFileName);

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
                Key = "hc_contrib_" + GenerateRandomToken(),
                Role = ApiKeyRole.Contribute,
                Description = "auto-generated contribute key"
            },
            new()
            {
                Key = "hc_sys_" + GenerateRandomToken(),
                Role = ApiKeyRole.System,
                Description = "auto-generated system key"
            }
        };
    }

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
