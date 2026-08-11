namespace Hercules.Skills;

/// <summary>
///     Провайдер embedding-векторов для семантической маршрутизации навыков.
///     Превращает текст (запрос пользователя или описание навыка) в вектор фиксированной размерности.
///     Реализации: StubEmbeddingProvider (offline, hash-based), YandexEmbeddingProvider, OllamaEmbeddingProvider.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Имя провайдера (для логов и диагностики).</summary>
    string Name { get; }

    /// <summary>Размерность вектора (зависит от модели).</summary>
    int Dimensions { get; }

    /// <summary>
    ///     Получить embedding-вектор для текста.
    ///     Никогда не бросает — при ошибке возвращает нулевой вектор (маршрутизатор fallback на keyword-matching).
    /// </summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}

/// <summary>
///     Stub-реализация: детерминированный hash-based embedding без внешних зависимостей.
///     Не даёт настоящей семантической близости, но позволяет тестировать маршрутизатор
///     и работать offline. В продакшене заменяется на YandexEmbeddingProvider или OllamaEmbeddingProvider.
///     Алгоритм: каждый токен → hash → позиция в векторе; суммирование с весом.
/// </summary>
public sealed class StubEmbeddingProvider : IEmbeddingProvider
{
    public string Name => "stub-hash";
    public int Dimensions => 256;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(new float[Dimensions]);
        }

        var vector = new float[Dimensions];
        var tokens = text.ToLowerInvariant()
            .Split([' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            var hash = HashString(token);
            // Позиция в векторе — hash % Dimensions
            var pos = (int)(hash % (uint)Dimensions);
            // Знак и вес — из другого части hash
            var signBit = (hash >> 63) & 1;
            var weight = ((hash >> 56) & 0x7F) / 127.0f; // 0..1
            vector[pos] += signBit == 0
                ? weight
                : -weight;
        }

        // L2-нормализация
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return Task.FromResult(vector);
    }

    private static ulong HashString(string s)
    {
        unchecked
        {
            var hash = 14695981039346656037UL; // FNV offset
            foreach (var c in s)
            {
                hash = (hash ^ c) * 1099511628211UL; // FNV prime
            }

            return hash;
        }
    }
}
