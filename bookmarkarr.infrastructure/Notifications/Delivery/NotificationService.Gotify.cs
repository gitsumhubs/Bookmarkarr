/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 */
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Infrastructure.Notifications.Delivery
{
    /// <summary>
    /// Gotify delivery (https://gotify.net/api-docs#/message/createMessage).
    /// </summary>
    /// <remarks>
    /// Kept in its own partial so the shared webhook dispatcher stays within the
    /// per-file size budget the architecture tests enforce.
    /// </remarks>
    public partial class NotificationService
    {
        private async Task SendGotifyNotificationAsync(string trigger, object data, string webhookUrl)
        {
            try
            {
                var payloadContext = await NotificationPayloadContextResolver.ResolveAsync(_configurationService, _requestContextAccessor, _logger);
                var discordPayload = NotificationPayloadBuilder.CreateDiscordPayload(trigger, data, payloadContext.BaseUrl, payloadContext.ApiVersion);
                var text = discordPayload is JsonObject d && d.TryGetPropertyValue("content", out var c) ? (c?.ToString() ?? string.Empty) : string.Empty;

                // Gotify shows the title in the notification header and the message in
                // the body. The trigger name is the most useful title we have, since the
                // rendered content is a single prose line.
                var gotifyBody = new
                {
                    title = trigger,
                    message = string.IsNullOrWhiteSpace(text) ? trigger : text,
                    priority = 5
                };
                var json = JsonSerializer.Serialize(gotifyBody);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                var redactedUrl = LogRedaction.RedactText(webhookUrl, LogRedaction.GetSensitiveValuesFromEnvironment());
                _logger.LogInformation("Sending Gotify POST to {WebhookUrl} with body: {Body}", redactedUrl, NotificationDiagnostics.AggressiveRedact(LogRedaction.RedactText(json, LogRedaction.GetSensitiveValuesFromEnvironment())));

                var response = await PostValidatedAsync(webhookUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    var respText = await NotificationDiagnostics.TryReadContentAsync(response.Content, _logger);
                    var redactedResp = NotificationDiagnostics.AggressiveRedact(LogRedaction.RedactText(respText, LogRedaction.GetSensitiveValuesFromEnvironment()));
                    _logger.LogWarning("Gotify response from {WebhookUrl}: {Status} - {Body}", redactedUrl, response.StatusCode, redactedResp);
                    await NotificationDiagnostics.LogFailedResponseAsync(response, webhookUrl, _logger);
                }
                return;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error sending Gotify notification to {WebhookUrl}", LogRedaction.RedactText(webhookUrl, LogRedaction.GetSensitiveValuesFromEnvironment()));
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            // Intentional broad catch: notification delivery failures must never propagate to callers.
            // OperationCanceledException is already handled above. All other failures are logged and swallowed.
#pragma warning disable CA1031
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Error sending Gotify notification to {WebhookUrl}", LogRedaction.RedactText(webhookUrl, LogRedaction.GetSensitiveValuesFromEnvironment()));
                return;
            }
#pragma warning restore CA1031
        }

        /// <summary>
        /// Detects a Gotify message endpoint.
        /// </summary>
        /// <remarks>
        /// Gotify is almost always self-hosted, so the hostname is whatever the user chose
        /// and cannot be matched the way a SaaS provider's can. The reliable signal is the
        /// shape of its publish endpoint: a path of exactly <c>/message</c> plus a
        /// <c>token</c> query parameter.
        ///
        /// The path is matched exactly rather than by suffix so it cannot capture the other
        /// providers handled here - Telegram posts to <c>/bot&lt;token&gt;/sendMessage</c> and
        /// Pushover to <c>/1/messages.json</c>, neither of which is <c>/message</c>. A
        /// hostname containing "gotify" is also accepted for the common naming convention.
        /// </remarks>
        internal static bool IsGotifyWebhook(string webhookUrl)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                return false;
            }

            if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (uri.Host.Contains("gotify", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var path = uri.AbsolutePath.TrimEnd('/');
            if (!path.Equals("/message", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return !string.IsNullOrWhiteSpace(query["token"]);
        }
    }
}
