using Hercules.Agent;
using Hercules.Config;
using Hercules.Skills;
using Hercules.Storage;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Hercules.Telegram;

/// <summary>
///     Вторичный интерфейс — Telegram-бот (long polling).
///     Команды: /start, /skills, /profile, /reset, /evaluate, /deprecate, /rollback.
///     Обычный текст → ответ агента.
///     Согласно ТЗ — один пользователь, один профиль.
/// </summary>
public sealed class TelegramBotInterface(
    TelegramConfig cfg,
    AgentCore agent,
    SkillManager skills,
    MemoryManager memory,
    SkillLifecycleService lifecycle,
    ILogger<TelegramBotInterface> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cfg.BotToken))
        {
            throw new InvalidOperationException("Не задан Telegram:BotToken в конфигурации.");
        }

        var bot = new TelegramBotClient(cfg.BotToken);
        agent.StartSession();

        User me = await bot.GetMe(ct);
        Console.WriteLine($"[Telegram] Бот @{me.Username} запущен. Ожидаю сообщения...");

        var options = new ReceiverOptions { AllowedUpdates = [UpdateType.Message] };
        await bot.ReceiveAsync(HandleUpdateAsync, HandleErrorAsync, options, ct);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Message is not { Text: { } text } msg)
        {
            return;
        }

        var chatId = msg.Chat.Id;
        var parts = text.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();

        try
        {
            var reply = cmd switch
            {
                "/start" => "👋 Привет! Я Hercules — самообучающийся ассистент.\n" +
                            "Команды: /skills, /profile, /reset, /evaluate, /deprecate, /rollback.\nПросто напишите сообщение, и я отвечу.",
                "/skills" => FormatSkills(),
                "/profile" => Truncate(memory.ProfileMarkdown, 3500),
                "/reset" => ResetMemory(),
                "/evaluate" when parts.Length >= 2 => await EvaluateSkill(parts[1], ct),
                "/deprecate" when parts.Length >= 2 => DeprecateSkill(parts[1], parts.Length >= 3 ? parts[2] : "deprecated via Telegram"),
                "/rollback" when parts.Length >= 2 => RollbackSkill(parts[1]),
                "/help" => "🛠 Команды:\n/skills — список навыков\n/evaluate [id] — оценить навык\n" +
                           "/deprecate [id] [reason] — пометить deprecated\n/rollback [id] — откатить версию\n" +
                           "/profile — профиль памяти\n/reset — сбросить память\n/hello — приветствие",
                "/hello" => "👋 Привет! Напишите сообщение, и я отвечу.",
                _ => await ChatReply(text, ct)
            };

            await bot.SendMessage(chatId, reply, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await bot.SendMessage(chatId, $"Ошибка: {ex.Message}", cancellationToken: ct);
        }
    }

    private async Task<string> ChatReply(string text, CancellationToken ct)
    {
        AgentResponse resp = await agent.HandleAsync(text, ct);
        var tag = resp.Mode == "skill"
            ? $"🧩 навык: {resp.UsedSkill?.Meta.Name}"
            : "💬 direct";
        var footer = $"\n\n_{tag} · {resp.Provider} · conf={resp.Confidence}_";

        // Авто-предложения (в Telegram — информационно, подтверждение через явные команды)
        if (resp.ProposeSkillForInput is not null)
        {
            footer += "\n_Похоже, запрос повторяется. Создать навык можно командой в CLI._";
        }

        return resp.Answer + footer;
    }

    private string ResetMemory()
    {
        memory.Reset();
        return "✓ Память сброшена.";
    }

    private string FormatSkills()
    {
        List<Skill> skills1 = skills.All();
        if (skills1.Count == 0)
        {
            return "Навыков пока нет.";
        }

        return string.Join("\n", skills1.Select(s =>
        {
            var deprecated = !string.IsNullOrEmpty(s.Meta.DeprecatedAt) ? " [DEPRECATED]" : "";
            return $"• {s.Meta.Name} (id: `{s.Meta.Id}`, v{s.Meta.Version}, success={s.Meta.SuccessRate:0.00}){deprecated}";
        }));
    }

    private async Task<string> EvaluateSkill(string skillId, CancellationToken ct)
    {
        var result = await lifecycle.EvaluateAsync(skillId, ct);
        var score = (result.Score * 100).ToString("0.0");
        var status = result.FailedTests == 0 ? "✅ PASS" : "❌ FAIL";
        var tests = result.TestResults.Count > 0
            ? "\n" + string.Join("\n", result.TestResults.Select(t =>
                $"  {(t.Passed ? "✅" : "❌")} {t.Name}"))
            : "";
        return $"📊 Оценка навыка `{skillId}`:\n{status} ({score}%)\n{tests}";
    }

    private string DeprecateSkill(string skillId, string reason)
    {
        var skill = lifecycle.Deprecate(skillId, reason);
        return skill is null
            ? $"⚠️ Навык `{skillId}` не найден."
            : $"🗑 Навык `{skill.Meta.Name}` помечен deprecated.\nПричина: {reason}";
    }

    private string RollbackSkill(string skillId)
    {
        var skill = lifecycle.Rollback(skillId);
        return skill is null
            ? $"⚠️ Навык `{skillId}` не найден или откат невозможен (версия 1)."
            : $"↩️ Навык `{skill.Meta.Name}` откащен до v{skill.Meta.Version}.";
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        logger.LogError(ex, "Telegram error");
        return Task.CompletedTask;
    }

    private static string Truncate(string s, int n)
    {
        return s.Length <= n
            ? s
            : s[..n] + "…";
    }
}
