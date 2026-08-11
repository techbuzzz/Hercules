using System.Text;
using Hercules.Agent;
using Hercules.Mesh;
using Hercules.Skills;
using Hercules.Storage;
using Spectre.Console;

namespace Hercules.CLI;

/// <summary>
///     REPL-интерфейс командной строки (primary). Реализует команды из ТЗ:
///     прямой ввод, /skills, /skills create, /skills improve, /memory show,
///     /memory reset, /reflect, /exit, /skills export, /skills import,
///     /marketplace, /templates, /mesh (Phase 3 + 4).
/// </summary>
public sealed class ConsoleUI(
    AgentCore agent,
    SkillManager skills,
    MemoryManager memory,
    ReflectionEngine reflection,
    SkillPackager packager,
    SkillMarketplace marketplace,
    AgentTemplateManager templates,
    AgentManifestService manifestService,
    CapabilityRegistry capabilityRegistry,
    IntentRouter intentRouter,
    MeshRouter meshRouter,
    CircuitBreaker circuitBreaker,
    DistributedReflection distributedReflection,
    SharedMemorySync sharedMemorySync)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        agent.StartSession();
        PrintBanner();

        while (!ct.IsCancellationRequested)
        {
            var input = ReadInput();
            if (input is null)
            {
                break; // EOF (например, при перенаправлении ввода)
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            try
            {
                if (input.StartsWith('/'))
                {
                    var exit = await HandleCommandAsync(input.Trim(), ct);
                    if (exit)
                    {
                        break;
                    }
                }
                else
                {
                    await HandleChatAsync(input, ct);
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Ошибка:[/] {ex.Message}");
            }
        }
    }

    private async Task HandleChatAsync(string input, CancellationToken ct)
    {
        AgentResponse resp = null!;
        await AnsiConsole.Status().StartAsync("Думаю...", async _ => { resp = await agent.HandleAsync(input, ct); });

        var color = resp.Confidence switch
        {
            "high" => "green",
            "low" => "red",
            _ => "yellow"
        };
        var tag = resp.Mode == "skill"
            ? $"навык: {resp.UsedSkill?.Meta.Name}"
            : "direct";

        AnsiConsole.MarkupLineInterpolated($"[blue]Hercules[/] [grey]({tag} · {resp.Provider} · [/][{color}]conf={resp.Confidence}[/][grey])[/]:");
        AnsiConsole.WriteLine(resp.Answer);
        AnsiConsole.WriteLine();

        // Human-in-the-loop: предложение создать навык
        if (resp.ProposeSkillForInput is not null)
        {
            AnsiConsole.MarkupLine("[yellow]Я заметил, что вы повторяете похожий запрос несколько раз.[/]");
            if (Confirm("Сохранить это как навык?"))
            {
                var skill = await CreateSkillWithStatus(resp.ProposeSkillForInput, ct);
                agent.ResetRequestCounter(resp.ProposeSkillForInput);
                AnsiConsole.MarkupLineInterpolated($"[green]✓ Навык создан:[/] {skill.Meta.Name} (id: {skill.Meta.Id})");
            }
            else
            {
                agent.ResetRequestCounter(resp.ProposeSkillForInput);
            }
        }

        // Human-in-the-loop: предложение улучшить навык
        if (resp.ProposeImproveSkillId is not null)
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]Я не очень хорошо справляюсь с навыком «{resp.ProposeImproveSkillName}».[/]");
            if (Confirm("Обновить навык (создать новую версию)?"))
            {
                var improved = await ImproveSkillWithStatus(resp.ProposeImproveSkillId, ct);
                if (improved is not null)
                {
                    AnsiConsole.MarkupLineInterpolated($"[green]✓ Навык обновлён до версии v{improved.Meta.Version}.[/]");
                }
            }
        }

        // Периодическая рефлексия
        if (agent.ShouldReflectByCount())
        {
            AnsiConsole.MarkupLine("[grey]— достигнут порог команд, запускаю рефлексию —[/]");
            await RunReflection(ct);
        }
    }

    private async Task<bool> HandleCommandAsync(string command, CancellationToken ct)
    {
        var parts = SplitCommand(command);
        var cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "/exit" or "/quit":
                await ShutdownAsync(ct);
                return true;

            case "/help":
                PrintHelp();
                break;

            case "/skills":
                if (parts.Length >= 2 && parts[1] == "create")
                {
                    var name = parts.Length >= 3
                        ? parts[2]
                        : AnsiConsole.Ask<string>("Название/тема навыка:");
                    var skill = await CreateSkillWithStatus(name, ct);
                    AnsiConsole.MarkupLineInterpolated($"[green]✓ Навык создан:[/] {skill.Meta.Name} (id: {skill.Meta.Id})");
                }
                else if (parts.Length >= 3 && parts[1] == "improve")
                {
                    var improved = await ImproveSkillWithStatus(parts[2], ct);
                    AnsiConsole.MarkupLine(improved is not null
                        ? $"[green]✓ Навык обновлён до версии v{improved.Meta.Version}.[/]"
                        : "[red]Навык с таким id не найден.[/]");
                }
                else if (parts.Length >= 3 && parts[1] == "export")
                {
                    ExportSkill(parts[2]);
                }
                else if (parts.Length >= 3 && parts[1] == "import")
                {
                    await ImportSkillAsync(parts[2], ct);
                }
                else
                {
                    PrintSkills();
                }

                break;

            case "/memory":
                if (parts.Length >= 2 && parts[1] == "reset")
                {
                    if (Confirm("Точно сбросить всю память пользователя?"))
                    {
                        memory.Reset();
                        AnsiConsole.MarkupLine("[green]✓ Память сброшена.[/]");
                    }
                }
                else // show
                {
                    var panel = new Panel(Markup.Escape(memory.ProfileMarkdown))
                        .Header("Профиль пользователя").Expand();
                    AnsiConsole.Write(panel);
                }

                break;

            case "/reflect":
                await RunReflection(ct);
                break;

            case "/marketplace":
                HandleMarketplaceCommand(parts);
                break;

            case "/templates":
                HandleTemplatesCommand(parts);
                break;

            case "/mesh":
                await HandleMeshCommand(parts, ct);
                break;

            default:
                AnsiConsole.MarkupLineInterpolated($"[red]Неизвестная команда:[/] {cmd}. Наберите /help.");
                break;
        }

        return false;
    }

    private async Task<Skill> CreateSkillWithStatus(string topic, CancellationToken ct)
    {
        Skill skill = null!;
        await AnsiConsole.Status().StartAsync("Генерирую навык...", async _ => { skill = await skills.CreateAsync(topic, ct); });
        return skill;
    }

    private async Task<Skill?> ImproveSkillWithStatus(string id, CancellationToken ct)
    {
        Skill? skill = null;
        await AnsiConsole.Status().StartAsync("Улучшаю навык...", async _ => { skill = await skills.ImproveAsync(id, ct); });
        return skill;
    }

    private void ExportSkill(string skillId)
    {
        try
        {
            var path = packager.Export(skillId);
            AnsiConsole.MarkupLineInterpolated($"[green]✓ Пакет экспортирован:[/] [grey]{Markup.Escape(path)}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Ошибка экспорта:[/] {ex.Message}");
        }
    }

    private async Task ImportSkillAsync(string packagePath, CancellationToken ct)
    {
        try
        {
            // Сначала валидируем
            var errors = packager.Validate(packagePath);
            if (errors.Count > 0)
            {
                foreach (var err in errors)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Ошибка валидации:[/] {err}");
                }

                return;
            }

            Skill? skill = null;
            await AnsiConsole.Status().StartAsync("Импортирую навык...", async _ => { skill = await Task.Run(() => packager.Import(packagePath), ct); });
            AnsiConsole.MarkupLineInterpolated($"[green]✓ Навык импортирован:[/] {skill!.Meta.Name} (id: {skill.Meta.Id}, v{skill.Meta.Version})");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Ошибка импорта:[/] {ex.Message}");
        }
    }

    private async Task RunReflection(CancellationToken ct)
    {
        ReflectionResult result = null!;
        await AnsiConsole.Status().StartAsync("Провожу самоанализ...", async _ => { result = await reflection.ReflectAsync(agent.SessionId, ct); });
        AnsiConsole.Write(new Panel(Markup.Escape(result.Markdown)).Header("Reflection Engine").Expand());
        AnsiConsole.MarkupLineInterpolated($"[grey]Сохранено в Skills/{result.FilePath}[/]");
    }

    private async Task ShutdownAsync(CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[grey]Сохраняю память и запускаю финальную рефлексию...[/]");
        await AnsiConsole.Status().StartAsync("Завершение сессии...", async _ => { await memory.PersistSessionAsync(agent.Transcript, ct); });
        await RunReflection(ct);
        agent.EndSession();
        AnsiConsole.MarkupLine("[green]До встречи![/]");
    }

    private void PrintSkills()
    {
        var skills1 = skills.All();
        if (skills1.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]Навыков пока нет. Они создаются автоматически или командой /skills create.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("id");
        table.AddColumn("Название");
        table.AddColumn("Фразы-приёмники");
        table.AddColumn("v");
        table.AddColumn("success");
        table.AddColumn("uses");
        foreach (var s in skills1.OrderByDescending(s => s.Meta.TotalUses))
        {
            table.AddRow(
                s.Meta.Id,
                Markup.Escape(s.Meta.Name),
                Markup.Escape(string.Join(", ", s.Meta.PhraseReceivers)),
                s.Meta.Version.ToString(),
                s.Meta.SuccessRate.ToString("0.00"),
                s.Meta.TotalUses.ToString());
        }

        AnsiConsole.Write(table);
    }

    private void HandleMarketplaceCommand(string[] parts)
    {
        var sub = parts.Length >= 2
            ? parts[1].ToLowerInvariant()
            : "list";

        switch (sub)
        {
            case "list":
                var entries = marketplace.List();
                if (entries.Count == 0)
                {
                    AnsiConsole.MarkupLine("[grey]Маркетплейс пуст. Опубликуйте пакет: /skills export {id}, затем /marketplace publish {path}.[/]");
                    return;
                }

                var table = new Table().Border(TableBorder.Rounded).Title("Маркетплейс");
                table.AddColumn("Файл");
                table.AddColumn("Навык");
                table.AddColumn("Описание");
                table.AddColumn("v");
                foreach (var e in entries)
                {
                    table.AddRow(Markup.Escape(e.FileName), Markup.Escape(e.Name), Markup.Escape(e.Description), e.Version.ToString());
                }

                AnsiConsole.Write(table);
                break;

            case "search" when parts.Length >= 3:
                var results = marketplace.Search(parts[2]);
                if (results.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[grey]Ничего не найдено по запросу '{parts[2]}'.[/]");
                    return;
                }

                var searchTable = new Table().Border(TableBorder.Rounded);
                searchTable.AddColumn("Навык");
                searchTable.AddColumn("Описание");
                foreach (var e in results)
                {
                    searchTable.AddRow(Markup.Escape(e.Name), Markup.Escape(e.Description));
                }

                AnsiConsole.Write(searchTable);
                break;

            case "install" when parts.Length >= 3:
                try
                {
                    var skill = marketplace.Install(parts[2]);
                    AnsiConsole.MarkupLineInterpolated($"[green]✓ Установлен из маркетплейса:[/] {skill.Meta.Name} (id: {skill.Meta.Id})");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Ошибка установки:[/] {ex.Message}");
                }

                break;

            case "publish" when parts.Length >= 3:
                try
                {
                    var destPath = marketplace.Publish(parts[2]);
                    AnsiConsole.MarkupLineInterpolated($"[green]✓ Опубликован в маркетплейс:[/] [grey]{Markup.Escape(destPath)}[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Ошибка публикации:[/] {ex.Message}");
                }

                break;

            default:
                AnsiConsole.MarkupLine("[grey]Команды:[/] /marketplace list | search {query} | install {file} | publish {path}");
                break;
        }
    }

    private void HandleTemplatesCommand(string[] parts)
    {
        var sub = parts.Length >= 2
            ? parts[1].ToLowerInvariant()
            : "list";

        switch (sub)
        {
            case "list":
                var templates1 = templates.List();
                if (templates1.Count == 0)
                {
                    AnsiConsole.MarkupLine("[grey]Шаблонов нет. Шаблоны — это bundles навыков + памяти + инструментов для вертикальных сценариев.[/]");
                    return;
                }

                var table = new Table().Border(TableBorder.Rounded).Title("Шаблоны агентов");
                table.AddColumn("Файл");
                table.AddColumn("Название");
                table.AddColumn("Описание");
                table.AddColumn("Навыков");
                foreach (var t in templates1)
                {
                    table.AddRow(Markup.Escape(t.FileName), Markup.Escape(t.Name), Markup.Escape(t.Description), t.SkillCount.ToString());
                }

                AnsiConsole.Write(table);
                break;

            case "apply" when parts.Length >= 3:
                try
                {
                    var result = templates.Apply(parts[2]);
                    AnsiConsole.MarkupLineInterpolated($"[green]✓ Шаблон применён:[/] {result.TemplateName}");
                    if (result.InstalledSkills.Count > 0)
                    {
                        AnsiConsole.MarkupLineInterpolated($"  Навыков установлено: {result.InstalledSkills.Count} ({string.Join(", ", result.InstalledSkills)})");
                    }

                    if (result.InstalledMemoryFiles.Count > 0)
                    {
                        AnsiConsole.MarkupLineInterpolated($"  Файлов памяти: {result.InstalledMemoryFiles.Count}");
                    }

                    if (result.InstalledToolFiles.Count > 0)
                    {
                        AnsiConsole.MarkupLineInterpolated($"  Инструментов: {result.InstalledToolFiles.Count}");
                    }

                    if (result.HasErrors)
                    {
                        foreach (var err in result.Errors)
                        {
                            AnsiConsole.MarkupLineInterpolated($"[red]  Ошибка:[/] {err}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Ошибка применения шаблона:[/] {ex.Message}");
                }

                break;

            default:
                AnsiConsole.MarkupLine("[grey]Команды:[/] /templates list | apply {file}");
                break;
        }
    }

    private async Task HandleMeshCommand(string[] parts, CancellationToken ct)
    {
        var sub = parts.Length >= 2
            ? parts[1].ToLowerInvariant()
            : "status";

        switch (sub)
        {
            case "status":
                var manifest = manifestService.Current;
                AnsiConsole.MarkupLineInterpolated($"[blue]Agent ID:[/] {manifest.AgentId}");
                AnsiConsole.MarkupLineInterpolated($"[blue]Endpoint:[/] {manifest.Endpoint}");
                AnsiConsole.MarkupLineInterpolated($"[blue]Capabilities:[/] {manifest.Capabilities.Count}");
                AnsiConsole.MarkupLineInterpolated($"[blue]Registry agents:[/] {capabilityRegistry.ListAgents().Count}");
                break;

            case "manifest":
                var m = manifestService.Save();
                AnsiConsole.MarkupLineInterpolated($"[green]✓ Манифест сохранён:[/] [grey]{Markup.Escape(manifestService.ManifestPath)}[/]");
                AnsiConsole.MarkupLineInterpolated($"  Agent: {m.AgentId} | Endpoint: {m.Endpoint} | Capabilities: {m.Capabilities.Count}");
                break;

            case "agents":
                var agents = capabilityRegistry.ListAgents();
                if (agents.Count == 0)
                {
                    AnsiConsole.MarkupLine("[grey]Реестр пуст. Зарегистрируйте peer'ов: /mesh publish-self, /mesh register {json}.[/]");
                    return;
                }

                var table = new Table().Border(TableBorder.Rounded).Title("Capability Registry");
                table.AddColumn("Agent ID");
                table.AddColumn("Имя");
                table.AddColumn("Endpoint");
                table.AddColumn("Last seen");
                foreach (var a in agents)
                {
                    table.AddRow(Markup.Escape(a.AgentId), Markup.Escape(a.DisplayName), Markup.Escape(a.Endpoint), a.LastSeen);
                }

                AnsiConsole.Write(table);
                break;

            case "publish-self":
                manifestService.Save();
                capabilityRegistry.Register(manifestService.Current);
                AnsiConsole.MarkupLineInterpolated($"[green]✓ Агент опубликован в реестре:[/] {manifestService.Current.AgentId}");
                break;

            case "find" when parts.Length >= 3:
                var found = capabilityRegistry.FindByCapability(parts[2]);
                if (found.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[grey]Агенты с capability '{parts[2]}' не найдены.[/]");
                    return;
                }

                foreach (var f in found)
                {
                    AnsiConsole.MarkupLineInterpolated($"  [blue]{f.AgentId}[/] ({f.DisplayName}) → {f.Endpoint}");
                }

                break;

            case "send" when parts.Length >= 4:
                // /mesh send {targetAgentId} {message...}
                var targetId = parts[2];
                var messageText = string.Join(" ", parts.Skip(3));
                var envelope = new IntentEnvelope(
                    IntentIds.NewRequestId(),
                    manifestService.Current.AgentId,
                    messageText,
                    messageText,
                    TraceId: Guid.NewGuid().ToString("N")[..8]);
                try
                {
                    var resp = await intentRouter.RouteAsync(envelope, ct);
                    if (resp.IsSuccess)
                    {
                        AnsiConsole.MarkupLineInterpolated($"[green]✓ Ответ от {resp.Agent}:[/] mode={resp.Mode} conf={resp.Confidence}");
                        AnsiConsole.WriteLine(resp.Result ?? "");
                    }
                    else
                    {
                        AnsiConsole.MarkupLineInterpolated($"[red]Ошибка ({resp.Status}):[/] {resp.Error}");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Ошибка:[/] {ex.Message}");
                }

                break;

            // === Phase 4: Fan-out + Circuit Breaker + Reflection + Shared Memory ===

            case "fanout" when parts.Length >= 3:
                // /mesh fanout {message...} — fan-out нескольким peer'ам + выбор лучшего
                var fanOutMessage = string.Join(" ", parts.Skip(2));
                var fanOutEnvelope = new IntentEnvelope(
                    IntentIds.NewRequestId(),
                    manifestService.Current.AgentId,
                    fanOutMessage,
                    fanOutMessage,
                    TraceId: Guid.NewGuid().ToString("N")[..8]);
                try
                {
                    var result = await meshRouter.RouteWithFanOutAsync(fanOutEnvelope, ct);
                    if (result.HasWinner)
                    {
                        AnsiConsole.MarkupLineInterpolated($"[green]✓ Лучший ответ от {result.Winner!.Agent}:[/] method={result.SelectionMethod} conf={result.Winner.Confidence} peers={result.AllResponses.Count} time={result.Duration.TotalMilliseconds:F0}ms");
                        if (!string.IsNullOrEmpty(result.JudgeRationale))
                        {
                            AnsiConsole.MarkupLineInterpolated($"[grey]Judge:[/] {result.JudgeRationale}");
                        }

                        AnsiConsole.WriteLine(result.Winner.Result ?? "");
                    }
                    else
                    {
                        AnsiConsole.MarkupLineInterpolated($"[red]Нет успешных ответов.[/] peers={result.AllResponses.Count} method={result.SelectionMethod}");
                        foreach (var r in result.AllResponses)
                        {
                            AnsiConsole.MarkupLineInterpolated($"  [grey]- {r.Agent}: {r.Status} {r.Error}[/]");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Fan-out error:[/] {ex.Message}");
                }

                break;

            case "circuits":
                // /mesh circuits — состояние circuit breakers
                var states = circuitBreaker.GetAllStates();
                if (states.Count == 0)
                {
                    AnsiConsole.MarkupLine("[grey]Circuit breakers: нет отслеживаемых peer'ов.[/]");
                    break;
                }

                var cbTable = new Table().Border(TableBorder.Rounded).Title("Circuit Breakers");
                cbTable.AddColumn("Agent ID");
                cbTable.AddColumn("State");
                foreach (var kvp in states)
                {
                    var color = kvp.Value switch
                    {
                        CircuitState.Closed => "green",
                        CircuitState.Open => "red",
                        CircuitState.HalfOpen => "yellow",
                        _ => "grey"
                    };
                    cbTable.AddRow(Markup.Escape(kvp.Key), $"[{color}]{kvp.Value}[/]");
                }

                AnsiConsole.Write(cbTable);
                break;

            case "reflect-mesh":
                // /mesh reflect-mesh — distributed reflection
                try
                {
                    DistributedReflectionResult? reflResult = null;
                    await AnsiConsole.Status().StartAsync("Distributed reflection...", async _ => { reflResult = await distributedReflection.ReflectAsync(ct); });
                    AnsiConsole.Write(new Panel(Markup.Escape(reflResult!.Markdown))
                        .Header("Distributed Reflection").Expand());
                    AnsiConsole.MarkupLineInterpolated($"[grey]Peers: {reflResult.PeerCount} | Open circuits: {reflResult.OpenCircuitCount} | Total capabilities: {reflResult.TotalCapabilities}[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Reflection error:[/] {ex.Message}");
                }

                break;

            case "recommendations":
                // /mesh recommendations — рекомендации по новым локальным навыкам
                var recs = distributedReflection.GetLocalSkillRecommendations();
                if (recs.Count == 0)
                {
                    AnsiConsole.MarkupLine("[grey]Рекомендаций нет — все peer'ы доступны.[/]");
                }
                else
                {
                    foreach (var rec in recs)
                    {
                        AnsiConsole.MarkupLineInterpolated($"[yellow]⚠ {rec}[/]");
                    }
                }

                break;

            case "memory-sync":
                // /mesh memory-sync — синхронизация shared-фактов с peer'ами
                try
                {
                    var received = 0;
                    await AnsiConsole.Status().StartAsync("Syncing shared memory...", async _ => { received = await sharedMemorySync.SyncFromPeersAsync(ct); });
                    AnsiConsole.MarkupLineInterpolated($"[green]✓ Синхронизировано фактов:[/] {received}");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Sync error:[/] {ex.Message}");
                }

                break;

            case "shared":
                // /mesh shared — список shared-фактов памяти
                var facts = sharedMemorySync.GetLocalFacts();
                if (facts.Count == 0)
                {
                    AnsiConsole.MarkupLine("[grey]Shared-фактов нет. Опубликуйте через Web API: POST /api/mesh/shared-memory.[/]");
                    break;
                }

                var fTable = new Table().Border(TableBorder.Rounded).Title("Shared Memory Facts");
                fTable.AddColumn("ID");
                fTable.AddColumn("Category");
                fTable.AddColumn("Source");
                fTable.AddColumn("Updated");
                foreach (var f in facts)
                {
                    fTable.AddRow(f.Id, f.Category, f.SourceAgent, f.UpdatedAt);
                }

                AnsiConsole.Write(fTable);
                break;

            default:
                AnsiConsole.MarkupLine("[grey]Команды:[/] /mesh status | manifest | agents | publish-self | " +
                                       "find {cap} | send {id} {msg} | fanout {msg} | circuits | reflect-mesh | recommendations | " +
                                       "memory-sync | shared");
                break;
        }
    }

    private static void PrintBanner()
    {
        AnsiConsole.Write(new FigletText("Hercules").Color(Color.Aqua));
        AnsiConsole.MarkupLine("[grey]Самообучающийся микроагент. Наберите [/][blue]/help[/][grey] для списка команд, [/][blue]/exit[/][grey] для выхода.[/]");
        AnsiConsole.WriteLine();
    }

    private static void PrintHelp()
    {
        var table = new Table().Border(TableBorder.Rounded).Title("Команды");
        table.AddColumn("Команда");
        table.AddColumn("Описание");
        table.AddRow("> текст", "Прямой запрос к LLM с контекстом профиля");
        table.AddRow("/skills", "Показать все навыки");
        table.AddRow("/skills create \"...\"", "Создать навык вручную");
        table.AddRow("/skills improve {id}", "Улучшить навык (новая версия)");
        table.AddRow("/skills export {id}", "Экспортировать навык в .skillpkg");
        table.AddRow("/skills import {path}", "Импортировать навык из .skillpkg");
        table.AddRow("/marketplace list", "Показать пакеты навыков в маркетплейсе");
        table.AddRow("/marketplace search {q}", "Поиск в маркетплейсе");
        table.AddRow("/marketplace install {f}", "Установить пакет из маркетплейса");
        table.AddRow("/marketplace publish {p}", "Опубликовать .skillpkg в маркетплейс");
        table.AddRow("/templates list", "Показать шаблоны агентов");
        table.AddRow("/templates apply {f}", "Применить шаблон (bundle навыков+памяти)");
        table.AddRow("/mesh status", "Состояние mesh-узла (манифест, реестр)");
        table.AddRow("/mesh manifest", "Сгенерировать и сохранить манифест агента");
        table.AddRow("/mesh agents", "Список известных агентов в реестре");
        table.AddRow("/mesh publish-self", "Опубликовать себя в capability registry");
        table.AddRow("/mesh find {cap}", "Найти агентов по имени capability");
        table.AddRow("/mesh send {id} {msg}", "Отправить intent агенту в mesh");
        table.AddRow("/mesh fanout {msg}", "Fan-out нескольким peer'ам + выбор лучшего");
        table.AddRow("/mesh circuits", "Состояние circuit breakers peer'ов");
        table.AddRow("/mesh reflect-mesh", "Distributed reflection по mesh");
        table.AddRow("/mesh recommendations", "Рекомендации по новым локальным навыкам");
        table.AddRow("/mesh memory-sync", "Синхронизировать shared-факты с peer'ами");
        table.AddRow("/mesh shared", "Список shared-фактов памяти");
        table.AddRow("/memory show", "Показать профиль пользователя");
        table.AddRow("/memory reset", "Сбросить память");
        table.AddRow("/reflect", "Запустить рефлексию вручную");
        table.AddRow("/help", "Эта справка");
        table.AddRow("/exit", "Выход с сохранением контекста");
        AnsiConsole.Write(table);
    }

    /// <summary>
    ///     Прочитать строку ввода. В интерактивном режиме используется Spectre,
    ///     при перенаправлённом вводе (пайп/файл) — обычный Console.ReadLine.
    /// </summary>
    private static string? ReadInput()
    {
        return Console.IsInputRedirected
            ? Console.ReadLine()
            : AnsiConsole.Prompt(new TextPrompt<string>("[green]>[/] ").AllowEmpty());
    }

    /// <summary>Подтверждение с поддержкой неинтерактивного режима (по умолчанию — да).</summary>
    private static bool Confirm(string question)
    {
        if (!Console.IsInputRedirected)
        {
            return AnsiConsole.Confirm(question);
        }

        AnsiConsole.MarkupLineInterpolated($"[grey]{question} (авто-да в неинтерактивном режиме)[/]");
        return true;
    }

    /// <summary>Разбор команды с поддержкой кавычек: /skills create "Поиск вакансий".</summary>
    private static string[] SplitCommand(string command)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in command)
        {
            switch (ch)
            {
                case '"':
                    inQuotes = !inQuotes;
                    continue;
                case ' ' when !inQuotes:
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }

                    break;
                }
                default:
                    current.Append(ch);
                    break;
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result.ToArray();
    }
}
