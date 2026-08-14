# Task 107 — Parent/child task relationships

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `parent-child-tasks`
**Studio Stage:** 8

## Goal
Добавить parent/child связи между DelegatedTask для поддержки sub-processes и parallel gateway fan-out/join.

## Acceptance criteria
- [ ] `DelegatedTask` — add `ParentTaskId: string?` and `ChildTaskIds: string[]`
- [ ] `DurableTask` — add `ParentTaskId: string?` and `ChildTaskIds: string[]`
- [ ] `TaskLifecycleProtocol`:
  - `CreateChildTask(parentId, ...)` → create child, link to parent
  - `GetChildren(parentId)` → list child tasks
  - `GetParent(childId)` → parent task
  - When child completes → notify parent → parent evaluates join condition
- [ ] Parallel gateway join:
  - Parent task creates N child tasks (fan-out)
  - Parent waits for all children (join=all) or any child (join=any)
  - When join satisfied → parent continues to next node
- [ ] Sub-process (call activity):
  - ServiceTask creates child workflow (child task = sub-process)
  - Parent waits for child completion
  - Child result → parent input
- [ ] SQLite schema: add `parent_task_id` column
- [ ] Unit tests: create child, link, join-all, join-any, sub-process
- [ ] `dotnet build` + `dotnet test` pass

## Dependencies
- task_106 (DelegatedTask persistence)
- task_105 (workflow executor — uses parent/child for gateways)

## Scope / Likely files
src/agent/Mesh/TaskLifecycle/DelegatedTask.cs, src/agent/Tasks/Models.cs, src/agent/Mesh/TaskLifecycle/TaskLifecycleProtocol.cs

## Links
- Studio Stage 8: [../EPIC_Hercules_Studio/tasks/stage_08_workflow.md](../EPIC_Hercules_Studio/tasks/stage_08_workflow.md)
- Backlog: [../backlog.md](../backlog.md)