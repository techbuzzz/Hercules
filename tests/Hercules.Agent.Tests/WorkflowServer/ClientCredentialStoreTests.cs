using Hercules.WorkflowServer.Auth;
using Hercules.WorkflowServer.Config;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.WorkflowServer;

/// <summary>
///     task_104: unit-тесты для <see cref="ClientCredentialStore"/> (ADR-0008).
///     Покрывает: генерацию, save/load round-trip, формат файла,
///     backward-compat (configured credentials имеют приоритет над файлом).
/// </summary>
public class ClientCredentialStoreTests : IDisposable
{
    private readonly string _tempDir;

    public ClientCredentialStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hercules-wfs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Generate_ProducesPrefixedClientIdAndSecret()
    {
        var creds = ClientCredentialStore.Generate();

        Assert.StartsWith("wfs_", creds.ClientId);
        Assert.StartsWith("wfs_secret_", creds.ClientSecret);
        // Энтропия: после префикса wfs_ идёт случайная часть.
        var idRandom = creds.ClientId.Split('_', 2)[1];
        var secretRandom = creds.ClientSecret.Split("_secret_", 2)[1];
        Assert.True(idRandom.Length >= 8, $"ClientId random part should be >= 8 chars, was {idRandom.Length}");
        Assert.True(secretRandom.Length >= 40, $"ClientSecret random part should be >= 40 chars, was {secretRandom.Length}");
    }

    [Fact]
    public void LoadOrGenerate_ConfiguredCreds_HavePriority()
    {
        var store = NewStore();
        var configured = new AuthSection { ClientId = "wfs_configured", ClientSecret = "wfs_secret_configured" };

        var result = store.LoadOrGenerate(configured);

        Assert.Equal("wfs_configured", result.ClientId);
        Assert.Equal("wfs_secret_configured", result.ClientSecret);
        // При наличии configured — файл НЕ создаётся.
        Assert.False(File.Exists(store.CredentialsFilePath));
    }

    [Fact]
    public void LoadOrGenerate_EmptyConfig_GeneratesAndPersists()
    {
        var store = NewStore();
        var configured = new AuthSection();

        var first = store.LoadOrGenerate(configured);

        Assert.False(string.IsNullOrEmpty(first.ClientId));
        Assert.False(string.IsNullOrEmpty(first.ClientSecret));
        // Файл должен быть создан.
        Assert.True(File.Exists(store.CredentialsFilePath));
    }

    [Fact]
    public void LoadOrGenerate_OnSecondCall_ReturnsSameCredentials()
    {
        var store = NewStore();
        var first = store.LoadOrGenerate(new AuthSection());
        var second = store.LoadOrGenerate(new AuthSection());

        Assert.Equal(first.ClientId, second.ClientId);
        Assert.Equal(first.ClientSecret, second.ClientSecret);
    }

    [Fact]
    public void LoadOrGenerate_AfterFileTampering_Regenerates()
    {
        var store = NewStore();
        var first = store.LoadOrGenerate(new AuthSection());

        // Подделываем файл — повреждаем JSON, чтобы LoadFromFile бросил.
        File.WriteAllText(store.CredentialsFilePath, "{ this is not valid json");

        var second = store.LoadOrGenerate(new AuthSection());

        // Сгенерированные заново credentials должны отличаться (хотя бы один из них).
        Assert.NotEqual(first.ClientId, second.ClientId);
    }

    private ClientCredentialStore NewStore()
    {
        var cfg = new WorkflowServerConfig { DataRoot = _tempDir };
        return new ClientCredentialStore(cfg, NullLogger<ClientCredentialStore>.Instance);
    }
}
