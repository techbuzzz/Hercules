using Hercules.Config;
using Hercules.WebApi.Auth;
using Hercules.WebApi.Config;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.WebApi;

/// <summary>
///     task_097: unit-tests for <see cref="ApiKeyStore"/> (ADR-0004).
///     Проверяет: генерацию пары ключей, save/load round-trip, формат файла,
///     backward-compat (configured keys имеют приоритет над файлом).
/// </summary>
public class ApiKeyStoreTests : IDisposable
{
    private readonly string _tempDir;

    public ApiKeyStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hercules-keys-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Generate_ProducesContributeAndSystem_WithCorrectPrefix()
    {
        var keys = ApiKeyStore.Generate();

        Assert.Equal(2, keys.Count);
        Assert.Contains(keys, k => k.Role == ApiKeyRole.Contribute && k.Key.StartsWith("hc_contrib_"));
        Assert.Contains(keys, k => k.Role == ApiKeyRole.System && k.Key.StartsWith("hc_sys_"));
        // Randomness: ключи должны различаться
        Assert.NotEqual(keys[0].Key, keys[1].Key);
    }

    [Fact]
    public void Generate_KeysAreCryptographicallyRandom_LongEnough()
    {
        var keys = ApiKeyStore.Generate();
        foreach (var k in keys)
        {
            // После префикса hc_contrib_/hc_sys_ идёт случайная часть.
            var randomPart = k.Key.Split('_', 3)[2];
            Assert.True(randomPart.Length >= 40, $"Random part should be >= 40 chars, was {randomPart.Length}");
        }
    }

    [Fact]
    public void LoadOrGenerate_WithEmptyConfig_CreatesKeysAndPersists()
    {
        var store = NewStore();
        var configured = new List<ApiKeyEntry>();

        var result = store.LoadOrGenerate(configured);

        Assert.Equal(2, result.Count);
        Assert.True(File.Exists(store.KeysFilePath), "keys.json должен быть создан");
    }

    [Fact]
    public void LoadOrGenerate_WithExistingFile_LoadsFromFile()
    {
        // 1. Создаём keys.json через первый запуск
        var store1 = NewStore();
        var firstRun = store1.LoadOrGenerate(new List<ApiKeyEntry>());
        var originalContrib = firstRun.First(k => k.Role == ApiKeyRole.Contribute).Key;

        // 2. Второй store (новый DataRoot, но с тем же путём) должен прочитать файл
        var store2 = NewStore();
        var secondRun = store2.LoadOrGenerate(new List<ApiKeyEntry>());

        Assert.Equal(originalContrib, secondRun.First(k => k.Role == ApiKeyRole.Contribute).Key);
    }

    [Fact]
    public void LoadOrGenerate_WithConfiguredKeys_DoesNotTouchFile()
    {
        var store = NewStore();
        var configured = new List<ApiKeyEntry>
        {
            new() { Key = "hc_contrib_configured", Role = ApiKeyRole.Contribute }
        };

        var result = store.LoadOrGenerate(configured);

        Assert.Single(result);
        Assert.Equal("hc_contrib_configured", result[0].Key);
        Assert.False(File.Exists(store.KeysFilePath), "Файл не должен создаваться, если ключи в конфиге");
    }

    [Fact]
    public void LoadOrGenerate_ReRegeneratesWhenConfigIsEmptyButFileMissing()
    {
        var store = NewStore();
        Assert.False(File.Exists(store.KeysFilePath)); // fresh

        var result = store.LoadOrGenerate(new List<ApiKeyEntry>());

        Assert.Equal(2, result.Count);
        Assert.True(File.Exists(store.KeysFilePath));
        // Файл содержит оба ключа
        var content = File.ReadAllText(store.KeysFilePath);
        Assert.Contains("hc_contrib_", content);
        Assert.Contains("hc_sys_", content);
    }

    [Fact]
    public void KeysFile_HasExpectedJsonStructure()
    {
        var store = NewStore();
        var keys = store.LoadOrGenerate(new List<ApiKeyEntry>());

        var content = File.ReadAllText(store.KeysFilePath);
        Assert.Contains("\"apiKeys\"", content);
        Assert.Contains("\"key\"", content);
        Assert.Contains("\"role\"", content);
        // Роли сериализуются как строки (ADR-0004) в camelCase.
        Assert.Contains("\"contribute\"", content);
        Assert.Contains("\"system\"", content);
    }

    private ApiKeyStore NewStore()
    {
        var cfg = new StorageConfig { DataRoot = _tempDir };
        return new ApiKeyStore(cfg, NullLogger<ApiKeyStore>.Instance);
    }
}
