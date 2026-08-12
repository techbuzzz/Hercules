using Hercules.Config;
using Hercules.Storage;
using Xunit;

namespace Hercules.Agent.Tests.Storage;

/// <summary>
///     Тесты MemoryStore: профиль, предпочтения, сущности, контекст, reset.
/// </summary>
public class MemoryStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MemoryStore _store;

    public MemoryStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hercules-mem-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var cfg = new StorageConfig { DataRoot = _tempDir, MemoryDir = "Memory" };
        _store = new MemoryStore(cfg);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    #region Profile

    [Fact]
    public async Task ReadProfileAsync_NoFile_ReturnsDefaultContent()
    {
        var content = await _store.ReadProfileAsync();

        Assert.Contains("Профиль пользователя", content);
        Assert.Contains("пока ничего не известно", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteProfileAsync_ThenReadProfile_ReturnsWrittenContent()
    {
        var written = "# Новый профиль\n\nТестовое содержимое.";
        await _store.WriteProfileAsync(written);

        var read = await _store.ReadProfileAsync();

        Assert.Equal(written, read);
    }

    [Fact]
    public void ProfilePath_ReturnsCorrectPath()
    {
        Assert.EndsWith("user_profile.md", _store.ProfilePath);
    }

    #endregion

    #region Preferences

    [Fact]
    public async Task ReadPreferencesAsync_NoFile_ReturnsDefaultContent()
    {
        var content = await _store.ReadPreferencesAsync();

        Assert.Contains("Предпочтения", content);
        Assert.Contains("язык", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WritePreferencesAsync_ThenReadPreferences_ReturnsWrittenContent()
    {
        var written = "# Новые предпочтения\n\nТон: деловой.";
        await _store.WritePreferencesAsync(written);

        var read = await _store.ReadPreferencesAsync();

        Assert.Equal(written, read);
    }

    [Fact]
    public void PreferencesPath_ReturnsCorrectPath()
    {
        Assert.EndsWith("preferences.md", _store.PreferencesPath);
    }

    #endregion

    #region Entities

    [Fact]
    public async Task ReadEntitiesAsync_NoFile_ReturnsDefaultContent()
    {
        var content = await _store.ReadEntitiesAsync();

        Assert.Contains("Известные сущности", content);
    }

    [Fact]
    public async Task WriteEntitiesAsync_ThenReadEntities_ReturnsWrittenContent()
    {
        var written = "# Известные сущности\n\n- Проект Hercules";
        await _store.WriteEntitiesAsync(written);

        var read = await _store.ReadEntitiesAsync();

        Assert.Equal(written, read);
    }

    [Fact]
    public void EntitiesPath_ReturnsCorrectPath()
    {
        Assert.EndsWith("entities.md", _store.EntitiesPath);
    }

    #endregion

    #region Append

    [Fact]
    public async Task AppendAsync_NewFile_CreatesFileWithContent()
    {
        var path = _store.ProfilePath;
        if (File.Exists(path)) File.Delete(path);

        await _store.AppendAsync(path, "## Новая секция\n\nКонтент.");

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("## Новая секция", content);
        Assert.Contains("Контент.", content);
    }

    [Fact]
    public async Task AppendAsync_ExistingFile_AppendsWithNewline()
    {
        var path = _store.ProfilePath;
        await _store.WriteProfileAsync("Original content");
        File.AppendAllText(path, "\n"); // ensure trailing newline

        await _store.AppendAsync(path, "Appended content");

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("Original content", content);
        Assert.Contains("Appended content", content);
    }

    #endregion

    #region Context

    [Fact]
    public async Task AppendContextAsync_NewFile_CreatesHeaderAndEntry()
    {
        var date = DateOnly.FromDateTime(DateTime.Now);
        var contextDir = Path.GetDirectoryName(_store.ProfilePath)!;

        await _store.AppendContextAsync("Сессия прошла успешно.", date);

        var path = Path.Combine(contextDir, $"context_{date:yyyy-MM-dd}.md");
        Assert.True(File.Exists(path), $"Expected {path} to exist");
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("Сессия прошла успешно", content);
    }

    [Fact]
    public async Task AppendContextAsync_ExistingFile_AppendsEntryNoHeader()
    {
        var date = DateOnly.FromDateTime(DateTime.Now);
        var contextDir = Path.GetDirectoryName(_store.ProfilePath)!;
        var path = Path.Combine(contextDir, $"context_{date:yyyy-MM-dd}.md");
        await File.WriteAllTextAsync(path, $"# Контекст за {date:yyyy-MM-dd}\n\n## Existing\n\nOld session.\n");

        await _store.AppendContextAsync("New session.", date);

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("Old session.", content);
        Assert.Contains("New session.", content);
        // The new session adds ## Сессия entry; the existing file had ## Existing (not ## Сессия)
        Assert.Equal(2, content.Split("##").Length - 1); // 2 sections total
    }

    [Fact]
    public async Task ReadLastContextAsync_NoFiles_ReturnsEmpty()
    {
        var result = await _store.ReadLastContextAsync();

        Assert.Equal("", result);
    }

    [Fact]
    public async Task ReadLastContextAsync_WithFiles_ReturnsNewestContent()
    {
        // Create two context files with different dates
        var oldDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-5));
        var newDate = DateOnly.FromDateTime(DateTime.Now);
        var oldPath = Path.Combine(Path.GetDirectoryName(_store.ProfilePath)!,
            $"context_{oldDate:yyyy-MM-dd}.md");
        var newPath = Path.Combine(Path.GetDirectoryName(_store.ProfilePath)!,
            $"context_{newDate:yyyy-MM-dd}.md");
        await File.WriteAllTextAsync(oldPath, "# Old context\n\nOld content.");
        await File.WriteAllTextAsync(newPath, "# New context\n\nNew content.");

        var result = await _store.ReadLastContextAsync();

        Assert.Contains("New content.", result);
    }

    #endregion

    #region Reset

    [Fact]
    public void Reset_WithFiles_DeletesAllMdFiles()
    {
        File.WriteAllText(_store.ProfilePath, "profile");
        File.WriteAllText(_store.PreferencesPath, "prefs");
        File.WriteAllText(_store.EntitiesPath, "entities");

        _store.Reset();

        Assert.False(File.Exists(_store.ProfilePath));
        Assert.False(File.Exists(_store.PreferencesPath));
        Assert.False(File.Exists(_store.EntitiesPath));
    }

    [Fact]
    public void Reset_NoDirectory_DoesNotThrow()
    {
        var dir = Path.GetDirectoryName(_store.ProfilePath)!;
        Directory.Delete(dir, true);

        var ex = Record.Exception(() => _store.Reset());

        Assert.Null(ex);
    }

    #endregion
}
