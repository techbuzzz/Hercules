---
id: mcp-call
name: MCP Tool Call
version: 1
phrase_receivers:
  - "mcp"
  - "tool call"
  - "mcp tool"
  - "model context protocol"
description: |
  Вызвать tool из подключённого MCP-сервера. MCP (Model Context Protocol) — стандарт
  для интеграции с внешними инструментами (filesystem, git, databases, etc.).
  ВНИМАНИЕ: ModelContextProtocol client SDK ещё не выпущен (ETA Q1-Q2 2026),
  сейчас — stub mode. Эта инструкция заработает сразу после выхода SDK.
---

# MCP Tool Call Skill

Когда пользователь хочет **вызвать tool из MCP-сервера** (после выхода client SDK)
используй tool `mcp.<server>.<tool>`.

## Конфигурация

```json
"Mcp": {
  "Servers": [
    {
      "Name": "filesystem",
      "Transport": "stdio",
      "Command": "mcp-server-filesystem",
      "Args": ["/home/user/documents"]
    }
  ]
}
```

## Статус

⚠️ ModelContextProtocol NuGet 0.3.0-preview содержит только server-side API.
Client SDK в разработке. Сейчас `McpClient.InitializeAsync` логирует intent
и создаёт stub-connection, но реальных вызовов не делает.

## Когда выйдет SDK

Заменить `McpClient.ConnectServerAsync` на реальный SDK-вызов:

```csharp
var transport = new StdioClientTransport(new() { Name = cfg.Name, Command = cfg.Command, Arguments = cfg.Args });
var client = await McpClient.CreateAsync(transport);
var tools = await client.ListToolsAsync();
foreach (var tool in tools)
{
    registry.Add(new McpToolAdapter(cfg.Name, tool));  // requires mutable registry
}
```

ToolRegistry нужно сделать mutable (Add(name, tool) после construction)
или менять DI для late-binding.

## Pitfalls

- ❌ Не надейся на MCP пока SDK не вышел
- ❌ Не конфигурируй серверы без знания их endpoint/command
- ✅ Следи за https://github.com/modelcontextprotocol/csharp-sdk
