using System.Text.Json;

namespace Hercules.WorkflowServer.Models;

/// <summary>
///     Определение workflow (task_104, task_105). На данном этапе — это обёртка
///     над произвольным JSON-графом. Executor (task_105) будет парсить
///     <see cref="GraphJson"/> в <c>WorkflowGraph</c> с типизированными узлами
///     (StartNode, ServiceTaskNode, ConditionalNode, ParallelGateway, etc.).
/// </summary>
public sealed class WorkflowDefinition
{
    /// <summary>Стабильный идентификатор (UUID v4, генерируется на сохранении).</summary>
    public string Id { get; set; } = "";

    /// <summary>Человекочитаемое имя.</summary>
    public string Name { get; set; } = "";

    /// <summary>Семантическая версия (по умолчанию 1).</summary>
    public int Version { get; set; } = 1;

    /// <summary>Опциональное описание.</summary>
    public string? Description { get; set; }

    /// <summary>JSON-граф (узлы + рёбра) в свободной форме (task_105 определит формальную схему).</summary>
    public JsonElement GraphJson { get; set; }

    /// <summary>Когда создано (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Когда обновлено (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Запрос на сохранение/обновление workflow definition (POST/PUT /api/workflows).</summary>
public sealed class SaveWorkflowRequest
{
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public string? Description { get; set; }
    public JsonElement GraphJson { get; set; }
}

/// <summary>Сводка о workflow definition (для list-endpoint, без тяжёлого graphJson).</summary>
public sealed class WorkflowSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Запрос на запуск workflow (POST /api/workflows/{id}/run).</summary>
public sealed class RunWorkflowRequest
{
    /// <summary>Опциональные входные данные для Start-узла графа.</summary>
    public JsonElement? Input { get; set; }
}

/// <summary>Ответ на запуск — пока stub (task_105 реализует реальный execution).</summary>
public sealed class WorkflowExecutionDto
{
    public string ExecutionId { get; set; } = "";
    public string WorkflowId { get; set; } = "";
    public string Status { get; set; } = "pending";
    public DateTimeOffset StartedAt { get; set; }
}
