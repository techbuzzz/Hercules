using Hercules.Agent;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Skills;
using Hercules.Storage;
using Xunit;

namespace Hercules.Agent.Tests.Phase2Tests;

public class SkillPackagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSkillRepository _repo;
    private readonly SkillPackager _packager;
    private readonly SkillManager _skillManager;

    public SkillPackagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-pkg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _repo = new FileSkillRepository(storageCfg);
        _packager = new SkillPackager(_repo);
        _skillManager = new SkillManager(_repo, new StubLlm("test"), new AgentConfig());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void Export_Creates_Valid_Skillpkg_File()
    {
        var skill = _skillManager.CreateManual("test-skill", ["привет"], "Ты — тестовый ассистент.");
        var path = _packager.Export(skill.Meta.Id);
        Assert.True(File.Exists(path));
        Assert.EndsWith(".skillpkg", path);
    }

    [Fact]
    public void Export_Throws_For_Nonexistent_Skill()
    {
        Assert.Throws<InvalidOperationException>(() => _packager.Export("nonexistent"));
    }

    [Fact]
    public void Import_From_Exported_Package_Restores_Skill()
    {
        var skill = _skillManager.CreateManual("оригинал", ["погода"], "Ты — погодный ассистент.", description: "Прогноз погоды");
        var path = _packager.Export(skill.Meta.Id);

        // Удаляем оригинал
        // (в реальном сценарии импорт происходит в другой агент, но для теста — в тот же)

        // Импортируем с Rename (чтобы не конфликтовать)
        var imported = _packager.Import(path, ConflictResolution.Rename);

        Assert.NotNull(imported);
        Assert.Equal("оригинал", imported.Meta.Name);
        Assert.Equal("Ты — погодный ассистент.", imported.Prompt);
        Assert.Equal(["погода"], imported.Meta.PhraseReceivers);
        Assert.NotEqual(skill.Meta.Id, imported.Meta.Id); // ID должен быть другим (Rename)
    }

    [Fact]
    public void Import_Replace_Strategy_Overwrites_Existing_Skill()
    {
        var skill = _skillManager.CreateManual("замена", ["тест"], "Старый промпт");
        var path = _packager.Export(skill.Meta.Id);

        // Импортируем с Replace — должен перезаписать
        var imported = _packager.Import(path, ConflictResolution.Replace);

        Assert.Equal(skill.Meta.Id, imported.Meta.Id);
        Assert.Equal("Старый промпт", imported.Prompt);
    }

    [Fact]
    public void Import_Skip_Strategy_Returns_Existing_Skill()
    {
        var skill = _skillManager.CreateManual("skip-test", ["skip"], "Промпт");
        var path = _packager.Export(skill.Meta.Id);

        var imported = _packager.Import(path, ConflictResolution.Skip);

        // Skip возвращает существующий навык (по ID, не по ссылке — repo.Load создаёт новый объект)
        Assert.Equal(skill.Meta.Id, imported.Meta.Id);
        Assert.Equal(skill.Prompt, imported.Prompt);
    }

    [Fact]
    public void Validate_Returns_No_Errors_For_Valid_Package()
    {
        var skill = _skillManager.CreateManual("валидный", ["проверка"], "Промпт", description: "Описание");
        var path = _packager.Export(skill.Meta.Id);

        var errors = _packager.Validate(path);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Returns_Errors_For_Nonexistent_Package()
    {
        var errors = _packager.Validate("/nonexistent/path.skillpkg");
        Assert.NotEmpty(errors);
    }
}

/// <summary>
///     Stub LLM для тестов SkillManager (не обращается к реальному провайдеру).
/// </summary>
internal sealed class StubLlm : ILLMClient
{
    private readonly string _response;

    public StubLlm(string response) => _response = response;
    public string ProviderName => "stub";
    public string ModelName => "stub";

    public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => Task.FromResult(new LlmResponse(_response, ProviderName, ModelName));

    public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => CompleteAsync(Roles.Main, messages, ct);

    public IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => StreamAsync(messages, ct);

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return _response;
    }
}