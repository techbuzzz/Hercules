# Task 105 — Workflow graph model + executor

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `workflow-graph-executor`
**Studio Stage:** 8

## Goal
Декларативная модель workflow-графа (nodes, edges, gateways) + executor/interpreter, который исполняет граф, драйвит `ITaskLifecycleProtocol` + `ITaskQueue` + `IMeshBus`.

## Acceptance criteria
- [ ] `WorkflowGraph` model:
  - Nodes: `StartNode`, `ServiceTaskNode`, `UserTaskNode`, `ConditionalNode`, `ParallelGatewayNode`, `ExclusiveGatewayNode`, `InclusiveGatewayNode`, `TimerEventNode`, `ErrorEventNode`, `EndNode`
  - Edges: `{from, to, condition?}`
  - Metadata: name, version, description, template
- [ ] `ServiceTaskNode`: { agentId (or capability), intent, payload, timeoutMs, retryPolicy }
- [ ] `UserTaskNode`: { question, inputType (text|file|approval|choice), choices[], timeoutMs }
- [ ] `ConditionalNode`: { expression, branches: {true: nodeId, false: nodeId} }
- [ ] `ParallelGatewayNode`: { branches: [nodeId[]], join: "all"|"any" }
- [ ] `TimerEventNode`: { durationMs }
- [ ] `ErrorEventNode`: { catchFrom: nodeId, redirect: nodeId }
- [ ] `WorkflowExecutor` — interpreter:
  - `ExecuteAsync(graph, input, cancellationToken)` → `WorkflowExecutionResult`
  - Algorithm: traverse graph from Start, execute nodes, follow edges
  - ServiceTask: create `IntentEnvelope` → `POST /api/mesh/intent` (or via ITransport) → wait response
  - UserTask: create `DelegatedTask` with `AwaitingInputContext` → long-poll → user input → continue
  - Conditional: evaluate expression → branch
  - ParallelGateway: fork parallel tasks → join (wait all or any)
  - Timer: wait durationMs
  - Error: catch → redirect
  - Persistence: save node states after each transition
  - Cancellation: support cancel → cleanup
- [ ] Expression evaluator: simple (field access, ==, !=, >, <, &&, ||) — no full DSL for MVP
- [ ] Unit tests: linear, conditional, parallel, user-task, timer, error scenarios
- [ ] `dotnet build` + `dotnet test` pass

## Dependencies
- task_104 (workflow-server project) — executor lives there
- task_106 (DelegatedTask persistence) — for UserTask
- task_107 (parent/child tasks) — for parallel gateways

## Scope / Likely files
src/workflow-server/Models/WorkflowGraph.cs, src/workflow-server/Executor/WorkflowExecutor.cs, src/workflow-server/Executor/ExpressionEvaluator.cs

## Links
- Studio Stage 8: [../EPIC_Hercules_Studio/tasks/stage_08_workflow.md](../EPIC_Hercules_Studio/tasks/stage_08_workflow.md)
- Backlog: [../backlog.md](../backlog.md)