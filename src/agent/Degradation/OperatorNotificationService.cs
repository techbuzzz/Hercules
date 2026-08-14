using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Hercules.Degradation;

/// <summary>
///     Operator notification service for degradation state changes.
///     Task 061: Local-first degradation.
/// </summary>
public sealed class OperatorNotificationService : IDisposable
{
    private readonly DegradationConfig _config;
    private readonly ILogger<OperatorNotificationService> _log;
    private readonly HttpClient _http;
    private bool _disposed;

    public OperatorNotificationService(
        DegradationConfig config,
        ILogger<OperatorNotificationService> log,
        HttpClient? http = null)
    {
        _config = config;
        _log = log;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>
    ///     Notify operators of a degradation mode change.
    /// </summary>
    public async Task NotifyModeChangeAsync(
        DegradationMode previousMode,
        DegradationMode newMode,
        string? reason = null,
        CancellationToken ct = default)
    {
        if (!_config.Notifications.Enabled)
        {
            return;
        }

        // Check notification settings
        if (newMode == DegradationMode.Degraded && !_config.Notifications.NotifyOnDegraded)
        {
            return;
        }

        if (newMode == DegradationMode.Offline && !_config.Notifications.NotifyOnOffline)
        {
            return;
        }

        if (newMode == DegradationMode.Full && !_config.Notifications.NotifyOnRecovery)
        {
            return;
        }

        var message = FormatModeChangeMessage(previousMode, newMode, reason);

        await Task.WhenAll(
            NotifyWebhookAsync(message, ct),
            NotifyTelegramAsync(message, ct),
            NotifyEmailAsync(message, ct)
        );
    }

    /// <summary>
    ///     Notify operators of work being queued.
    /// </summary>
    public async Task NotifyWorkQueuedAsync(
        int queueSize,
        CancellationToken ct = default)
    {
        if (!_config.Notifications.Enabled || !_config.Notifications.NotifyOnWorkQueued)
        {
            return;
        }

        var message = $"⚠️ [Hercules] Work queued due to degradation. Queue size: {queueSize}";

        await Task.WhenAll(
            NotifyWebhookAsync(message, ct),
            NotifyTelegramAsync(message, ct),
            NotifyEmailAsync(message, ct)
        );
    }

    private string FormatModeChangeMessage(
        DegradationMode previousMode,
        DegradationMode newMode,
        string? reason)
    {
        var emoji = newMode switch
        {
            DegradationMode.Full => "✅",
            DegradationMode.Degraded => "⚠️",
            DegradationMode.Offline => "🚫",
            _ => "❓"
        };

        var title = newMode switch
        {
            DegradationMode.Full => "[Hercules] Recovered to Full Mode",
            DegradationMode.Degraded => "[Hercules] Degraded Mode Activated",
            DegradationMode.Offline => "[Hercules] Offline Mode Activated",
            _ => "[Hercules] Mode Changed"
        };

        var message = $"{emoji} {title}\n" +
                     $"Previous: {previousMode}\n" +
                     $"Current: {newMode}";

        if (!string.IsNullOrEmpty(reason))
        {
            message += $"\nReason: {reason}";
        }

        message += $"\nTime: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";

        return message;
    }

    private async Task NotifyWebhookAsync(string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.Notifications.WebhookUrl))
        {
            return;
        }

        try
        {
            var payload = new
            {
                text = message,
                timestamp = DateTimeOffset.UtcNow
            };

            var response = await _http.PostAsJsonAsync(
                _config.Notifications.WebhookUrl,
                payload,
                ct);

            if (response.IsSuccessStatusCode)
            {
                _log.LogDebug("Webhook notification sent successfully");
            }
            else
            {
                _log.LogWarning("Webhook notification failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to send webhook notification");
        }
    }

    private async Task NotifyTelegramAsync(string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.Notifications.TelegramBotToken) ||
            string.IsNullOrWhiteSpace(_config.Notifications.TelegramChatId))
        {
            return;
        }

        try
        {
            var url = $"https://api.telegram.org/bot{_config.Notifications.TelegramBotToken}/sendMessage";
            var payload = new
            {
                chat_id = _config.Notifications.TelegramChatId,
                text = message,
                parse_mode = "Markdown"
            };

            var response = await _http.PostAsJsonAsync(url, payload, ct);

            if (response.IsSuccessStatusCode)
            {
                _log.LogDebug("Telegram notification sent successfully");
            }
            else
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                _log.LogWarning("Telegram notification failed: {StatusCode} - {Content}",
                    response.StatusCode, content);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to send Telegram notification");
        }
    }

    private async Task NotifyEmailAsync(string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.Notifications.SmtpHost) ||
            string.IsNullOrWhiteSpace(_config.Notifications.FromEmail) ||
            string.IsNullOrWhiteSpace(_config.Notifications.ToEmails))
        {
            return;
        }

        try
        {
            // For email, we would typically use an SMTP client
            // This is a placeholder for the email notification logic
            // In production, use MailKit or similar library
            _log.LogDebug("Email notification would be sent via {SmtpHost}:{Port}",
                _config.Notifications.SmtpHost,
                _config.Notifications.SmtpPort);

            // Note: Actual SMTP implementation would go here
            // For now, just log the intent
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to send email notification");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
