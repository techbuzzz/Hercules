using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hercules.WorkflowServer.Config;

namespace Hercules.WorkflowServer.Auth;

/// <summary>
///     Персистентное хранилище clientId/clientSecret (task_104, ADR-0008).
///     При первом запуске, если <c>Auth.ClientId/ClientSecret</c> не заданы —
///     генерирует пару и сохраняет в <c>DataRoot/security/workflow-credentials.json</c>.
///     Идемпотентно: при наличии файла — загружается, не перегенерируется.
/// </summary>
public sealed class ClientCredentialStore
{
    private const string SecuritySubdir = "security";
    private const string CredentialsFileName = "workflow-credentials.json";
    private const int RandomBytes = 32; // 256 бит энтропии → ~43 Base64-символа

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ILogger<ClientCredentialStore> _logger;
    private readonly string _dataRoot;

    public ClientCredentialStore(WorkflowServerConfig cfg, ILogger<ClientCredentialStore> logger)
    {
        _dataRoot = cfg.DataRoot;
        _logger = logger;
    }

    /// <summary>Путь к файлу credentials (для логов и отображения).</summary>
    public string CredentialsFilePath => Path.Combine(_dataRoot, SecuritySubdir, CredentialsFileName);

    /// <summary>
    ///     Возвращает финальные credentials: сначала из <see cref="AuthSection"/>,
    ///     иначе из файла, иначе генерирует и сохраняет.
    ///     Метод идемпотентен: при наличии файла не перегенерирует.
    /// </summary>
    public ClientCredential LoadOrGenerate(AuthSection configured)
    {
        // 1. Если в конфиге уже есть clientId+secret — используем их.
        if (!string.IsNullOrEmpty(configured.ClientId) && !string.IsNullOrEmpty(configured.ClientSecret))
        {
            _logger.LogDebug("Client credentials: используется clientId={ClientId} из конфигурации", configured.ClientId);
            return new ClientCredential(configured.ClientId, configured.ClientSecret);
        }

        // 2. Если файл есть — загружаем из него.
        if (File.Exists(CredentialsFilePath))
        {
            try
            {
                var fromFile = LoadFromFile();
                if (fromFile is not null)
                {
                    _logger.LogInformation("Client credentials: загружено clientId={ClientId} из {Path}", fromFile.ClientId, CredentialsFilePath);
                    return fromFile;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Client credentials: не удалось прочитать {Path}, генерируем новые", CredentialsFilePath);
            }
        }

        // 3. Иначе — генерируем и сохраняем.
        var generated = Generate();
        try
        {
            SaveToFile(generated);
            _logger.LogInformation("Client credentials: сгенерированы и сохранены в {Path}", CredentialsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Client credentials: не удалось сохранить в {Path}; credentials валидны только до перезапуска", CredentialsFilePath);
        }
        return generated;
    }

    /// <summary>Генерирует новую пару clientId/clientSecret.</summary>
    public static ClientCredential Generate()
    {
        // Префиксы облегчают визуальную идентификацию в логах.
        var clientId = "wfs_" + GenerateRandomToken(8);
        var clientSecret = "wfs_secret_" + GenerateRandomToken(RandomBytes);
        return new ClientCredential(clientId, clientSecret);
    }

    private ClientCredential? LoadFromFile()
    {
        var json = File.ReadAllText(CredentialsFilePath, Encoding.UTF8);
        var doc = JsonSerializer.Deserialize<CredentialsFile>(json, JsonOptions);
        if (doc is null || string.IsNullOrEmpty(doc.ClientId) || string.IsNullOrEmpty(doc.ClientSecret))
        {
            return null;
        }
        return new ClientCredential(doc.ClientId, doc.ClientSecret);
    }

    private void SaveToFile(ClientCredential creds)
    {
        var dir = Path.GetDirectoryName(CredentialsFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        using var stream = new FileStream(CredentialsFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, new CredentialsFile { ClientId = creds.ClientId, ClientSecret = creds.ClientSecret }, JsonOptions);
    }

    private static string GenerateRandomToken(int bytes)
    {
        var random = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(random)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private sealed class CredentialsFile
    {
        [JsonPropertyName("clientId")]
        public string ClientId { get; set; } = "";

        [JsonPropertyName("clientSecret")]
        public string ClientSecret { get; set; } = "";
    }
}

/// <summary>
///     Учётные данные клиента (Studio → workflow-server) — task_104, ADR-0008.
///     Упрощённая модель: только одна пара clientId/clientSecret на инсталляцию (без JWT, без ролей).
/// </summary>
public sealed record ClientCredential(string ClientId, string ClientSecret);
