using System.Runtime.CompilerServices;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Skills;
using Hercules.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.AgentCoreTests;

/// <summary>
/// Тесты SkillDeprecationManager: deprecate, rollback, get deprecated, undeprecate.
/// </summary>
public class SkillDeprecationManagerTests : IDisposable
{
    private readonly SkillDeprecationManager _manager;
    private readonly FileSkillRepository _repo;
    private readonly SkillManager _skillManager;
    private readonly string _tempDir;

    public SkillDeprecationManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-depr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var storageCfg = new StorageConfig { DataRoot = _tempDir };
        _repo = new FileSkillRepository(storageCfg, NullLogger<FileSkillRepository>.Instance);
        _skillManager = new SkillManager(_repo, new StubLlmClient("test"), new AgentConfig());
        _manager = new SkillDeprecationManager(_repo, NullLogger<SkillDeprecationManager>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void Deprecate_Sets_DeprecatedAt_and_Reason()
    {
        var skill = _skillManager.CreateManual("test", ["тест"], "Ты — тестовый.");
        var result = _manager.Deprecate(skill.Meta.Id, "заменён новым");
        Assert.NotNull(result);
        Assert.NotNull(result.Meta.DeprecatedAt);
        Assert.Equal("заменён новым", result.Meta.DeprecationReason);
    }

    [Fact]
    public void Deprecate_ReturnsNull_for_nonexistent_skill()
    {
        var result = _manager.Deprecate("nonexistent-id", "reason");
        Assert.Null(result);
    }

    [Fact]
    public void Rollback_Decrements_Version_and_restores_previous_description()
    {
        var skill = _skillManager.CreateManual("rollback-test", ["роллбэк"], "Версия 1 prompt.");
        // Передаём явное description, отличное от текущего, чтобы версии имели разный контент
        var updated = _skillManager.UpdateManual(skill.Meta.Id, null, "Версия 2 prompt.",
            "Это навык версии 2 с другим описанием.");

        Assert.Equal(2, updated!.Meta.Version);
        Assert.Contains("Это навык версии 2", updated.Description);

        var rolled = _manager.Rollback(updated.Meta.Id);

        Assert.NotNull(rolled);
        Assert.Equal(1, rolled!.Meta.Version);
        // При rollback восстанавливается контент .v1.md (от CreateManual)
        Assert.Contains("rollback-test", rolled.Description);
    }

    [Fact]
    public void Rollback_ReturnsNull_for_version_1()
    {
        var skill = _skillManager.CreateManual("v1-only", ["фикс"], "Только первая версия.");
        Assert.Equal(1, skill.Meta.Version);
        var result = _manager.Rollback(skill.Meta.Id);
        Assert.Null(result);
    }

    [Fact]
    public void Rollback_ReturnsNull_for_nonexistent()
    {
        var result = _manager.Rollback("nonexistent-id");
        Assert.Null(result);
    }

    [Fact]
    public void GetDeprecated_Returns_only_deprecated_skills()
    {
        var active = _skillManager.CreateManual("активный", ["живой"], "Активный навык.");
        var deprecated = _skillManager.CreateManual("депрецированный", ["устарел"], "Устарел.");
        _manager.Deprecate(deprecated.Meta.Id, "низкое качество");

        var list = _manager.GetDeprecated();
        Assert.Single(list);
        Assert.Equal("депрецированный", list[0].Meta.Name);
    }

    [Fact]
    public void GetDeprecated_Empty_when_no_deprecated()
    {
        _skillManager.CreateManual("a1", ["a"], "A");
        _skillManager.CreateManual("a2", ["b"], "B");
        var list = _manager.GetDeprecated();
        Assert.Empty(list);
    }

    [Fact]
    public void Undeprecate_Clears_DeprecatedAt()
    {
        var skill = _skillManager.CreateManual("к-восст", ["восст"], "Восстанавливаемый.");
        _manager.Deprecate(skill.Meta.Id, "временно");
        var undeprecated = _manager.Undeprecate(skill.Meta.Id);

        Assert.NotNull(undeprecated);
        Assert.Null(undeprecated.Meta.DeprecatedAt);
        Assert.Null(undeprecated.Meta.DeprecationReason);
    }

    [Fact]
    public void Undeprecate_on_active_skill_returns_skill_unchanged()
    {
        var skill = _skillManager.CreateManual("не-депрец", ["не"], "Не депрецирован.");
        Assert.Null(skill.Meta.DeprecatedAt);
        var result = _manager.Undeprecate(skill.Meta.Id);
        Assert.NotNull(result);
        Assert.Null(result.Meta.DeprecatedAt);
    }

    // ---- Stub ----

    private sealed class StubLlmClient : ILLMClient
    {
        private readonly string _responseText;
        public StubLlmClient(string responseText) => _responseText = responseText;
        public string ProviderName => "stub";
        public string ModelName => "stub-model";
        public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            Task.FromResult(new LlmResponse(_responseText, ProviderName, ModelName));
        public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            CompleteAsync(Roles.Main, messages, ct);
        public IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default) =>
            StreamAsync(messages, ct);
        public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return _responseText;
        }
    }
}
