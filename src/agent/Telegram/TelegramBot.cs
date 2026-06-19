using Hercules.Agent;
using Hercules.Config;
using Hercules.Storage;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Hercules.Telegram;

/// <summary>
///     Вторичный интерфейс — Telegram-бот (long polling).
///     Команды: /start, /skills, /profile, /reset. Обычный текст → ответ агента.
///     Согласно ТЗ — один пользователь, один профиль.
/// </summary>
public sealed class TelegramBotInterface(
    TelegramConfig cfg,
    AgentCore agent,
    SkillManager skills,
    MemoryManager memory)
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

        try
        {
            var reply = text.Trim() switch
            {
                "/start" => "👋 Привет! Я Hercules — самообучающийся ассистент.\n" +
                            "Команды: /skills, /profile, /reset.\nПросто напишите сообщение, и я отвечу.",
                "/skills" => FormatSkills(),
                "/profile" => Truncate(memory.ProfileMarkdown, 3500),
                "/reset" => ResetMemory(),
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
            $"• {s.Meta.Name} (id: {s.Meta.Id}, v{s.Meta.Version}, success={s.Meta.SuccessRate:0.00})"));
    }

    private static Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        Console.Error.WriteLine($"[Telegram] Ошибка: {ex.Message}");
        return Task.CompletedTask;
    }

    private static string Truncate(string s, int n)
    {
        return s.Length <= n
            ? s
            : s[..n] + "…";
    }
}
