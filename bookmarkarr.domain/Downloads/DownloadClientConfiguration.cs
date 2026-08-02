using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Bookmarkarr.Domain.Downloads
{
    public class DownloadClientConfiguration
    {
        private string? _category;

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "qbittorrent", "transmission", "sabnzbd", "nzbget"
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string DownloadPath { get; set; } = string.Empty;
        public bool UseSSL { get; set; } = false;
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Cleanup behavior after successful import: "none", "remove", "remove_and_delete"
        /// </summary>
        public string RemoveCompletedDownloads { get; set; } = "none";

        // Store as JSON string in database
        public string SettingsJson { get; set; } = "{}";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Not mapped - for JSON serialization in API responses
        public Dictionary<string, object> Settings
        {
            get => string.IsNullOrWhiteSpace(SettingsJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(SettingsJson) ?? [];
            set
            {
                var settings = value ?? new Dictionary<string, object>();
                if (!string.IsNullOrWhiteSpace(_category))
                {
                    settings["category"] = _category;
                }

                SettingsJson = JsonSerializer.Serialize(settings);
            }
        }

        /// <summary>
        /// API compatibility alias for the category stored in SettingsJson.
        /// Older/external clients commonly submit category at the top level.
        /// </summary>
        [NotMapped]
        public string Category
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_category))
                {
                    return _category;
                }

                return Settings.TryGetValue("category", out var category)
                    ? category?.ToString() ?? string.Empty
                    : string.Empty;
            }
            set
            {
                _category = value?.Trim() ?? string.Empty;
                var settings = Settings;
                if (string.IsNullOrWhiteSpace(_category))
                {
                    settings.Remove("category");
                }
                else
                {
                    settings["category"] = _category;
                }

                SettingsJson = JsonSerializer.Serialize(settings);
            }
        }

        public int GetPollingInterval(int defaultInterval = 30)
        {
            if (Settings != null)
            {
                bool hasSetting = Settings.TryGetValue("PollingIntervalSeconds", out var interval);
                if (hasSetting && int.TryParse(interval?.ToString() ?? string.Empty, out var custom) && custom >= 15)
                    return custom;
            }

            return defaultInterval;
        }
    }
}
