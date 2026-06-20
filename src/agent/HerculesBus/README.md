# HerculesBus — мессенджер для ИИ агентов

Внутренний pub/sub message bus для взаимодействия ИИ агентов между собой.
**Не для людей** — если нужен человек в loop, кидается `ApprovalRequest` в Telegram-бот или другой фронт.

## Зачем

Сейчас агенты общаются через Telegram. Это неудобно:
- ❌ Telegram в РФ доступен не всегда
- ❌ Сообщения адресованы людям (контакты, чаты, личка)
- ❌ Нет структурированного канала для tool-call/tool-result
- ❌ Нет channel-based broadcast для observability

**HerculesBus** — это Slack/Discord для агентов:
- ✅ **Channel-based**: `#main`, `#wasm-sandbox`, `#contract-audit`, `#alerts`
- ✅ **Pub/sub push**: подписчик мгновенно получает новое сообщение
- ✅ **History**: late-join агенты читают последние N сообщений
- ✅ **Threads**: `reply_to` для тредов
- ✅ **Attachments**: результаты tool execution, логи, скриншоты
- ✅ **Multi-node**: HTTP API (POST /messages, GET /channels/.../stream) для remote агентов
- ✅ **SSE streaming**: real-time push через Server-Sent Events
- ✅ **Auth**: bearer tokens (X-Hercules-Token)

## Архитектура

```
┌──────────────────────────────────────────────────────────────┐
│ HerculesBus (фасад)                                         │
│   ├── IChannelStore    (history)                            │
│   ├── IAgentRegistry   (online status, heartbeat)           │
│   └── IEventBus        (push delivery)                      │
├──────────────────────────────────────────────────────────────┤
│ In-Memory implementations (V3.1, single-node)               │
│   ├── InMemoryChannelStore     — ConcurrentDictionary       │
│   ├── InMemoryAgentRegistry    — ConcurrentDictionary       │
│   └── InMemoryEventBus         — System.Threading.Channels  │
├──────────────────────────────────────────────────────────────┤
│ HTTP server (zero-deps, HttpListener)                       │
│   POST /agents/register        — register identity          │
│   POST /agents/{id}/heartbeat  — update LastSeen + status   │
│   GET  /agents                 — list agents                │
│   POST /channels               — create channel             │
│   GET  /channels               — list channels              │
│   POST /messages               — publish message            │
│   GET  /channels/{name}/recent — history (JSON)             │
│   GET  /channels/{name}/stream — SSE push                   │
│   GET  /healthz                — liveness                   │
└──────────────────────────────────────────────────────────────┘
```

## API

```csharp
using HerculesBus;
using HerculesBus.InMemory;

// Setup
var bus = new global::HerculesBus.HerculesBus(
    new InMemoryChannelStore(),
    new InMemoryAgentRegistry(),
    new InMemoryEventBus());

// Register local agent
await bus.RegisterAsync(new AgentIdentity(
    AgentId: "hercules",
    DisplayName: "Hercules",
    Roles: new[] { "hercules-agent" },
    Token: "tok-abc"));

// Ensure channel
await bus.EnsureChannelAsync("main", "General chat", isPrivate: false, createdBy: "hercules");

// Send message
await bus.SendAsync(new AgentMessage(
    Id: "",                                  // server-assigned ULID
    Channel: "main",
    SenderAgentId: "hercules",
    SenderName: "Hercules",
    Kind: MessageKinds.Text,
    Body: "Hello from Hercules!"));

// Subscribe via IAsyncEnumerable
await foreach (var msg in bus.SubscribeAsync("main", ct))
{
    Console.WriteLine($"[{msg.SenderName}] {msg.Body}");
}
```

## Message kinds

| Kind               | Назначение                                          |
|--------------------|-----------------------------------------------------|
| `text`             | Обычное текстовое сообщение                         |
| `tool-call`        | Запрос на tool execution (Body = JSON schema)       |
| `tool-result`      | Результат tool execution (Body = JSON, ReplyTo = id)|
| `system`           | Agent online/offline, error                         |
| `alert`            | Высокий приоритет, всегда показывается              |
| `approval-request` | Запрос одобрения от человека (human-in-the-loop)    |

## Использование с несколькими процессами

Запустите server в одном процессе:

```csharp
var server = new HerculesBusHttpServer(bus, "http://localhost:9876/");
server.AddToken("shared-secret");
await server.StartAsync();
```

Подключитесь из другого процесса:

```bash
curl -H "X-Hercules-Token: shared-secret" \
     -d '{"id":"","channel":"main","sender_agent_id":"remote-agent","sender_name":"Remote","kind":"text","body":"hi"}' \
     -H "Content-Type: application/json" \
     http://localhost:9876/messages
```

Или используйте Server-Sent Events для real-time:

```bash
curl -N -H "X-Hercules-Token: shared-secret" \
     http://localhost:9876/channels/main/stream
# event: message
# data: {"id":"01HZB0X...","channel":"main",...}
```

## Use cases в Hercules

1. **Hercules ↔ ContractAuditor bot**: contract-auditor публикует результаты аудита в `#contract-audit`, Hercules подписан и обновляет context.
2. **Multi-instance Hercules**: несколько instances синхронизируются через HerculesBus вместо общей SQLite (eventually consistent).
3. **Observability**: admin UI читает ВСЕ каналы через SSE stream и показывает в дашборде.
4. **Human-in-the-loop bridge**: Web UI подписан на `#approvals` и показывает кнопки approve/reject для `approval-request`.

## V3.2 (out of scope)

- SQLite-backed `IChannelStore` (persistence между перезапусками)
- Distributed registry (Redis/etcd)
- HTTPS + mTLS + per-agent ACLs
- WebSocket для bi-directional (вместо SSE для server-push)
- Prometheus metrics endpoint
- Agent discovery через mDNS/Consul

## Тесты (23/23 passing, 586ms)

- **UlidTests** (5): длина, лексическая сортировка, алфавит, энтропия, timestamp-encoding
- **HerculesBusTests** (13): register, channel create/dedup, send+publish, server-side ID, multiple subscribers, broadcast isolation, threads, history pagination, heartbeat, IAsyncEnumerable
- **HerculesBusHttpServerTests** (5): healthz, 401 без токена, register, channel+message roundtrip, SSE stream delivery

## Pitfalls

1. **Namespace shadowing**: `HerculesBus` (namespace) + `HerculesBus` (тип фасада) → использовать `global::HerculesBus.HerculesBus` в тестах.
2. **`Channel` конфликт**: мой `HerculesBus.Channel` (канал) перекрывает `System.Threading.Channels.Channel` → использовать полное имя `System.Threading.Channels.Channel<T>`.
3. **ULID encoder bit-boundary**: timestamp (48 бит) + random (80 бит) = 128 бит / 5 = 25.6 chars → 26 chars. Prefix из timestamp занимает ~9-10 chars, не ровно 10 — не тестируйте exact-prefix.
4. **SSE subscription race**: между `Subscribe()` и первым `Publish()` есть ~200ms гонка. В тестах делать `Task.Delay(200)` после подписки.
5. **InMemoryChannelStore не persistent**: теряется при перезапуске. Для production — SqliteChannelStore (V3.2).
