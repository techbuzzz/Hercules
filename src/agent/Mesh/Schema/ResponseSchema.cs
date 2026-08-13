using System.Text.Json.Serialization;

namespace Hercules.Mesh.Schema;

/// <summary>
///     Запрошенная схема ответа — позволяет sender'у указать, какой формат ответа ожидается.
///     Это помогает receiver'у корректно сериализовать ответ и позволяет прокси/middleware
///     валидировать соответствие.
/// </summary>
public sealed class ResponseSchema
{
    /// <summary>Текущая версия схемы.</summary>
    public const string CurrentVersion = "1.0";

    /// <summary>
    ///     Тип схемы: "json" | "text" | "xml" | "binary".
    ///     Определяет, какой формат тела ответа ожидается.
    /// </summary>
    [JsonPropertyName("schema_type")]
    public string SchemaType { get; set; } = "json";

    /// <summary>
    ///     JSON Schema (Draft-07) или null, если schema_type=text/binary.
    ///     Пример: "{ "type": "object", "properties": { "answer": { "type": "string" } } }"
    /// </summary>
    [JsonPropertyName("json_schema")]
    public string? JsonSchema { get; set; }

    /// <summary>
    ///     MIME content type ожидаемого ответа.
    ///     Пример: "application/json", "text/plain", "application/xml".
    /// </summary>
    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    ///     Версия схемы (semver).
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = CurrentVersion;

    /// <summary>
    ///     Создать JSON schema с указанным примером.
    /// </summary>
    public static ResponseSchema Json(string? jsonSchema = null)
    {
        return new ResponseSchema
        {
            SchemaType = "json",
            JsonSchema = jsonSchema,
            ContentType = "application/json"
        };
    }

    /// <summary>
    ///     Создать text schema.
    /// </summary>
    public static ResponseSchema Text()
    {
        return new ResponseSchema
        {
            SchemaType = "text",
            JsonSchema = null,
            ContentType = "text/plain"
        };
    }
}
