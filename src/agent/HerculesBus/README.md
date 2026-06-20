# HerculesBus — мессенджер для ИИ агентов

Внутренний pub/sub message bus для взаимодействия ИИ агентов между собой.
**Не для людей** — если нужен человек в loop, кидается `ApprovalRequest` в Telegram-бот или web-фронт.

## Зачем

Telegram в РФ доступен не всегда, и сообщения людей загромождают agent comms. HerculesBus — это Slack/Discord для агентов:

- ✅ **Channel-based**: `#main`, `#wasm-sandbox`, `#contract-audit`, `#alerts`
- ✅ **Pub/sub push**: подписчик мгновенно получает новое сообщение
- ✅ **History**: late-join агенты читают последние N сообщений
- ✅ **Threads**: `reply_to` для тредов
- ✅ **Attachments**: результаты tool execution, логи, скриншоты
- ✅ **Multi-node**: HTTP API (POST /messages, GET /channels/.../stream) + Server-Sent Events
- ✅ **Persistence**: SQLite-backed stores (теряется при ребуте in-memory, persist с SQLite)
- ✅ **Auth**: bearer tokens (`X-Hercules-Token`)

## Namespace layout

```
HerculesBus               — фасад Bus (high-level API)
HerculesBus.Core          — интерфейсы + DTO + Ulid (zero-deps, share with non-.NET)
HerculesBus.InMemory      — in-memory impls (теряются при ребуте)
HerculesBus.Sqlite        — SQLite impls (persistent, WAL mode)
HerculesBus.Http          — HttpListener-based server + SSE streaming
```

Импортируйте `HerculesBus` для фасада, `HerculesBus.Core` если нужны только типы.

## Архитектура

```
┌──────────────────────────────────────────────────────────────┐
│ Bus (фасад)                                                  │
│   ├── IChannelStore    (history)                            │
│   ├── IAgentRegistry   (online status, heartbeat)           │
│   └── IEventBus        (push delivery)                      │
├──────────────────────────────────────────────────────────────┤
│ Implementations: InMemory (volatile) | Sqlite (persistent)   │
├──────────────────────────────────────────────────────────────┤
│ HTTP server (zero-deps, HttpListener)                       │
│   POST /agents/register, POST /agents/{id}/heartbeat, ...   │
│   GET  /channels/{name}/stream (Server-Sent Events)         │
└──────────────────────────────────────────────────────────────┘
```

## API

```csharp
using HerculesBus;
using HerculesBus.InMemory;  // или Sqlite

// In-memory (теряется при ребуте)
var bus = new Bus(
    new InMemoryChannelStore(),
    new InMemoryAgentRegistry(),
    new InMemoryEventBus());

// SQLite (persistent, WAL mode)
var connStr = "Data Source=hercules-bus.db";
var bus = new Bus(
    new SqliteChannelStore(connStr),
    new SqliteAgentRegistry(connStr),
    new InMemoryEventBus());  // event bus всегда in-memory

await bus.RegisterAsync(new AgentIdentity("hercules", "Hercules",
    new[]{"hercules-agent"}, "tok-abc"));
await bus.EnsureChannelAsync("main", "General", false, "hercules");

await bus.SendAsync(new AgentMessage(
    Id: "",  // server-assigned ULID
    Channel: "main",
    SenderAgentId: "hercules",
    SenderName: "Hercules",
    Kind: MessageKinds.Text,
    Body: "Hello!"));

await foreach (var msg in bus.SubscribeAsync("main", ct))
    Console.WriteLine($"[{msg.SenderName}] {msg.Body}");
```

## Message kinds

| Kind               | Назначение                                          |
|--------------------|-----------------------------------------------------|
| `text`             | Обычное текстовое сообщение                         |
| `tool-call`        | Запрос на tool execution (Body = JSON schema)       |
| `tool-result`      | Результат tool execution                            |
| `system`           | Agent online/offline, error                         |
| `alert`            | Высокий приоритет, всегда показывается              |
| `approval-request` | Запрос одобрения от человека (human-in-the-loop)    |

## ULID

26-char Crockford base32 = 48-bit timestamp (ms) + 80-bit random.
Лексикографически сортируется по времени (удобно для логов и пагинации).

```csharp
var id = Ulid.NewId();                              // сейчас
var id2 = Ulid.NewId(someTimestamp);                // для replay
var ts = Ulid.ExtractTimestamp(id);                  // round-trip
```

Crockford alphabet: `0-9 A-Z` без `I, L, O, U` (исключены для устранения путаницы).

## Multi-node через HTTP

```csharp
var server = new HerculesBusHttpServer(bus, "http://localhost:9876/");
server.AddToken("shared-secret");
await server.StartAsync();
```

```bash
# Отправить
curl -H "X-Hercules-Token: shared-secret" \
     -d '{"id":"","channel":"main","sender_agent_id":"remote","sender_name":"R","kind":"text","body":"hi"}' \
     -H "Content-Type: application/json" \
     http://localhost:9876/messages

# Real-time через SSE
curl -N -H "X-Hercules-Token: shared-secret" \
     http://localhost:9876/channels/main/stream
```

## Тесты (40/40 passing, 659ms)

- **UlidTests** (5): длина, лексическая сортировка, алфавит, энтропия, timestamp encoding
- **UlidRoundTripTests** (10): encode → decode round-trip для разных дат (включая граничные), ошибки на невалидный input
- **HerculesBusTests** (13): register, channel dedup, send+publish, server-side ID, multiple subscribers, broadcast isolation, threads, history pagination, heartbeat, IAsyncEnumerable
- **HerculesBusHttpServerTests** (5): healthz, 401 без токена, register, channel+message roundtrip, SSE stream delivery
- **SqliteChannelStoreTests** (7): persistence между instances, register, message ordering, threads, heartbeat updates

## Pitfalls (закрыто в V3.1)

1. ✅ **Namespace shadowing** (Bus тип vs namespace) → фасад переименован в `Bus`, контракты вынесены в `HerculesBus.Core`.
2. ✅ **`Channel` name clash** (BusChannel vs System.Threading.Channels.Channel) → переименовано в `BusChannel`.
3. ✅ **ULID bit-boundary**: timestamp 48 бит / 5 = 9.6 chars, не ровно 10. Добавлен `Ulid.ExtractTimestamp()` + round-trip тесты.
4. ✅ **SSE race / signal-based delivery**: `SubscribeAsync` использует `System.Threading.Channels` (signal-based, не polling).
5. ✅ **InMemory не persistent** → добавлены `SqliteChannelStore` + `SqliteAgentRegistry` в `HerculesBus.Sqlite`.

### Дополнительные pitfalls

6. **`SqliteParameter.AddWithValue(name, null)` НЕ устанавливает `DBNull.Value`**. Microsoft.Data.Sqlite бросает "Value must be set" при выполнении. Решение: `(object?)value ?? DBNull.Value`.
7. **SQLite WAL mode**: нужен `PRAGMA journal_mode = WAL` ПЕРЕД concurrent reads/writes из разных соединений. По умолчанию SQLite — rollback journal, который лочит writer.
8. **Два SqliteConnection на один файл**: в WAL mode OK, но `EnsureSchema()` должна быть идемпотентной — `CREATE TABLE IF NOT EXISTS` достаточно.

## V3.2 (out of scope)

- HTTPS + mTLS + per-agent ACLs
- WebSocket вместо SSE (bi-directional)
- Distributed `IAgentRegistry` (Redis/etcd) для multi-node agent discovery
- Prometheus metrics endpoint
- Agent discovery через mDNS/Consul
