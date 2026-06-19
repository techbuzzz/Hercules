using Hercules.Config;

namespace Hercules.Tools;

/// <summary>
///     MCP (Model Context Protocol) клиент (Stage 3).
///     Подключается к MCP-серверам из конфига (stdio / http транспорт)
///     и auto-регистрирует их tools в <see cref="ToolRegistry" />.
///
///     ВНИМАНИЕ: v0.3.0-preview NuGet пакета ModelContextProtocol предоставляет
///     только server-side API. Client-side SDK ещё не выпущен (ETA Q1-Q2 2026).
///     Этот класс — интерфейсная обёртка, готовая к подключению SDK сразу после релиза.
///     Сейчас McpClient отслеживает серверы из конфига, но не может реально с ними общаться.
///
///     TODO: заменить заглушку на реальный SDK-вызов когда выйдет client API.
/// </summary>
public sealed class McpClient
{
    private readonly McpConfig _cfg;
    private readonly List<McpServerConnection> _servers = new();
    private bool _initialized;

    public McpClient(McpConfig cfg)
    {
        _cfg = cfg;
    }

    /// <summary>Список подключённых серверов (для диагностики / shutdown).</summary>
    public IReadOnlyList<McpServerConnection> Servers => _servers;

    /// <summary>
    ///     Подключиться ко всем серверам из конфига.
    ///     Вызывается из DI startup. Сейчас — заглушка (логирует intent, не делает реальных вызовов).
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        _initialized = true;

        if (_cfg.Servers.Count == 0)
        {
            return;
        }

        foreach (var serverCfg in _cfg.Servers)
        {
            if (string.IsNullOrWhiteSpace(serverCfg.Name))
            {
                await Console.Error.WriteLineAsync("[MCP] Skipping server with empty name");
                continue;
            }

            // TODO: реальное подключение когда выйдет client SDK.
            // var client = await McpClient.CreateAsync(new StdioClientTransport(...));
            // var tools = await client.ListToolsAsync();
            // foreach (var tool in tools) registry.Add(new McpToolAdapter(serverCfg.Name, tool));
            var conn = new McpServerConnection(serverCfg.Name, serverCfg.Transport, isStub: true);
            _servers.Add(conn);
            await Console.Error.WriteLineAsync(
                $"[MCP] Registered server '{conn.Name}' (transport: {conn.Transport}). " +
                $"NOTE: ModelContextProtocol client SDK not yet available — stub mode.");
        }
    }

    /// <summary>Отключиться от всех серверов.</summary>
    public async Task ShutdownAsync()
    {
        foreach (var s in _servers)
        {
            try { await s.DisposeAsync(); } catch { /* best effort */ }
        }
        _servers.Clear();
    }
}

/// <summary>
///     Connection к одному MCP-серверу. Сейчас stub; в будущем — обёртка над McpClient.
/// </summary>
public sealed class McpServerConnection : IAsyncDisposable
{
    public string Name { get; }
    public string Transport { get; }
    public bool IsStub { get; }

    public McpServerConnection(string name, string transport, bool isStub)
    {
        Name = name;
        Transport = transport;
        IsStub = isStub;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
