namespace ArchiveThis;

using ArchiveThis.Models;
using Mastonet;
using Mastonet.Entities;
using Newtonsoft.Json;

public class Toot
{
    private MastodonClient _client;
    private readonly Database _database;

    public Toot(Database database, Config.Secrets secrets)
    {
        //  _config = JsonConvert.DeserializeObject<Config>(File.ReadAllText("./config.json")) ?? throw new FileNotFoundException("cannot read config");
        _client = CreateClient(secrets.Instance, secrets.AccessToken);
        _database = database;
    }

    private MastodonClient CreateClient(string instance, string secret)
    {
        return new MastodonClient(instance, secret) ?? throw new Exception("Client could not be created");
    }


    public async Task<Status> SendToot(string content, string replyTo, bool isPrivate)
    {
        return await _client.PublishStatus(content, isPrivate ? Visibility.Private : Visibility.Public, replyTo);
    }

    public async Task GetFeaturedTags(string hashtag)
    {
        var existingConfigs = await _database.GetAllItems<HashtagItem>();
        var hashtagConfig = existingConfigs.FirstOrDefault(q => q.Tag == hashtag) ?? new Models.HashtagItem { Tag = hashtag };
        var options = new ArrayOptions();

        var latest = hashtagConfig.RequestItems.OrderByDescending(q => q.Created).FirstOrDefault();
        if (latest == null)
        {
            options.Limit = 10;
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