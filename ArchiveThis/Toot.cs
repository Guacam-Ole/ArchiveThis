namespace ArchiveThis;

using System.Data;
using ArchiveThis.Models;
using Mastonet;
using Mastonet.Entities;

using Microsoft.Extensions.Logging;

public class Toot
{
    private MastodonClient _client;
    private readonly Database _database;
    private readonly ILogger<Toot> _logger;
    private readonly string[] _doArchiveTriggerWords=new  string []{ "ArchiveThisUrl", "!archive", "!url"};

    public Toot(Database database, Config.Secrets secrets, ILogger<Toot> logger)
    {
        _client = CreateClient(secrets.Instance, secrets.AccessToken);
        _database = database;
        _logger=logger;
    }

    private MastodonClient CreateClient(string instance, string secret)
    {
        return new MastodonClient(instance, secret) ?? throw new Exception("Client could not be created");
    }


    public async Task<Status> SendToot(string content, string replyTo, Visibility visibility)
    {
        _logger.LogDebug("🐘->{replyTo}",replyTo);
        return await _client.PublishStatus(content, visibility, replyTo);
    }
    public async Task<Status> SendToot(string content, string replyToId, bool isPrivate) {
        return await SendToot(content, replyToId, isPrivate ? Visibility.Private : Visibility.Public);
    }

    private async Task<Status> FavToot(string statusId) {
        return await _client.Favourite(statusId);
    }

    public async Task GetMentions() {
        var newNotificaitons=await _client.GetNotifications();
        if (newNotificaitons!=null) {
            
            foreach (var notification in newNotificaitons) {
                switch (notification.Type) {
                    case "mention":
                    if (notification.Status==null) continue;
                        bool hasKeyword=_doArchiveTriggerWords.Any(q=>notification.Status.Content.Contains(q, StringComparison.InvariantCultureIgnoreCase));
                        if (!hasKeyword) {
                            if (!string.IsNullOrEmpty(notification.Status.InReplyToId)) await SendToot($"Hey there, @{notification.Account.AccountName}. This is just a really stupid bot. So I do not really understand what you are trying to tell me.\n\n If you want me to put a url into the archive you HAVE TO put any of the following words '{string.Join(',',_doArchiveTriggerWords)}' somewhere in your toot", notification.Status.Id, Visibility.Direct);
                            continue;
                        }
                        var url=await GetUrlFromToot(notification.Status);
                        if (url==null) {
                            await SendToot($"Sorry, @{notification.Account.AccountName} I did not find any URL in your message or one of the messages above. I do ONLY archive URLs, not the toot itself", notification.Status.Id, notification.Status.Visibility);
                            continue;
                        }

                        await _database.UpsertItem(new RequestItem {
                            MastodonId=notification.Status.Id, 
                            State= RequestItem.RequestStates.Pending, 
                            RequestedBy=notification.Account.AccountName,
                            Url=url,
                            Visibility= notification.Status.Visibility
                        });
                        await FavToot(notification.Status.Id);

                    break;
                    case "follow":
                        await SendToot($"Hey, @{notification.Account.AccountName} Great that you followed me. Just be aware this is NOT necessary if you want me to archive stuff :)", null, true);
                    break;
                }
            }
        }
        await _client.ClearNotifications();
    }

    private async Task<string?> GetUrlFromToot(Status toot) {
        if (!string.IsNullOrEmpty(toot.Card?.Url)) return toot.Card.Url;
        if (toot.InReplyToId==null) return null;
        var parent = await _client.GetStatus(toot.InReplyToId);
        return await GetUrlFromToot(parent);
    }

    public async Task GetFeaturedTags(string hashtag)
    {
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
            options.Limit=100;
            options.SinceId = latest.MastodonId;
        }

        var newEntries = await _client.GetTagTimeline(hashtag, options);
        foreach (var entry in newEntries)
        {
            var url = entry.Card?.Url;
            hashtagConfig.RequestItems.Add(new RequestItem
            {
                Url = url,
                State = RequestItem.RequestStates.Pending,
                MastodonId = entry.Id,
                Tag = hashtag,
            });
        }
        await _database.UpsertItem(hashtagConfig);
    }
}