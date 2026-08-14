# Task 74 — NATS JetStream ack/fail implementation

**Phase:** 5
**Initiative:** 45
**Status:** pending
**Owner:** —
**Slug:** `nats-jetstream-ack`

## Goal
`NatsTaskQueue.AckAsync` и `FailAsync` (`Mesh/Backends/Nats/NatsTaskQueue.cs:266-278`) — no-ops (`await Task.CompletedTask`). JetStream ack/nak никогда не отправляется → сообщения redeliver'ятся бесконечно (до `MaxDeliver`/`AckWait`). Это критический баг: задача считается "обработанной", но остаётся в потоке и доставляется снова и снова. Дополнительно `MaxDeliver = 10` hardcoded.

## Acceptance criteria
### Sub-tasks
- [ ] `Mesh/Backends/Nats/NatsTaskQueue.cs:266-278` — реализовать `AckAsync`: получить `NatsJSMsg` по `receiptHandle` (или хранить `NatsJSMsg` в `QueuedTask` metadata), вызвать `msg.AckAsync()`.
- [ ] Реализовать `FailAsync`: вызвать `msg.NakAsync()` (для retry) или `msg.TermAsync()` (для DLQ после `MaxRetries`).
- [ ] Хранить JetStream message reference: `DequeueAsync` должен сохранять `NatsJSMsg` в in-flight map keyed by `receiptHandle` (ULID), чтобы `AckAsync`/`FailAsync` могли его найти.
- [ ] `Mesh/Backends/Nats/NatsTaskQueue.cs:145-148` — вынести `MaxDeliver = 10` в `NatsMeshConfig.MaxDeliveryAttempts` (default 10).
- [ ] Реализовать DLQ: после `MaxDeliveryAttempts` превышенных deliveries, `TermAsync()` + записать в локальный DLQ (файл или SQLite) для ручного requeue.
- [ ] `RequeueDeadLetterAsync` — переработать: переместить из локального DLQ обратно в JetStream stream (`PublishAsync`).
- [ ] Integration-тест (с тестовым NATS container или mock `INatsJSConsume`): `Enqueue` → `Dequeue` → `Ack` → сообщение не redeliver'ится.
- [ ] Integration-тест: `Enqueue` → `Dequeue` → `Fail` (retry < max) → `Nak` → redelivery происходит.
- [ ] Integration-тест: `Enqueue` → `Dequeue` → `Fail` (retry >= max) → `Term` → в DLQ.
- [ ] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Mesh/Backends/Nats/NatsTaskQueue.cs, src/agent/Mesh/Backends/Nats/NatsMeshConfig.cs, src/agent/Mesh/Abstractions/ITaskQueue.cs (metadata field)

## Dependencies
- блокирует / опирается на: [task_068 — nats-jetstream-transport](task_068.md)

## Risks / Rollback
NATS client API может отличаться между версиями; проверить совместимость с установленной `NATS.Net` package. Rollback: вернуть no-ops (но баг останется). Если NATS backend не используется в production — понизить priority.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)