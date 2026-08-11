using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Hercules.Config;

/// <summary>
///     Хранилище "живой" конфигурации агента. Позволяет обновлять настройки
///     во время работы приложения без перезагрузки и сохранять их в JSON-файл.
///     Служит единым источником правды для всех компонентов агента.
/// </summary>
public sealed class RuntimeConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly ILogger<RuntimeConfigStore> _logger;
    private readonly object _lock = new();
    private volatile AppConfig _snapshot;

    public RuntimeConfigStore(AppConfig initial, string filePath, ILogger<RuntimeConfigStore> logger)
    {
        _snapshot = initial ?? throw new ArgumentNullException(nameof(initial));
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _logger = logger;
    }

    /// <summary>Текущий актуальный снимок конфигурации.</summary>
    public AppConfig Current => _snapshot;

    /// <summary>
    ///     Полностью заменить текущую конфигурацию, сохранить в файл и уведомить
    ///     подписчиков об изменении.
    /// </summary>
    public void Update(AppConfig next)
    {
        ArgumentNullException.ThrowIfNull(next);

        lock (_lock)
        {
            _snapshot = next;
            SaveLocked();
        }

        Changed?.Invoke(this, next);
    }

    /// <summary>
    ///     Частично обновить конфигурацию через патч (JSON Merge Patch-стиль).
    ///     Позволяет менять отдельные поля, не перезаписывая всю конфигурацию.
    /// </summary>
    public void Patch(JsonElement patch)
    {
        AppConfig merged;
        lock (_lock)
        {
            merged = PatchLocked(_snapshot, patch);
            _snapshot = merged;
            SaveLocked();
        }

        Changed?.Invoke(this, merged);
    }

    /// <summary>Событие изменения конфигурации. Подписчики могут перезагрузить зависимые ресурсы.</summary>
    public event EventHandler<AppConfig>? Changed;

    private void SaveLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_snapshot, JsonOptions);
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _filePath, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration to {FilePath}", _filePath);
        }
    }

    private static AppConfig PatchLocked(AppConfig current, JsonElement patch)
    {
        // Сериализуем текущий снимок и новый патч в JsonDocument, сливаем на уровне JSON,
        // затем десериализуем обратно в AppConfig. Просто, надёжно и не требует ручного кода.
        var currentJson = JsonSerializer.SerializeToElement(current, JsonOptions);
        var merged = MergeJson(currentJson, patch);
        return merged.Deserialize<AppConfig>(JsonOptions) ?? throw new InvalidOperationException("Patch produced null configuration");
    }

    private static JsonElement MergeJson(JsonElement current, JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object)
        {
            return patch.Clone();
        }

        var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        MergeObject(current, patch, writer);
        writer.WriteEndObject();
        writer.Flush();

        stream.Position = 0;
        using var merged = JsonDocument.Parse(stream);
        return merged.RootElement.Clone();
    }

    private static void MergeObject(JsonElement current, JsonElement patch, Utf8JsonWriter writer)
    {
        foreach (var property in current.EnumerateObject())
        {
            if (patch.TryGetProperty(property.Name, out var patchProperty))
            {
                if (patchProperty.ValueKind == JsonValueKind.Null)
                {
                    continue; // null removes the property
                }

                writer.WritePropertyName(property.Name);
                if (property.Value.ValueKind == JsonValueKind.Object && patchProperty.ValueKind == JsonValueKind.Object)
                {
                    writer.WriteStartObject();
                    MergeObject(property.Value, patchProperty, writer);
                    writer.WriteEndObject();
                }
                else
                {
                    patchProperty.WriteTo(writer);
                }
            }
            else
            {
                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }
        }

        foreach (var property in patch.EnumerateObject())
        {
            if (!current.TryGetProperty(property.Name, out _))
            {
                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }
        }
    }
}
