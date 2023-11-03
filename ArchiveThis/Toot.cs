namespace ArchiveThis;
using Mastonet;
using Mastonet.Entities;
using Newtonsoft.Json;

public class Toot {
    private Config _config;
    private MastodonClient _client;
    private readonly Database _database;

    public Toot(Database database) {
         _config = JsonConvert.DeserializeObject<Config>(File.ReadAllText("./config.json")) ?? throw new FileNotFoundException("cannot read config");
        _client=CreateClient(_config.Instance, _config.Secret);
        _database = database;
    }

    private MastodonClient CreateClient(string instance, string secret) {
        return new MastodonClient(instance, secret) ?? throw new Exception("Client could not be created");
    }

    public async Task CheckForNewTagContents() {
        foreach (var hashtag in _config.HashTags) {
            await GetFeaturedTagsSince(hashtag);
        }
    }

    private async Task GetFeaturedTagsSince(string hashtag) {
        var existingConfigs=await _database.GetHashTagItems();
        var hashtagConfig=existingConfigs.FirstOrDefault(q=>q.HashTag==hashtag)?? new Models.HashtagItem { HashTag=hashtag, RequestItems=new List<Models.RequestItem>()};
        var options=new ArrayOptions();

        var latest=hashtagConfig.RequestItems?.OrderByDescending(q=>q.Created).FirstOrDefault();
        if (latest==null) {
        options.Limit=10;
        } else {
            options.SinceId=latest.MastodonId;
        }

        var newEntries=await _client.GetTagTimeline(hashtag, options);
    }
}