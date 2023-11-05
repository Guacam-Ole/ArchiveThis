using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Serialization;
using ArchiveThis.Config;
using ArchiveThis.Models;
using Microsoft.Extensions.Logging;

namespace ArchiveThis
{
    public class Archive
    {
        private readonly Database _database;
        private readonly Store _store;
        private readonly Toot _toot;
        private readonly Config.Config _config;
        private readonly ILogger<Archive> _logger;

        public Archive(Database database, Toot toot, Store store, Config.Config config, ILogger<Archive> logger)
        {
            _database = database;
            _store = store;
            _toot = toot;
            _config = config;
            _logger = logger;
        }


        private Timer InitTimer(TimerCallback timerCallback, SingleTimer timerSettings)
        {
            return new Timer(timerCallback, null, timerSettings.Delay, timerSettings.Interval);
        }

        public void StartTimers()
        {
            InitTimer(CheckMastodonRequestsTimerCallback, _config.Timers.CheckForMastodonRequests);
            InitTimer(StoreUrlsInArchiveTimerCallback, _config.Timers.SendRequestsToArchive);
            InitTimer(ReplyTimerCallback, _config.Timers.SendRepliesToMastodon);
            InitTimer(CleanupTimerCallback, _config.Timers.CleanUp);
            InitTimer(HashtagTimerCallback, _config.Timers.HashTagCheck);
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
                    var text = $"That Url has been archived as {item.ArchiveUrl}. \n\n#{config.Tag} #ArchiveThis";
                    var mastodonResponse = await _toot.SendToot(text, item.MastodonId, false);
                    item.ResponseId = mastodonResponse?.Id;
                    item.State = RequestItem.RequestStates.Posted;
                }
                await _database.UpsertItem(config);
            }
        }

        public async Task ArchiveUrlsForHashtag()
        {
            foreach (var hashtag in _config.HashTags)
            {
                await _toot.GetFeaturedTags(hashtag);
                var hashtagConfigs = await _database.GetAllItems<HashtagItem>();
                var hashtagConfig = hashtagConfigs.FirstOrDefault(q => q.Tag == hashtag);
                if (hashtagConfig == null) continue;
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
                    if (await _store.UrlIsFaultyOrHasContent(item.Url, item.Site.FailureContent)) item.State = RequestItem.RequestStates.AlreadyBlocked;
                }
                await _database.UpsertItem(hashtagConfig);
            }
        }

        private async Task<List<RequestItem>> FillRequestsWithExistingResults(List<RequestItem> items)
        {
            var oldRequests = await _database.GetAllItems<RequestItem>();
            var oldHashtags = await _database.GetAllItems<HashtagItem>();

            var successFullRequests = oldRequests.Where(q => q.ArchiveUrl != null).ToList();
            foreach (var hashtag in oldHashtags)
            {
                var successfullHashtagRequests = hashtag.RequestItems.Where(q => q.ArchiveUrl != null);
                if (successfullHashtagRequests.Any()) successFullRequests.AddRange(successfullHashtagRequests);
            }

            foreach (var item in items.Where(q => q.State == RequestItem.RequestStates.Pending))
            {
                var successfulMatch = successFullRequests.FirstOrDefault(q => q.Url.Equals(item.Url, StringComparison.InvariantCultureIgnoreCase));
                if (successfulMatch != null)
                {
                    item.State = RequestItem.RequestStates.Success;
                    item.ArchiveUrl = successfulMatch.ArchiveUrl;
                }
            }
            return items;
        }

        private async Task<List<RequestItem>> StoreBunchOfItems(List<RequestItem> items)
        {
            var openTasks = new List<Task<ResponseItem>>();
            await FillRequestsWithExistingResults(items);

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
                        item.ArchiveUrl = response.ArchiveUrl;
                        _logger.LogDebug("stored '{url}' as '{archive}'", item.Url, item.ArchiveUrl);
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

        private async void HashtagTimerCallback(object? state)
        {
            _logger.LogInformation("Checking Hashtag contents");
            await ArchiveUrlsForHashtag();
        }

        private async void CleanupTimerCallback(object? state)
        {
            var successStates = new List<RequestItem.RequestStates> {
                RequestItem.RequestStates.Pending,
                RequestItem.RequestStates.Running,
                RequestItem.RequestStates.Posted,
                RequestItem.RequestStates.Success
            };
            var errorStates = new List<RequestItem.RequestStates> {
                RequestItem.RequestStates.AlreadyBlocked,
                RequestItem.RequestStates.Error,
                RequestItem.RequestStates.InvalidUrl
            };

            _logger.LogInformation("Cleaning up");
            await _database.DeleteFinishedItems(successStates, DateTime.Now.AddDays(-_config.DeleteSuccessFulRequestAfterDays));
            await _database.DeleteFinishedItems(errorStates, DateTime.Now.AddDays(-_config.DeleteFailedRequestsAfterDays));

            foreach (var hasthags in await _database.GetAllItems<HashtagItem>())
            {
                hasthags.RequestItems.RemoveAll(q => successStates.Contains(q.State) && q.Created<DateTime.Now.AddDays(-_config.DeleteSuccessFulRequestAfterDays) );
                hasthags.RequestItems.RemoveAll(q => errorStates.Contains(q.State) && q.Created<DateTime.Now.AddDays(-_config.DeleteFailedRequestsAfterDays) );
            }
        }

        private async void ReplyTimerCallback(object? state)
        {
            _logger.LogInformation("Replying to Mastodon");

            throw new NotImplementedException();
        }

        private async void StoreUrlsInArchiveTimerCallback(object? state)
        {
            _logger.LogInformation("Archiving Urls");
            var newItems = await _database.GetNewRequestItems();
            foreach (var item in await StoreBunchOfItems(newItems))
            {
                await _database.UpsertItem(item);
            }
        }

        private async void CheckMastodonRequestsTimerCallback(object? state)
        {
            _logger.LogInformation("Checking Mastodon for new Requests");

            throw new NotImplementedException();
        }
    }
}