namespace Bookmarkarr.Application.Notifications.Contracts;

public interface IDiscordBotService
{
    Task<bool> StartBotAsync();
    Task<bool> StopBotAsync();
    Task<bool> IsBotRunningAsync();
    Task<string?> GetBotStatusAsync();
}
