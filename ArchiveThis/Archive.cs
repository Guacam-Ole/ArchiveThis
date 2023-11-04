using System.ComponentModel;
using System.Xml.Serialization;
using ArchiveThis.Models;
using Microsoft.Extensions.Logging;

namespace ArchiveThis
{
    public class Archive
    {
        private readonly Database _database;
        private readonly Store _store;
        private readonly Toot _toot;
        private readonly Config _config;
        private readonly ILogger<Archive> _logger;
        private int _storeIntervalMinutes = 120;
        private int _replyIntervalMinutes = 120;
        private int _cleanupIntervalMinutes = 120;
        private int _hashtagIntervalMinutes = 1;
        private const int _requestIntervalMinutes = 120;

        public Archive(Database database, Toot toot, Store store, Config config, ILogger<Archive> logger)
        {
            _database = database;
            _store = store;
            _toot = toot;
            _config = config;
            _logger = logger;
        }

        public void ArchiveNewRequrest()
        { }

        private void ReadNewRequests()
        { }

        private void StoreRequests()
        { }

        private void WriteReplies()
        { }

        private void WriteSingleReply()
        { }

        private Timer InitTimer(TimerCallback timerCallback, int intervalMinutes)
        {
            return new Timer(timerCallback, null, 0, 60000 * intervalMinutes);
        }


        public void StartTimers()
        {
            InitTimer(RequestTimerCallback, _requestIntervalMinutes);
            InitTimer(StoreTimerCallback, _storeIntervalMinutes);
            InitTimer(ReplyTimerExecute, _replyIntervalMinutes);
            InitTimer(CleanupTimerCallback, _cleanupIntervalMinutes);
            InitTimer(HashtagTimerCallback, _hashtagIntervalMinutes);
        }

        public async Task RespondHashtagResults()
        {
            var hashtagConfigs = await _database.GetAllItems<HashtagItem>();
            foreach (var config in hashtagConfigs)
            {
                var succeededItems = config.RequestItems.Where(q => q.State == RequestItem.RequestStates.Success);
                _logger.LogDebug("sending '{count}' successful archives for '{tag}' back to Mastodon", succeededItems.Count(), config.Tag);
                foreach (var item in succeededItems)
                {
                    var text=$"That Url has been archived as {item.Url}. \n\n#{config.Tag} #ArchiveThis";
                    var mastodonResponse=await _toot.SendToot(text, item.MastodonId, false);
                    item.ResponseId=mastodonResponse?.Id;
                    item.State= RequestItem.RequestStates.Posted;
                }
                await _database.UpsertItem(config);
            }
        }

        public async Task ArchiveUrlsForHashtag()
        {
            await _toot.RetrieveNewTagContents();
            var hashtagConfigs = await _database.GetAllItems<HashtagItem>();
            foreach (var hashtagConfig in hashtagConfigs)
            {
                foreach (var requestItem in hashtagConfig.RequestItems.Where(q => q.State == RequestItem.RequestStates.Pending))
                {
                    if (string.IsNullOrEmpty(requestItem.Url))
                    {
                        _logger.LogDebug("requestitem '{id}' has no Url", requestItem.MastodonId);
                        requestItem.State = RequestItem.RequestStates.InvalidUrl;
                        continue;
                    }
                    var matchingSite = _config.Sites.FirstOrDefault(q => requestItem.Url.Contains(q.Domain, StringComparison.InvariantCultureIgnoreCase));
                    if (matchingSite == null)
                    {
                        _logger.LogDebug("requestitem '{id}' has no valid Url for hashtag operation", requestItem.MastodonId);
                        requestItem.State = RequestItem.RequestStates.InvalidUrl;
                        continue;
                    }
                    requestItem.Site = matchingSite;
                }
                hashtagConfig.RequestItems = await StoreBunchOfItems(hashtagConfig.RequestItems);
                foreach (var item in hashtagConfig.RequestItems.Where(q => q.State == RequestItem.RequestStates.Success))
                {
                    if (item.Site == null || item.Url == null) continue;
                    if (await UrlIsFaultyOrHasContent(item.Url, item.Site.FailureContent)) item.State = RequestItem.RequestStates.AlreadyBlocked;
                }
                await _database.UpsertItem(hashtagConfig);
            }
        }

        private async void HashtagTimerCallback(object? state)
        {
            await ArchiveUrlsForHashtag();
        }

        private async Task<bool> UrlIsFaultyOrHasContent(string url, string errorContent)
        {
            HttpClient client = new HttpClient();
            var checkingResponse = await client.GetAsync(url);
            if (!checkingResponse.IsSuccessStatusCode)
            {
                return true;
            }
            var content = await checkingResponse.Content.ReadAsStringAsync();
            return content.Contains(errorContent, StringComparison.InvariantCultureIgnoreCase);
        }

        private async void CleanupTimerCallback(object? state)
        {
            await _database.DeleteFinishedItems();
        }

        private async void ReplyTimerExecute(object? state)
        {
            // TODO: Reply responses to Mastodon
            throw new NotImplementedException();
        }

        private async void StoreTimerCallback(object? state)
        {
            var newItems = await _database.GetNewRequestItems();
            foreach (var item in await StoreBunchOfItems(newItems))
            {
                await _database.UpsertItem(item);
            }
        }

        private async Task<List<RequestItem>> StoreBunchOfItems(List<RequestItem> items)
        {
            var openTasks = new List<Task<ResponseItem>>();

            if (!items.Any(q => q.State == RequestItem.RequestStates.Pending))
            {
                return items;
            }
            _logger.LogDebug("Sending {count} urls to archive", items.Count);
            foreach (var item in items.Where(q => q.State == RequestItem.RequestStates.Pending))
            {
                item.State = RequestItem.RequestStates.Running;
                openTasks.Add(_store.GetMastodonResponseForRequest(item));
            }

            var responses = await Task.WhenAll(openTasks);
            _logger.LogDebug("ARchive finished");

            foreach (var response in responses)
            {
                try
                {
                    var item = items.First(q => q.MastodonId == response.RequestId);
                    if (response.ResponseCode == Store.ResponseCodes.Error)
                    {
                        item.State = RequestItem.RequestStates.Error;
                        _logger.LogWarning("Could not store '{url}'", item.Url);
                    }
                    else
                    {
                        item.State = RequestItem.RequestStates.Success;
                        item.Url = response.ArchiveUrl;
                        _logger.LogDebug("stored '{url}'", item.Url);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error on retrieving response for '{id}'", response.RequestId);
                }
            }
            _logger.LogInformation("Stored new Urls");
            return items;
        }

        private async void RequestTimerCallback(object? state)
        {
            // TODO: Check Mastodon for mentions

            throw new NotImplementedException();
        }
    }
}