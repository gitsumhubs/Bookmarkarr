/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */

namespace Bookmarkarr.Tests.Features.Infrastructure.Notifications;

public sealed class GotifyWebhookDetectionTests
{
    [Theory]
    [InlineData("https://gotify.example.com/message?token=AbCdEf123")]
    [InlineData("http://gotify:80/message?token=AbCdEf123")]
    [InlineData("https://GOTIFY.example.com/message?token=x")]
    public void GotifyHostnames_AreDetected(string url)
    {
        Assert.True(NotificationService.IsGotifyWebhook(url));
    }

    [Theory]
    // Gotify is nearly always self-hosted, so the hostname carries no signal and the
    // endpoint shape has to do the work.
    [InlineData("https://notify.mydomain.tld/message?token=AbCdEf123")]
    [InlineData("https://push.internal.example/message/?token=AbCdEf123")]
    [InlineData("https://push.example.com/message?token=abc&priority=8")]
    public void SelfHostedMessageEndpointWithToken_IsDetected(string url)
    {
        Assert.True(NotificationService.IsGotifyWebhook(url));
    }

    [Theory]
    // Every one of these is handled by another provider branch. If Gotify detection
    // captured them, those integrations would silently break.
    [InlineData("https://api.pushover.net/1/messages.json?token=abc&user=xyz")]
    [InlineData("https://api.telegram.org/bot123456:ABC/sendMessage?chat_id=42")]
    [InlineData("https://api.pushbullet.com/v2/pushes?token=abc")]
    [InlineData("https://discord.com/api/webhooks/123/abc")]
    [InlineData("https://ntfy.sh/my-topic")]
    [InlineData("https://hooks.slack.com/services/T000/B000/XXXX")]
    [InlineData("https://hooks.zapier.com/hooks/catch/123/abc")]
    public void OtherProviders_AreNotClaimedByGotify(string url)
    {
        Assert.False(NotificationService.IsGotifyWebhook(url));
    }

    [Theory]
    // A /message path without a token is not a usable Gotify endpoint, so it should
    // fall through to generic webhook handling rather than be posted Gotify-shaped.
    [InlineData("https://push.example.com/message")]
    [InlineData("https://push.example.com/message?token=")]
    [InlineData("https://push.example.com/messages?token=abc")]
    [InlineData("https://push.example.com/api/message/send?token=abc")]
    public void MessagePathWithoutUsableToken_IsNotDetected(string url)
    {
        Assert.False(NotificationService.IsGotifyWebhook(url));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("/message?token=abc")]
    public void MalformedInput_IsRejectedWithoutThrowing(string url)
    {
        Assert.False(NotificationService.IsGotifyWebhook(url));
    }
}
