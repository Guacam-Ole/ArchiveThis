using HtmlAgilityPack;

namespace ArchiveThis;

using Mastonet;
using Mastonet.Entities;
using Microsoft.Extensions.Logging;

public class Toot
{
    private MastodonClient _client;
    private readonly ILogger<Toot> _logger;

    public Toot(Config.Secrets secrets, ILogger<Toot> logger)
    {
        _client = CreateClient(secrets.Instance, secrets.AccessToken);
        _logger = logger;
    }

    private MastodonClient CreateClient(string instance, string secret)
    {
        return new MastodonClient(instance, secret) ?? throw new Exception("Client could not be created");
    }

    private async Task SendToot(string content, string? replyTo, Visibility visibility)
    {
        _logger.LogDebug("🐘->{replyTo}", replyTo);
        await _client.PublishStatus(content, visibility, replyTo);
    }

    private async Task HandleMentions(IEnumerable<Notification> mentions)
    {
        var currentUser = await _client.GetCurrentUser();
        foreach (var mention in mentions.DistinctBy(q => q.Status!.InReplyToId))
        {
            if (mention.Status == null) continue;
            var alreadyAnswered = await HasAlreadyAnswered(mention.Status, currentUser.Id);
            if (alreadyAnswered)
            {
                _logger.LogDebug("I already answered. Do nothing");
                continue;
            }
            
            var url = await GetUrlFromToot(mention.Status);
            if (url == null)
            {
                await SendToot(
                    $"Sorry, @{mention.Account.AccountName} I did not find any URL in your toot or the one above yours.",
                    mention.Status.Id, mention.Status.Visibility);
                continue;
            }

            var archivUrl = $"http://archive.is/newest/{url}";
            _logger.LogDebug("Received request to store url '{url}'", url);
            var response = $"@{mention.Account.AccountName} \nHere is your archived Website:\n\n💾 {archivUrl}";

            await SendToot(response, mention.Status.Id, mention.Status.Visibility);
        }
    }


    public async Task GetNotifications()
    {
        var newNotifications = await _client.GetNotifications();
        await _client.ClearNotifications();

        if (newNotifications?.Count > 0)
        {
            var mentions = newNotifications.Where(q => q is { Status: not null, Type: "mention" });
            await HandleMentions(mentions);
        }
    }

    private async Task<bool> HasAlreadyAnswered(Status toot, string myId)
    {
        _logger.LogDebug("Trying to find out if I already answered");
        _logger.LogDebug("Toot: {toot}", toot);

        if (toot.Account.Id == myId)
        {
            return true;
        }
        if (toot.InReplyToId == null) return false;
        var parent = await _client.GetStatus(toot.InReplyToId);
        return await HasAlreadyAnswered(parent, myId);
    }

    private async Task<string?> GetUrlFromToot(Status toot)
    {
        _logger.LogDebug("Trying to find url in {Text}", toot?.Card?.Html);
        _logger.LogDebug("Toot: {toot}", toot);
        if (!string.IsNullOrEmpty(toot.Card?.Url)) return toot.Card.Url;

        if (toot.Card == null)
        {
            // try to find in text when Card is missing
            var doc = new HtmlDocument();
            doc.LoadHtml(toot.Content);
            var urls = new List<string>();
            foreach (var link in doc.DocumentNode.SelectNodes("//a[@href]"))
            {
                if (link.InnerText != null && link.InnerText.StartsWith("http")) return link.InnerText;
            }
        }

        if (toot.InReplyToId == null) return null;
        var parent = await _client.GetStatus(toot.InReplyToId);
        return await GetUrlFromToot(parent);
    }
}