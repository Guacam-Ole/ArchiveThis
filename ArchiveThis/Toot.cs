namespace ArchiveThis;

using ArchiveThis.Models;

using HtmlAgilityPack;

using Mastonet;
using Mastonet.Entities;

using Microsoft.Extensions.Logging;

using System.Data;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Xml;

using static System.Net.Mime.MediaTypeNames;

public class Toot
{
    private MastodonClient _client;
    private readonly Database _database;
    private readonly ILogger<Toot> _logger;
    private readonly string[] _doArchiveTriggerWords = new string[] { "ArchiveThisUrl", "!archive", "!url" };

    public Toot(Database database, Config.Secrets secrets, ILogger<Toot> logger)
    {
        _client = CreateClient(secrets.Instance, secrets.AccessToken);
        _database = database;
        _logger = logger;
    }

    private MastodonClient CreateClient(string instance, string secret)
    {
        return new MastodonClient(instance, secret) ?? throw new Exception("Client could not be created");
    }

    public async Task<Status> SendToot(string content, string replyTo, string? replaceId, Visibility visibility)
    {
        if (replaceId == null)
        {
            _logger.LogDebug("🐘->{replyTo}", replyTo);
            return await _client.PublishStatus(content, visibility, replyTo);
        }
        else
        {
            _logger.LogDebug("🐘 <->{replyTo}", replaceId);
            return await _client.EditStatus(replaceId, content);
        }
    }

    private async Task<Status> FavToot(string statusId)
    {
        return await _client.Favourite(statusId);
    }

    private async Task HandleMentions(IEnumerable<Notification> mentions)
    {
        foreach (var mention in mentions.DistinctBy(q => q.Status!.InReplyToId))
        {
            if (mention.Status == null) continue;
            bool hasKeyword = _doArchiveTriggerWords.Any(q => mention.Status.Content.Contains(q, StringComparison.InvariantCultureIgnoreCase));
            if (!hasKeyword)
            {
                if (!string.IsNullOrEmpty(mention.Status.InReplyToId)) await SendToot($"Hey there, @{mention.Account.AccountName}. This is just a really stupid bot. So I do not really understand what you are trying to tell me.\n\n If you want me to put a url into the archive you HAVE TO put any of the following words '{string.Join(',', _doArchiveTriggerWords)}' somewhere in your toot. I also can only find URLs in toots you reply to or your toot", mention.Status.Id, null, Visibility.Direct);
                continue;
            }
            var url = await GetUrlFromToot(mention.Status);
            if (url == null)
            {
                await SendToot($"Sorry, @{mention.Account.AccountName} I did not find any URL in your toot or the ones above yours. I do ONLY archive URLs, not the toot itself", mention.Status.Id, null, mention.Status.Visibility);
                continue;
            }
            var item = new RequestItem
            {
                MastodonId = mention.Status.Id,
                State = RequestItem.RequestStates.Pending,
                RequestedBy = mention.Account.AccountName,
                Url = url,
                Visibility = mention.Status.Visibility
            };
            _logger.LogDebug("Received request to store url '{url}'", url);
            var mastodonResponse = await SendToot($"@{item.RequestedBy} I received your request and will try to send that URL to the archive. \n\nPlease be aware that depending on the waybackmachine this can take HOURS sometimes. No need to resend that request.", mention.Status.Id, null, mention.Status.Visibility);
            if (mastodonResponse != null) item.ResponseId = mastodonResponse.Id;
            await _database.UpsertItem(item);
            await FavToot(mention.Status.Id);
        }
    }

    private async Task HandleFollows(IEnumerable<Notification> follows)
    {
        foreach (var follow in follows)
        {
            await SendToot($"Hey, @{follow.Account.AccountName} Great that you followed me. Just be aware this is NOT necessary if you want me to archive stuff :) \n\nThis is how this works:\n If you want me to archive an url mention me and add one of the following keywords:\n{string.Join(',', _doArchiveTriggerWords)}\n\nWhen I fav your toot you will know that I have seen it. I will then try to find any URL in one of the toots you replied to.\n(I cannot archive mastodon-urls and will ignore any url that has already been archived) ", null, null, Visibility.Private);
        }
    }

    public async Task GetNotifications()
    {
        var newNotifications = await _client.GetNotifications();
        await _client.ClearNotifications();

        if (newNotifications != null)
        {
            var mentions = newNotifications.Where(q => q.Status != null && q.Type == "mention");
            var follows = newNotifications.Where(q => q.Type == "follow");

            await HandleMentions(mentions);
            await HandleFollows(follows);
        }
    }

    private async Task<string?> GetUrlFromToot(Status toot)
    {
        if (!string.IsNullOrEmpty(toot.Card?.Url)) return toot.Card.Url;
        
        if (toot.Card==null)
        {
            // try to find in text when Card is missing
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(toot.Content);
            var urls=new List<string>();    
            foreach (HtmlNode link in doc.DocumentNode.SelectNodes("//a[@href]"))
            {
                if (link.InnerText != null && link.InnerText.StartsWith("http")) return link.InnerText;
            }
        }
        if (toot.InReplyToId == null) return null;
        var parent = await _client.GetStatus(toot.InReplyToId);
        return await GetUrlFromToot(parent);
    }

    public async Task GetFeaturedTags(string hashtag)
    {
        var whoami = await _client.GetCurrentUser();
        var existingConfigs = await _database.GetAllItems<HashtagItem>();
        var hashtagConfig = existingConfigs.FirstOrDefault(q => q.Tag == hashtag) ?? new HashtagItem { Tag = hashtag };
        var options = new ArrayOptions();

        var latest = hashtagConfig.RequestItems.OrderByDescending(q => q.Created).FirstOrDefault();
        if (latest == null)
        {
            options.Limit = 1;
        }
        else
        {
            options.Limit = 100;
            options.SinceId = latest.MastodonId;
        }

        var newEntries = await _client.GetTagTimeline(hashtag, options);
        foreach (var entry in newEntries)
        {
            if (entry.Account?.Id == whoami.Id) continue;  // Don't answer yourself
            var url = entry.Card?.Url;
            if (string.IsNullOrEmpty(url)) continue;

            if (url.Contains("archive.org", StringComparison.OrdinalIgnoreCase)) continue; // No redundancy, please
            hashtagConfig.RequestItems.Add(new RequestItem
            {
                Url = url,
                State = RequestItem.RequestStates.Pending,
                MastodonId = entry.Id,
                Tag = hashtag,
            });
            _logger.LogDebug("Added Hashtag-Request for url '{url}'", url);
        }
        await _database.UpsertItem(hashtagConfig);
    }
}