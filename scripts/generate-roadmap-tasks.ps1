# ============================================================================
# generate-roadmap-tasks.ps1
# ----------------------------------------------------------------------------
# Генерирует docs/roadmap/backlog.md и 64 task_*.md файлов из embedded-массива
# задач. Идемпотентен: перезаписывает файлы при повторном запуске.
# ----------------------------------------------------------------------------
# Использование: pwsh -File scripts/generate-roadmap-tasks.ps1
# ============================================================================

[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path "$PSScriptRoot\..").Path,
    [string]$OutDir = "docs/roadmap",
    [string]$TasksDir = "docs/roadmap/tasks"
)

$ErrorActionPreference = 'Stop'

# ---------- Данные по 64 задачам ----------
# N, Phase, Id (slug), Title (RU), Goal, Scope, Deps, Risks

$Tasks = @(
    # ===== PHASE 1 (18) =====
    @{ N=1;  Phase=1; Id='core-agent-loop';           Title='Базовый цикл агента';
        Goal='AgentCore обрабатывает запрос: определяет применимый навык, опционально планирует ограниченные шаги инструментов, обновляет память, логирует исход.';
        Scope='src/agent/AgentCore.cs, src/agent/Loop/';
        Deps='';
        Risks='Неопределённый цикл при плохих skill matches; нужен явный max-steps + cancellation.' }

    @{ N=2;  Phase=1; Id='skill-lifecycle';           Title='Жизненный цикл навыков';
        Goal='Автоматизировать создание, версионирование, тестирование, оценку, улучшение, депрекацию и откат навыков; high-risk изменения остаются human-gated.';
        Scope='src/agent/Skills/SkillManager.cs, src/agent/Skills/Versioning/';
        Deps='1';
        Risks='Само-модификация навыков без approval; нужен явный policy gate.' }

    @{ N=3;  Phase=1; Id='hybrid-storage';            Title='Гибридное хранилище';
        Goal='Markdown/JSON для навыков и человеко-читаемой памяти; SQLite для сессий, оценок, метрик, бюджетов, аудита и устойчивого состояния задач. Без внешних зависимостей.';
        Scope='src/agent/Storage/, src/agent/Memory/';
        Deps='1';
        Risks='Без внешних зависимостей => сложная миграция схем; нужен плановая версионность.' }

    @{ N=4;  Phase=1; Id='multi-provider-llm';        Title='Мульти-провайдер LLM';
        Goal='YandexGPT, Ollama Cloud/Local, LM Studio и OpenAI-совместимые провайдеры через Microsoft.Extensions.AI с health-check, retry, fallback, capability detection.';
        Scope='src/agent/LLM/, src/agent/LLM/Providers/';
        Deps='1';
        Risks='Разные capabilities провайдеров ломают единые типы; нужны capability-флаги.' }

    @{ N=5;  Phase=1; Id='interfaces';                Title='Интерфейсы';
        Goal='CLI REPL, Telegram-бот, ASP.NET Core Minimal API и Astro web UI поверх одного application service и policy-слоя.';
        Scope='src/agent/CLI/, src/agent/Telegram/, src/agent/Hercules.WebApi/, src/hercules-web/';
        Deps='1,4';
        Risks='Дрейф UX между интерфейсами; единый presentation-слой обязателен.' }

    @{ N=6;  Phase=1; Id='tests-and-benchmarks';      Title='Тесты и бенчмарки';
        Goal='dotnet test покрывает ≥70%; dotnet run --benchmark измеряет skill hit rate, latency, токены, оценочную стоимость, успех инструментов, рост памяти.';
        Scope='tests/Hercules.Agent.Tests/, src/agent/CLI/Commands/BenchmarkCommand.cs';
        Deps='1,2,3';
        Risks='70% coverage трудно для LLM-кода; отделить pure-logic от side-effects.' }

    @{ N=7;  Phase=1; Id='typed-contracts';           Title='Типизированные контракты агента';
        Goal='Запросы, планы, вызовы и результаты инструментов, записи памяти и ответы используют версионированные JSON-схемы и C# типы; некорректный LLM-вывод чинится один раз или отклоняется.';
        Scope='src/agent/Contracts/, src/agent/LLM/JsonRepair/';
        Deps='1,4';
        Risks='Слишком жёсткие схемы ограничивают LLM; баланс между strict и permissive.' }

    @{ N=8;  Phase=1; Id='bounded-execution';         Title='Ограниченный цикл исполнения';
        Goal='Plan-act-observe с настраиваемыми max steps, wall-clock timeout, cancellation, recursion depth и per-request лимитом вызовов инструментов. Прямой ответ навыка остаётся fast-path.';
        Scope='src/agent/AgentCore.cs (limits), src/agent/Loop/';
        Deps='1,7';
        Risks='Слишком жёсткие лимиты ломают сложные задачи; нужны настраиваемые per-skill бюджеты.' }

    @{ N=9;  Phase=1; Id='tool-boundary-policy';      Title='Граница инструментов и policy engine';
        Goal='Локальные инструменты объявляют input/output схемы, side-effect level, required permission, timeout, retry, idempotency. ToolPolicy применяет allow/deny перед вызовом.';
        Scope='src/agent/Tools/, src/agent/Tools/Policy/';
        Deps='1,7';
        Risks='Сложные правила сложно отлаживать; нужен policy dry-run режим.' }

    @{ N=10; Phase=1; Id='approval-gates';            Title='Гейты подтверждения';
        Goal='Read-only операции идут автоматически; write/delete/network/shell/financial/hardware требуют policy-based подтверждения с показом proposed action оператору.';
        Scope='src/agent/Tools/Approval/, src/agent/Hercules.WebApi/Controllers/ApprovalController.cs';
        Deps='9';
        Risks='UX-фрикция в CLI/Web при частых подтверждениях; батчинг и доверенные scopes.' }

    @{ N=11; Phase=1; Id='layered-memory';            Title='Слоистая память';
        Goal='Разделить request context, short-lived session/working memory, durable facts и append-only episodic records. Memory writes имеют source, confidence, TTL, sensitivity.';
        Scope='src/agent/Memory/Layers/, src/agent/Memory/Metadata/';
        Deps='3';
        Risks='Утечка чувствительных данных в LLM-контекст; redaction-обязательна.' }

    @{ N=12; Phase=1; Id='budget-guardrails';         Title='Бюджеты и guardrails';
        Goal='Per-request и per-day лимиты на токены, стоимость, время, вызовы инструментов, диск и ретраи. Агент сообщает graceful degradation вместо тихого превышения.';
        Scope='src/agent/Budget/, src/agent/Hercules.WebApi/Controllers/BudgetController.cs';
        Deps='1,4,8';
        Risks='Жёсткие лимиты ломают сложные задачи; soft-warn + hard-cap.' }

    @{ N=13; Phase=1; Id='opentelemetry';             Title='Фундамент OpenTelemetry';
        Goal='ASP.NET, LLM, tool, skill и storage операции эмитят связанные traces, метрики и structured logs. Локальный console/file exporter по умолчанию; OTLP опционально.';
        Scope='src/agent/HostBuilderExtensions.cs, src/agent/Observability/';
        Deps='1';
        Risks='Стоимость трейсинга на долгих задачах; sampling-стратегия обязательна.' }

    @{ N=14; Phase=1; Id='audit-privacy';             Title='Аудит и приватность';
        Goal='Каждый side-effect и policy decision имеет audit record (actor, requestId, tool, permission, payload hash, result, timestamp). Конфигурируемая redaction для секретов и PII.';
        Scope='src/agent/Audit/, src/agent/Redaction/';
        Deps='9,11';
        Risks='Производительность записи в SQLite под нагрузкой; батчинг.' }

    @{ N=15; Phase=1; Id='secrets-config';            Title='Секреты и конфигурация';
        Goal='appsettings + environment variables; секреты никогда не пишутся в skill packages, Markdown memory, telemetry или export archives.';
        Scope='src/agent/Config/, src/agent/HostBuilderExtensions.cs';
        Deps='1';
        Risks='Случайная утечка в логи; централизованный redaction-фильтр.' }

    @{ N=16; Phase=1; Id='eval-harness';              Title='Оценка навыков (eval harness)';
        Goal='Каждый навык имеет deterministic fixtures и опциональные LLM-judge кейсы. Baseline записывается до promotion, регрессии блокируют автоматический rollout.';
        Scope='src/agent/Skills/Eval/, src/agent/CLI/Commands/SkillEvalCommand.cs';
        Deps='2,7';
        Risks='Flaky LLM-judge; нужны reproducibility-инварианты (temperature=0, seed).' }

    @{ N=17; Phase=1; Id='safe-self-improvement';     Title='Безопасное самоулучшение';
        Goal='Maintenance workflow анализирует анонимизированные failures и eval, предлагает versioned skill diff, тесты, ожидаемый gain и rollback plan. Не активирует свои изменения вне approval policy.';
        Scope='src/agent/Reflection/MaintenanceWorkflow.cs, src/agent/Reflection/ProposalDiffer.cs';
        Deps='2,14,16';
        Risks='Само-модификация без human gate; строгий two-person rule для prod-skills.' }

    @{ N=18; Phase=1; Id='durable-task-lifecycle';    Title='Устойчивый жизненный цикл задач';
        Goal='Долгие задачи имеют task ID, state transitions, cancellation, retry, resumable checkpoints, idempotency key. Синхронные chat-запросы остаются простыми.';
        Scope='src/agent/Tasks/, src/agent/Storage/SqliteSessionStore.cs';
        Deps='3,12';
        Risks='Сложность recovery-сценариев; обязательны chaos-тесты.' }

    # ===== PHASE 2 (13) =====
    @{ N=19; Phase=2; Id='skill-package-format';      Title='Формат пакета навыка';
        Goal='Папка навыка: skill.meta.json, skill.prompt.md, skill.tests.json, опционально tool.schema.json, examples.json и changelog.';
        Scope='src/agent/Skills/Package/, docs/skill-package-spec.md';
        Deps='2';
        Risks='Обратная совместимость при изменении формата; semver + миграции.' }

    @{ N=20; Phase=2; Id='skill-manifest';            Title='Манифест навыка и совместимость';
        Goal='skill.meta.json: ID, semver, owner, поддерживаемые версии Hercules, версии input/output схем, required tools, permissions, model requirements, risk level, бюджеты.';
        Scope='src/agent/Skills/Package/Manifest.cs';
        Deps='19';
        Risks='Schema drift; нужна валидация по версии схемы.' }

    @{ N=21; Phase=2; Id='skill-marketplace';         Title='Маркетплейс навыков';
        Goal='data/Skills/marketplace/: import/export CLI, integrity hashes, signed packages, разрешение зависимостей, public template repository.';
        Scope='src/agent/Skills/Marketplace/, src/agent/CLI/Commands/SkillMarketplaceCommand.cs';
        Deps='19,20';
        Risks='Supply chain; подпись пакетов обязательна для prod.' }

    @{ N=22; Phase=2; Id='semantic-routing';          Title='Семантическая маршрутизация';
        Goal='SkillRouter ранжирует применимые навыки по embedding similarity, lexical match, input-schema compatibility, историческому качеству, latency и policy eligibility.';
        Scope='src/agent/Skills/Routing/SkillRouter.cs, src/agent/Skills/Routing/Scoring/';
        Deps='2,16';
        Risks='Зависимость от embedding-провайдера; нужен deterministic fallback (см. задачу 23).' }

    @{ N=23; Phase=2; Id='deterministic-router';      Title='Детерминированный fallback маршрутизатора';
        Goal='No-embedding режим: tags, keyword triggers, declared input types; edge deployments остаются работоспособными offline и на ограниченных ресурсах.';
        Scope='src/agent/Skills/Routing/KeywordRouter.cs';
        Deps='22';
        Risks='Снижение качества маршрутизации; пользовательский override обязателен.' }

    @{ N=24; Phase=2; Id='tool-registry';             Title='Реестр инструментов';
        Goal='Навыки объявляют HTTP, FS, shell, DB, GPIO/MQTT и MCP-инструменты из data/Tools/; реестр хранит allow/deny, схемы, лимиты и health state.';
        Scope='src/agent/Tools/Registry/, src/agent/Tools/Source/';
        Deps='9';
        Risks='Безопасность динамической загрузки; подписанные sources.' }

    @{ N=25; Phase=2; Id='mcp-adapter';               Title='MCP-адаптер';
        Goal='Hercules может потреблять выбранные MCP-серверы и экспонировать подходящие инструменты через MCP-совместимый адаптер. Built-in tools остаются .NET-имплементациями без отдельного процесса.';
        Scope='src/agent/Mcp/, src/agent/Tools/McpAdapter/';
        Deps='24';
        Risks='Поверхность атаки MCP-серверов; strict capability allowlist.' }

    @{ N=26; Phase=2; Id='least-privilege-grants';    Title='Least-privilege grants';
        Goal='Навык получает только объявленные capabilities. Runtime проверяет grant при вызове; импорт навыка не может тихо расширить разрешения.';
        Scope='src/agent/Tools/Grants/, src/agent/Skills/Import/';
        Deps='9,20';
        Risks='Обратная совместимость со старыми навыками; миграционный режим.' }

    @{ N=27; Phase=2; Id='context-assembly';          Title='Сборка и сжатие контекста';
        Goal='ContextBuilder выбирает релевантную память, схемы инструментов, примеры и prior task state в рамках token-бюджета; завершённые tool traces сжимаются в episodic memory.';
        Scope='src/agent/Context/ContextBuilder.cs, src/agent/Context/Summarizer/';
        Deps='11,12';
        Risks='Потеря важного контекста при сжатии; importance-score.' }

    @{ N=28; Phase=2; Id='caching';                   Title='Кэширование';
        Goal='Deterministic tool results, embeddings, routing decisions и provider-supported prompt prefixes кэшируются с scope, TTL, invalidation и sensitivity-правилами.';
        Scope='src/agent/Cache/';
        Deps='22';
        Risks='Stale cache для sensitive данных; per-data-class TTL.' }

    @{ N=29; Phase=2; Id='skill-quality-score';       Title='Score качества навыка';
        Goal='Per-version метрики: acceptance rate, test score, user correction rate, fallback rate, latency, cost, safety denials. Promotion и routing используют score без монополии.';
        Scope='src/agent/Skills/Quality/SkillQualityScore.cs';
        Deps='14,16';
        Risks='Goodhart law; score — не единственный критерий promotion.' }

    @{ N=30; Phase=2; Id='agent-templates';           Title='Шаблоны агентов';
        Goal='Готовые бандлы skills/memory/policy/tools/eval/config для вертикалей: greenhouse, energy, cold-chain, server closet.';
        Scope='templates/greenhouse/, templates/cold-chain/, ...';
        Deps='19,21';
        Risks='Шаблон становится "магическим"; нужна явная доку ментация override-полей.' }

    @{ N=31; Phase=2; Id='template-simulation';       Title='Симуляция шаблонов';
        Goal='Шаблоны включают replayable sensor/event fixtures и failure scenarios; валидация без реального железа или внешних side-effects.';
        Scope='templates/*/sim/, src/agent/Simulation/';
        Deps='30';
        Risks='Сложность моделирования неисправностей; reuse реальных eval-фикстур.' }

    # ===== PHASE 3 (11) =====
    @{ N=32; Phase=3; Id='agent-manifest';            Title='Манифест агента';
        Goal='Каждый агент публикует agent.manifest.json: name, version, capabilities, skills, endpoint, auth, поддерживаемые версии протокола, resource limits, trust metadata.';
        Scope='src/agent/Mesh/Manifest/, src/agent/Hercules.WebApi/Controllers/ManifestController.cs';
        Deps='1,15';
        Risks='Расхождение манифеста и реальности; генерация из runtime-state.' }

    @{ N=33; Phase=3; Id='a2a-agent-card';            Title='A2A Agent Card совместимость';
        Goal='Hercules публикует и потребляет A2A-совместимую Agent Card с метаданными capabilities и skills, сохраняя нативный манифест для локальных deployment.';
        Scope='src/agent/Mesh/A2A/AgentCard.cs';
        Deps='32';
        Risks='A2A спецификация может дрейфовать; версионные адаптеры.' }

    @{ N=34; Phase=3; Id='capability-registry';       Title='Capability registry';
        Goal='Локальный файл или SQLite-реестр: известные агенты, capabilities, endpoint health, trust level, поддержка протоколов, cost/latency hints, expiry.';
        Scope='src/agent/Mesh/Registry/';
        Deps='32';
        Risks='Stale entries; health-check + TTL обязательны.' }

    @{ N=35; Phase=3; Id='delegation-envelope';       Title='Формат inter-agent сообщений';
        Goal='Версионированный JSON delegation envelope: requestId, traceId, idempotencyKey, sender, recipient, intent, typed payload, replyTo, deadline, auth context, requested response schema.';
        Scope='src/agent/Mesh/Envelope/, src/agent/Mesh/Schema/';
        Deps='7';
        Risks='Эволюция схемы; явные semver + миграции.' }

    @{ N=36; Phase=3; Id='task-lifecycle-protocol';  Title='Протокол жизненного цикла задач';
        Goal='Delegated задачи: accepted, working, awaiting-input, completed, failed, cancelled, expired; вызывающий может poll, subscribe или callback по возможностям transport.';
        Scope='src/agent/Mesh/TaskLifecycle/';
        Deps='18,35';
        Risks='Согласованность состояния при сбоях; idempotent transitions.' }

    @{ N=37; Phase=3; Id='transports';                Title='Опции транспорта';
        Goal='HTTP и gRPC first-class; опциональный адаптер для RabbitMQ, NATS и Azure Service Bus для асинхронных/disconnected сред.';
        Scope='src/agent/Mesh/Transport/';
        Deps='35';
        Risks='Разные гарантии доставки; абстракция + per-transport caveats.' }

    @{ N=38; Phase=3; Id='discovery';                 Title='Механизмы discovery';
        Goal='Static config, registry lookup и mDNS/Bonjour для deployment-specific discovery. Discovery сам по себе не выдаёт trust.';
        Scope='src/agent/Mesh/Discovery/';
        Deps='34';
        Risks='Spoofing discovery; trust policy отдельно.' }

    @{ N=39; Phase=3; Id='identity-delegation';       Title='Идентичность и делегация';
        Goal='mTLS, API-ключи или OAuth-совместимые bearer-токены для peer-вызовов. Delegated request несёт минимум identity claims и tool authority.';
        Scope='src/agent/Mesh/Auth/';
        Deps='15';
        Risks='Token leakage; короткоживущие токены + scope reduction.' }

    @{ N=40; Phase=3; Id='trust-admission';          Title='Trust и admission policy';
        Goal='Peer-агенты allow-listed по identity и capability. Вызовы отклоняются, когда intent, data classification, schema version, budget или risk level не разрешены.';
        Scope='src/agent/Mesh/Policy/';
        Deps='9,39';
        Risks='Слишком строго => false negatives; нужны dry-run отчёты.' }

    @{ N=41; Phase=3; Id='inter-agent-audit';         Title='Inter-agent audit trail';
        Goal='Каждая делегация записывает sender, receiver, intent, payload hash, data classification, policy decision, cost, latency, response hash, исход; связано по traceId.';
        Scope='src/agent/Mesh/Audit/';
        Deps='14,35';
        Risks='Размер логов; ротация и архив.' }

    @{ N=42; Phase=3; Id='protocol-tests';            Title='Контрактные и chaos тесты';
        Goal='Protocol fixtures проверяют обратную совместимость и обработку malformed messages; локальные test-агенты симулируют таймауты, дубли, недоступность и schema mismatch.';
        Scope='tests/Hercules.Agent.Tests/Mesh/';
        Deps='35,36,37';
        Risks='Сложно воспроизводимо; нужен deterministic test harness.' }

    # ===== PHASE 4 (10) =====
    @{ N=43; Phase=4; Id='mesh-router';               Title='Mesh router';
        Goal='Когда локальный навык недоступен, неприменим или ниже confidence threshold, router выбирает лучший trusted peer по capability, policy, health, latency, quality score и budget.';
        Scope='src/agent/Mesh/Router/';
        Deps='22,34,40';
        Risks='Скрытые предпочтения провайдера; явный scoring + observability.' }

    @{ N=44; Phase=4; Id='complexity-router';         Title='Сложность и стоимость';
        Goal='Дешёвое детерминированное правило или маленький классификатор выбирает: direct skill / small model / large model / one peer / fan-out. Решения измеримы и конфигурируемы.';
        Scope='src/agent/Mesh/Router/ComplexityRouter.cs';
        Deps='43';
        Risks='Сложность ML-классификатора; версия на rules + опциональный ML.' }

    @{ N=45; Phase=4; Id='fan-out-in';                Title='Fan-out / fan-in';
        Goal='Запрос уходит нескольким применимым агентам под строгим concurrency и budget. Ответы schema-validated, выбираются детерминированно, голосованием или опциональным judge.';
        Scope='src/agent/Mesh/Router/FanOut.cs, src/agent/Mesh/Aggregation/';
        Deps='43,44';
        Risks='Стоимость fan-out; строгие per-request лимиты.' }

    @{ N=46; Phase=4; Id='verification-pipeline';     Title='Verification pipeline';
        Goal='High-impact ответы проверяются независимым verifier skill, source policy, numeric validator или вторым агентом до возврата/действия.';
        Scope='src/agent/Mesh/Verification/';
        Deps='7,40';
        Risks='Verifier сам может ошибаться; meta-verification для критичных операций.' }

    @{ N=47; Phase=4; Id='retry-timeout-breaker';     Title='Retry, timeout, circuit breaker';
        Goal='Неудачные peer-вызовы используют deadline-aware retries, exponential backoff с jitter, bulkheads, rate limits и circuit breakers. Non-idempotent вызовы не ретраятся вслепую.';
        Scope='src/agent/Mesh/Resilience/';
        Deps='37,40';
        Risks='Retry storm; per-peer rate limit + breaker state observability.' }

    @{ N=48; Phase=4; Id='delegation-boundaries';     Title='Границы делегации';
        Goal='Mesh ограничивает hop count, fan-out width, суммарные tool calls, кумулятивную стоимость и время. Каждый агент может отклонить делегацию, превышающую его policy/capacity.';
        Scope='src/agent/Mesh/Budget/';
        Deps='12,40';
        Risks='Ложные отказы; явный reason code в ответе.' }

    @{ N=49; Phase=4; Id='human-escalation';          Title='Human-in-the-loop эскалация';
        Goal='Mesh эскалирует оператору неоднозначные, low-confidence, policy-sensitive, деструктивные и budget-exceeding операции с concise action plan и approval context.';
        Scope='src/agent/Mesh/Escalation/, src/hercules-web/src/components/EscalationPanel.astro';
        Deps='10,40';
        Risks='Operator fatigue; приоритизация и группировка эскалаций.' }

    @{ N=50; Phase=4; Id='distributed-reflection';    Title='Distributed reflection';
        Goal='Отчёты сравнивают производительность навыков и peer-ов, выявляют повторяющиеся сбои и предлагают кандидатов: skills, тесты, routing rules, peer relationships. Создают proposals, никогда не делают unreviewed prod changes.';
        Scope='src/agent/Mesh/Reflection/';
        Deps='17,41';
        Risks='Слишком много proposals; rate-limit + ranking.' }

    @{ N=51; Phase=4; Id='shared-memory-sync';        Title='Shared memory sync';
        Goal='Избранные факты и навыки синхронизируются только между trusted агентами: explicit namespaces, provenance, conflict resolution, TTL, encryption in transit, per-field data-classification policy.';
        Scope='src/agent/Mesh/SharedMemory/';
        Deps='11,14,40';
        Risks='Утечка чувствительных данных; data classification enforcement.' }

    @{ N=52; Phase=4; Id='mesh-eval-suite';           Title='Mesh evaluation suite';
        Goal='Воспроизводимые сценарии измеряют task success, safety denials, routing quality, latency, cost, resilience и деградацию при отказе peer/tool/LLM-провайдера.';
        Scope='tests/Hercules.Agent.Tests/Mesh/EvalSuite/, templates/mesh-eval/';
        Deps='42,45,47';
        Risks='Реалистичные сценарии дороги; shared scenario library.' }

    # ===== PHASE 5 (12) =====
    @{ N=53; Phase=5; Id='mesh-dashboard';            Title='Mesh dashboard';
        Goal='Web UI: live agent topology, traffic, health, policy denials, бюджеты, queued tasks, skill usage heatmap, последние eval results.';
        Scope='src/hercules-web/src/components/MeshTopology.astro, src/hercules-web/src/pages/mesh.astro';
        Deps='13,34';
        Risks='UI-сложность; phase-gated фичи с feature flags.' }

    @{ N=54; Phase=5; Id='centralized-observability'; Title='Централизованные логи и трейсы';
        Goal='Каждый запрос и inter-agent вызов имеет traceId; метрики, трейсы и redacted логи отгружаются через OTLP в Grafana/Loki, Jaeger или облачные сервисы.';
        Scope='src/agent/Observability/Otlp/';
        Deps='13,41';
        Risks='Sensitive PII в логах; централизованная redaction.' }

    @{ N=55; Phase=5; Id='security-ops';              Title='Security operations';
        Goal='Fleet-wide identity rotation, credential revocation, certificate renewal, проверка подписи пакетов, vulnerability reporting, security audit export.';
        Scope='src/agent/Security/';
        Deps='15,21,39';
        Risks='Сложность key management; интеграция с KMS.' }

    @{ N=56; Phase=5; Id='rate-limits-quotas';        Title='Rate limits и квоты';
        Goal='Per-agent, per-skill, per-user, per-tenant лимиты concurrency, calls, токенов, оценочной стоимости, storage, message volume.';
        Scope='src/agent/Mesh/Quotas/';
        Deps='12,48';
        Risks='Многоуровневые лимиты сложно отлаживать; явный report при отказе.' }

    @{ N=57; Phase=5; Id='lifecycle-management';      Title='Управление жизненным циклом';
        Goal='CLI и API: inventory, start, stop, drain, update, canary, health check, rollback, decommissioning агентов и skill packages.';
        Scope='src/agent/CLI/Commands/AgentLifecycleCommand.cs, src/agent/Hercules.WebApi/Controllers/LifecycleController.cs';
        Deps='1,15';
        Risks='Случайный downtime; canary + auto-rollback при health-degradation.' }

    @{ N=58; Phase=5; Id='config-policy-rollout';     Title='Configuration и policy rollout';
        Goal='Подписанные версионированные configuration и policy бандлы: staged rollout, local validation, expiry, rollback, last-known-good fallback.';
        Scope='src/agent/Config/Rollout/';
        Deps='15,55';
        Risks='Bad config rollout; staged + dry-run + monitoring.' }

    @{ N=59; Phase=5; Id='edge-provisioning';         Title='Edge provisioning';
        Goal='SD-card и Docker images для Raspberry Pi: first-boot Wi-Fi, identity enrolment, API key/cert activation, локальная инициализация storage, secure defaults.';
        Scope='images/raspberry-pi/, deploy/raspberry-pi/';
        Deps='15,55';
        Risks='Hardware-специфичные баги; CI matrix на реальных устройствах.' }

    @{ N=60; Phase=5; Id='offline-resilience';        Title='Offline resilience';
        Goal='Edge-агент буферизует sensor logs, task results и outgoing alerts через bounded local queues; возобновляет sync с deduplication и ordering при восстановлении связи.';
        Scope='src/agent/Offline/';
        Deps='3,18';
        Risks='Buffer overflow; явные приоритеты и TTL.' }

    @{ N=61; Phase=5; Id='local-degradation';         Title='Local-first degradation';
        Goal='Когда cloud LLM, peers или сеть недоступны, агент следует configured safe fallback: deterministic rules, local skills, reduced-capability models, queued work, operator notification.';
        Scope='src/agent/Degradation/';
        Deps='4,22,60';
        Risks='Неожиданная silent degradation; явный mode flag + observability.' }

    @{ N=62; Phase=5; Id='fleet-templates';           Title='Fleet templates';
        Goal='Один валидированный template на вертикаль: greenhouse, cold-chain, server closet, energy, vending — software, policy, тесты, мониторинг, offline behaviour, hardware BOM.';
        Scope='templates/greenhouse/, templates/cold-chain/, ...';
        Deps='30,31';
        Risks='Слишком специфично для общего ядра; чёткие границы template-vs-core.' }

    @{ N=63; Phase=5; Id='backup-recovery';           Title='Backup и recovery';
        Goal='Encrypted backups: configuration, skills, selected memory, SQLite data, device identity recovery. Restore-процедуры автоматизированы и протестированы.';
        Scope='src/agent/Backup/';
        Deps='3,55';
        Risks='Encryption key loss; recovery-of-keys обязателен.' }

    @{ N=64; Phase=5; Id='operational-slos';          Title='Операционные SLO';
        Goal='Каждый template объявляет availability, response-time, data-loss, recovery-time и cost objectives, alert thresholds и runbooks.';
        Scope='docs/slos/, templates/*/slo.md';
        Deps='54,57';
        Risks='SLO без автоматического enforcement; integration с 56/57.' }
)

# ---------- Шаблоны ----------

$Template = @'
# Task {N} — {Title}

**Phase:** {Phase}
**Status:** pending
**Owner:** —
**Slug:** `{Id}`

## Goal
{Goal}

## Acceptance criteria
- [ ] TBD при старте работы (декомпозиция в sub-tasks)

## Scope / Likely files
{Scope}

## Dependencies
{DepsBlock}

## Risks / Rollback
{Risks}

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
'@

# ---------- Генерация ----------

Write-Host "=== Hercules Roadmap generator ===" -ForegroundColor Cyan
Write-Host "Tasks: $($Tasks.Count)"
Write-Host "Out:   $Root/$OutDir"

$tasksFull = Join-Path -Path $Root -ChildPath $TasksDir
$backlogFull = Join-Path -Path $Root -ChildPath $OutDir
New-Item -Path $tasksFull -ItemType Directory -Force | Out-Null
New-Item -Path $backlogFull -ItemType Directory -Force | Out-Null

foreach ($t in $Tasks) {
    $depsBlock = if ($t.Deps) {
        $ids = ($t.Deps -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
        $links = $ids | ForEach-Object {
            $n = [int]$_
            $slug = ($Tasks | Where-Object { $_.N -eq $n }).Id
            "- блокирует / опирается на: [task_$('{0:D3}' -f $n) — $slug](task_$('{0:D3}' -f $n).md)"
        }
        ($links -join "`n")
    } else {
        "- нет (стартовая)"
    }

    $body = $Template `
        -replace '\{N\}', $t.N `
        -replace '\{Phase\}', $t.Phase `
        -replace '\{Title\}', $t.Title `
        -replace '\{Goal\}', $t.Goal `
        -replace '\{Scope\}', $t.Scope `
        -replace '\{DepsBlock\}', $depsBlock `
        -replace '\{Risks\}', $t.Risks `
        -replace '\{Id\}', $t.Id

    $filename = "task_$('{0:D3}' -f $t.N).md"
    $path = Join-Path -Path $tasksFull -ChildPath $filename
    Set-Content -Path $path -Value $body -Encoding UTF8
}

Write-Host "Generated $($Tasks.Count) task files in $tasksFull" -ForegroundColor Green

# ---------- Backlog.md (сводный) ----------

# Группировка по фазам
$phaseTitles = @{
    1 = "Phase 1 — Single autonomous unit (now → Q3 2026)"
    2 = "Phase 2 — Composable skills and tool use (Q4 2026)"
    3 = "Phase 3 — Inter-agent protocol (Q1 2027)"
    4 = "Phase 4 — Mesh orchestration (Q2 2027)"
    5 = "Phase 5 — IoT/edge fleet and mesh operations (Q3 2027)"
}

# Соберём таблицы фаз заранее (в $rows используем одинарные кавычки — backticks литералы)
$phaseBlocks = @()
foreach ($p in 1..5) {
    $rows = @()
    $rows += '## ' + $phaseTitles[$p]
    $rows += ''
    $rows += '| # | Task | Slug | Status |'
    $rows += '|---|------|------|--------|'
    $phaseTasks = $Tasks | Where-Object { $_.Phase -eq $p } | Sort-Object N
    foreach ($t in $phaseTasks) {
        $num = ('{0:D3}' -f $t.N)
        $rows += ('| {0} | [{1}](tasks/task_{2}.md) | `{3}` | pending |' -f $t.N, $t.Title, $num, $t.Id)
    }
    $phaseBlocks += ($rows -join "`n")
}
$phasesMarkdown = ($phaseBlocks -join "`n`n")

# StringBuilder-based generation (без here-string — избегаем проблем с backticks/escape)
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# Hercules — Roadmap Backlog')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('Это рабочий backlog по [ROADMAP-EN.md](../ROADMAP-EN.md) / [ROADMAP-RU.md](../ROADMAP-RU.md).')
[void]$sb.AppendLine('Каждой initiative соответствует отдельный `task_NNN.md` в [tasks/](tasks/).')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('> **Использование:** выбираем следующую `pending` задачу, переводим в `in_progress`,')
[void]$sb.AppendLine('> декомпозируем в sub-tasks, выполняем, переводим в `done`. См. раздел [Workflow](#workflow).')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## Содержание')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('- [Phase 1 — Single autonomous unit (now → Q3 2026)](#phase-1--single-autonomous-unit-now--q3-2026)')
[void]$sb.AppendLine('- [Phase 2 — Composable skills and tool use (Q4 2026)](#phase-2--composable-skills-and-tool-use-q4-2026)')
[void]$sb.AppendLine('- [Phase 3 — Inter-agent protocol (Q1 2027)](#phase-3--inter-agent-protocol-q1-2027)')
[void]$sb.AppendLine('- [Phase 4 — Mesh orchestration (Q2 2027)](#phase-4--mesh-orchestration-q2-2027)')
[void]$sb.AppendLine('- [Phase 5 — IoT/edge fleet and mesh operations (Q3 2027)](#phase-5--iotedge-fleet-and-mesh-operations-q3-2027)')
[void]$sb.AppendLine('- [Workflow](#workflow)')
[void]$sb.AppendLine('- [Roadmap cron](#roadmap-cron)')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('---')
[void]$sb.AppendLine('')
[void]$sb.AppendLine($phasesMarkdown)
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## Workflow')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('1. **Выбор следующей задачи:** из `pending` берём с наименьшим N, у которой выполнены зависимости.')
[void]$sb.AppendLine('2. **Декомпозиция:** открываем соответствующий `tasks/task_NNN.md`, заполняем раздел Acceptance criteria конкретными sub-tasks.')
[void]$sb.AppendLine('3. **Status flip:** в `tasks/task_NNN.md` правим `Status: pending` → `Status: in_progress` (commit).')
[void]$sb.AppendLine('4. **Работа:** реализуем, обновляем `Acceptance criteria` по мере выполнения (чекбоксы).')
[void]$sb.AppendLine('5. **Готово:** `Status: done` + ссылка на PR/commit + краткое описание реализованного поведения в конце файла.')
[void]$sb.AppendLine('6. **Блокер:** если застряли — `Status: blocked` + описание блокера в разделе Notes.')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('Правила:')
[void]$sb.AppendLine('- Не меняем Roadmap (вышестоящий документ) без обсуждения; backlog — рабочий артефакт.')
[void]$sb.AppendLine('- Соблюдаем [Design constraints](../ROADMAP-EN.md#design-constraints-for-a-micro-agent).')
[void]$sb.AppendLine('- Любая LLM-автономия ограничена [policy engine](../ROADMAP-EN.md#non-goals) и human-gate.')
[void]$sb.AppendLine('- Перед PR — `dotnet test` + ручной smoke + апдейт CHANGELOG.')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## Roadmap cron')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('См. `scripts/roadmap-cron.md` — ежедневный self-reminder, который:')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('- сканирует `docs/roadmap/tasks/`,')
[void]$sb.AppendLine('- находит первую `pending` задачу с выполненными зависимостями,')
[void]$sb.AppendLine('- отправляет в текущую сессию prompt на декомпозицию и старт работы,')
[void]$sb.AppendLine('- удаляется сам, если все задачи в `done` или явный `blocked`.')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('Создаётся через `mavis cron create` (см. секцию Setup ниже).')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('### Setup')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('```')
[void]$sb.AppendLine('# Из корня репозитория')
[void]$sb.AppendLine('mavis cron create --name roadmap-driver --schedule "0 9 * * *" --prompt "Roadmap tick: просканируй docs/roadmap/tasks/, выбери первую pending задачу с выполненными зависимостями, переведи её в in_progress, декомпозируй в sub-tasks и приступай. Если все done — выведи отчёт и попроси удалить cron. Ссылка: docs/roadmap/backlog.md" --timezone "Europe/Berlin"')
[void]$sb.AppendLine('```')
[void]$sb.AppendLine('')

$backlogPath = Join-Path -Path $backlogFull -ChildPath 'backlog.md'
Set-Content -Path $backlogPath -Value $sb.ToString() -Encoding UTF8

Write-Host "Generated $backlogPath" -ForegroundColor Green
Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
$byPhase = $Tasks | Group-Object Phase | Sort-Object Name
foreach ($g in $byPhase) {
    Write-Host ("  Phase {0}: {1} tasks" -f $g.Name, $g.Count)
}
Write-Host ("  TOTAL: {0} tasks" -f $Tasks.Count)
