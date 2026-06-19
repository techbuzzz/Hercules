using System.Text;
using Hercules.Agent;
using Hercules.Storage;
using Spectre.Console;

namespace Hercules.CLI;

/// <summary>
///     REPL-интерфейс командной строки (primary). Реализует команды из ТЗ:
///     прямой ввод, /skills, /skills create, /skills improve, /memory show,
///     /memory reset, /reflect, /exit.
/// </summary>
public sealed class ConsoleUI(
    AgentCore agent,
    SkillManager skills,
    MemoryManager memory,
    ReflectionEngine reflection)
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
                Skill skill = await CreateSkillWithStatus(resp.ProposeSkillForInput, ct);
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
                Skill? improved = await ImproveSkillWithStatus(resp.ProposeImproveSkillId, ct);
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
                    Skill skill = await CreateSkillWithStatus(name, ct);
                    AnsiConsole.MarkupLineInterpolated($"[green]✓ Навык создан:[/] {skill.Meta.Name} (id: {skill.Meta.Id})");
                }
                else if (parts.Length >= 3 && parts[1] == "improve")
                {
                    Skill? improved = await ImproveSkillWithStatus(parts[2], ct);
                    AnsiConsole.MarkupLine(improved is not null
                        ? $"[green]✓ Навык обновлён до версии v{improved.Meta.Version}.[/]"
                        : "[red]Навык с таким id не найден.[/]");
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
                    Panel panel = new Panel(Markup.Escape(memory.ProfileMarkdown))
                        .Header("Профиль пользователя").Expand();
                    AnsiConsole.Write(panel);
                }

                break;

            case "/reflect":
                await RunReflection(ct);
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
        List<Skill> skills1 = skills.All();
        if (skills1.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]Навыков пока нет. Они создаются автоматически или командой /skills create.[/]");
            return;
        }

        Table table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("id");
        table.AddColumn("Название");
        table.AddColumn("Триггеры");
        table.AddColumn("v");
        table.AddColumn("success");
        table.AddColumn("uses");
        foreach (Skill s in skills1.OrderByDescending(s => s.Meta.TotalUses))
        {
            table.AddRow(
                s.Meta.Id,
                Markup.Escape(s.Meta.Name),
                Markup.Escape(string.Join(", ", s.Meta.Triggers)),
                s.Meta.Version.ToString(),
                s.Meta.SuccessRate.ToString("0.00"),
                s.Meta.TotalUses.ToString());
        }

        AnsiConsole.Write(table);
    }

    private static void PrintBanner()
    {
        AnsiConsole.Write(new FigletText("Hercules").Color(Color.Aqua));
        AnsiConsole.MarkupLine("[grey]Самообучающийся микроагент. Наберите [/][blue]/help[/][grey] для списка команд, [/][blue]/exit[/][grey] для выхода.[/]");
        AnsiConsole.WriteLine();
    }

    private static void PrintHelp()
    {
        Table table = new Table().Border(TableBorder.Rounded).Title("Команды");
        table.AddColumn("Команда");
        table.AddColumn("Описание");
        table.AddRow("> текст", "Прямой запрос к LLM с контекстом профиля");
        table.AddRow("/skills", "Показать все навыки");
        table.AddRow("/skills create \"...\"", "Создать навык вручную");
        table.AddRow("/skills improve {id}", "Улучшить навык (новая версия)");
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
