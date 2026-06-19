# Hercules v2 — Code Execution Loop + Multi-Role Routing

**Branch:** `feat/agent-v2-code-execution`
**Base:** `origin/main` @ `4dd0690`
**Owner:** Victor V (techbuzzz)
**Started:** 2026-06-19

---

## Главная цель

Расширить Hercules self-improving микроагент (C# 15 / .NET 10) возможностями:
1. **Multi-role LLM routing** — главный агент делегирует подзадачи специализированным ролям (CodeWriter, Reflector и т.д.)
2. **Safe code execution** — генерирует и запускает C# код в sandbox (SecOps-grade)
3. **Tool ecosystem** — HTTP, MCP, A2A нативная поддержка
4. **PhraseReceiver naming** — переименование `Triggers` → `PhraseReceivers` (human-friendly)

---

## Stage 1 — Foundations (PhraseReceiver + Role routing)

### 1.1 PhraseReceiver rename
- [ ] `Skill.PhraseReceivers` (новое свойство, основное)
- [ ] Backward-compat read: `skill.{id}.v{N}.md` YAML front-matter `triggers:` → `phrase_receivers:`
- [ ] `SkillRouter.Route()` — использовать `PhraseReceivers`
- [ ] `SkillManager.CreateAsync` — генерировать `phrase_receivers` в JSON от LLM
- [ ] `SkillManager.CreateManual` / `UpdateManual` — параметр `phraseReceivers` (новый), `triggers` (back-compat)
- [ ] YAML front-matter parser: читать оба ключа, приоритет у `phrase_receivers`
- [ ] Файлы навыков: переписать front-matter `triggers:` → `phrase_receivers:` (auto-migration при load)
- [ ] **Тесты:** routing works with phrase_receivers, old triggers still read, migration

### 1.2 Multi-role routing
- [ ] `AppConfig.Roles` секция (`main`, `code_writer`, `reflector`)
- [ ] `ILLMClient.CompleteAsync(role, messages, ct)` — overload с role
- [ ] `LlmClientFactory.GetClient(role)` — возвращает правильный клиент
- [ ] `RoleRouter` — выбирает role по контексту
- [ ] Backward compat: `CompleteAsync(messages, ct)` = `role="main"`
- [ ] **Тесты:** role routing returns right provider, missing role → fallback to "main"

### 1.3 Deliverable
- `feat/agent-v2-code-execution` push
- Tests pass, build clean
- Commit: `stage(1): rename Triggers → PhraseReceivers + multi-role routing`

---

## Stage 2 — Sandbox + Code Execution

### 2.1 ICodeExecutor interface
- [ ] `src/agent/CodeExecution/ICodeExecutor.cs`
- [ ] `ExecutionRequest` (code, language, timeoutMs, memoryLimitMb, workingDir)
- [ ] `ExecutionResult` (stdout, stderr, exitCode, durationMs, killedReason)

### 2.2 SandboxOptions
- [ ] Default: timeout=30s, maxOutputBytes=10MB, maxFileSize=10MB, maxOpenFiles=50, maxProcesses=20
- [ ] `BlockedHosts` для outbound network (если нужно)
- [ ] Temp dir per execution: `~/.hercules/sandbox/{session_id}/{exec_id}/`
- [ ] Cleanup после выполнения (TTL 1 час)

### 2.3 DangerousCodeScanner
- [ ] Regex patterns: `rm -rf`, `Format-Volume`, `Remove-Item -Recurse -Force`
- [ ] `System.IO.File.Delete`, `Directory.Delete(..., true)`
- [ ] `System.Diagnostics.Process.Start` с shell-аргументами
- [ ] `Invoke-Expression`, `eval(`, `exec(`
- [ ] Whitelist для легитимного: `File.ReadAllText`, `File.WriteAllText`, `Math.Sqrt`, etc.
- [ ] `ScanResult` с найденными нарушениями + line numbers

### 2.4 DotnetFileBasedExecutor
- [ ] Использует `dotnet run --file code.cs`
- [ ] `ProcessStartInfo` с `RedirectStandardOutput/Error`, `CreateNoWindow=true`
- [ ] Resource limits: `ulimit` через `prlimit` (Linux) / Job Objects (Win)
- [ ] Temp `DOTNET_CLI_HOME`, `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`
- [ ] Pre-execution scan → если violation → return error без запуска
- [ ] Timeout enforcement через `Process.WaitForKill(timeoutMs)`

### 2.5 CodeExecutionSkill (preset)
- [ ] `skill.code-execution.v1.md` с PhraseReceivers `["напиши код", "запусти", "выполни код", "execute"]`
- [ ] Prompt обучает LLM: как генерировать C# для sandbox, как читать результат
- [ ] Регистрируется в `SkillManager` при первом запуске (если отсутствует)

### 2.6 Deliverable
- Build clean, integration test: `File.WriteAllText` works, `File.Delete` blocked
- Commit: `stage(2): sandboxed code execution (regex + ulimit + file-based apps)`

---

## Stage 3 — HTTP + MCP + A2A

### 3.1 ToolRegistry
- [ ] `src/agent/Tools/ITool.cs` (Name, Description, ExecuteAsync)
- [ ] `ToolRegistry` — DI singleton, регистрация по `ITool`
- [ ] LLM prompt injection: list available tools

### 3.2 HttpTool
- [ ] `GET`, `POST`, `PUT`, `DELETE` methods
- [ ] Allow-list domains из config (`Http.AllowedDomains`, default `["*"]`)
- [ ] Rate limit: 60 req/min
- [ ] Timeout: 10s default
- [ ] Audit log в SQLite

### 3.3 McpClient
- [ ] NuGet: `ModelContextProtocol` (latest stable)
- [ ] `McpClient` обёртка — connect, list tools, invoke
- [ ] Config: `Mcp.Servers` список с stdio/HTTP transport
- [ ] Tools auto-registered в ToolRegistry

### 3.4 A2AClient (Agent-to-Agent)
- [ ] Минимальный JSON-RPC 2.0 client
- [ ] `A2A.SubmitTask`, `GetTask`, `CancelTask`
- [ ] Config: `A2A.Endpoints` список
- [ ] Tool: `a2a_delegate`

### 3.5 Preset skills
- [ ] `skill.http-call.v1.md` (PhraseReceivers: `["http", "api call", "fetch"]`)
- [ ] `skill.mcp-call.v1.md` (PhraseReceivers: `["mcp", "tool call"]`)
- [ ] `skill.a2a-delegate.v1.md` (PhraseReceivers: `["delegate", "ask agent", "a2a"]`)

### 3.6 Deliverable
- HTTP tool works, MCP stdio server works (тест с `mcp-server-filesystem` если доступен)
- A2A mock server test
- Commit: `stage(3): HTTP + MCP + A2A tool ecosystem`

---

## Stage 4 — Integration + Reflection

### 4.1 AgentCore new flow
- [ ] В `HandleAsync` — после Router, вызвать `RoleRouter` для main роли
- [ ] В system prompt — секция "Available tools" с `ToolRegistry.ListForLLM()`
- [ ] LLM может вернуть `Action` enum: `respond` | `execute_code` | `http_call` | `mcp_call` | `a2a_delegate`
- [ ] AgentCore парсит action, вызывает соответствующий tool, кладёт результат в transcript, вызывает LLM снова для финального ответа
- [ ] Max iteration: 3 tool calls per turn (защита от infinite loop)

### 4.2 ReflectionEngine updates
- [ ] Анализировать code execution traces (success/failure/exceptions)
- [ ] Если `execution_failure_rate > 0.5` за последние 5 выполнений → propose skill improvement
- [ ] HTTP 4xx/5xx rate → propose обновить http-call skill

### 4.3 Sandbox audit в SQLite
- [ ] `sandbox_executions` table: id, session_id, code_hash, exit_code, duration_ms, blocked_patterns
- [ ] Query в Admin: последние 20 выполнений

### 4.4 Deliverable
- E2E test: "fetch weather" → main → http_call → reflect
- "compute factorial" → main → code_execution → reflect
- Commit: `stage(4): agent v2 flow + tool-aware reflection`

---

## Финальные шаги

- [ ] CHANGELOG-RU.md + CHANGELOG-EN.md обновить
- [ ] README sections: code execution, tools, MCP
- [ ] PR description с примерами
- [ ] `gh pr create --base main --head feat/agent-v2-code-execution`

---

## Точки отката

Каждый этап — отдельный commit. Если что-то пошло не так:
- `git log feat/agent-v2-code-execution` — найти последний green commit
- `git reset --hard <commit>` — откатить текущий stage
- `git push --force` — sync remote

## Verification (каждый stage)

```bash
cd ~/workspace/Hercules/src/agent
dotnet build
dotnet test  # если есть тесты
dotnet run   # smoke test CLI
```

## Open Risks

| Risk | Mitigation |
|------|------------|
| `dotnet run --file` cold start = 2-5s | Cache warm-up, keep NDK warm via long-lived process pool |
| Regex blacklist = bypass by obfuscation | v1 достаточно для safe-by-default, v2 = full AST analysis |
| MCP stdio = child process management | Использовать `Microsoft.Extensions.Hosting` BackgroundService |
| A2A spec draft = breaking changes | Pin client version, abstract behind interface |
| Role routing latency | 1 LLM call для main + N для подзадач, accept overhead |
