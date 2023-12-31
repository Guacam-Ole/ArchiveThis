using ArchiveThis.Config;
using ArchiveThis.Models;

using Microsoft.Extensions.Logging;

using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace ArchiveThis
{
    public class Archive
    {
        private readonly Database _database;
        private readonly Store _store;
        private readonly Toot _toot;
        private readonly Config.Config _config;
        private readonly ILogger<Archive> _logger;
        private List<Timer> _timers = new();
        private Dictionary<string, DateTime> _timerRuns = new();
        private DateTime? _lastWatchDogRun = null;

        public Archive(Database database, Toot toot, Store store, Config.Config config, ILogger<Archive> logger)
        {
            _database = database;
            _store = store;
            _toot = toot;
            _config = config;
            _logger = logger;
        }

        public async Task RespondHashtagResults()
        {
            var hashtagConfigs = await _database.GetAllItems<HashtagItem>();
            foreach (var config in hashtagConfigs)
            {
                var succeededItems = config.RequestItems.Where(q => q.State == RequestItem.RequestStates.Success);
                if (!succeededItems.Any()) continue;
                _logger.LogDebug("🐘 sending '{count}' successful archives for #{tag} back", succeededItems.Count(), config.Tag);
                foreach (var item in succeededItems)
                {
                    try
                    {
                        var text = $"That Url has been archived as {item.ArchiveUrl}. \n\n #ArchiveThis";
                        var mastodonResponse = await _toot.SendToot(text, item.MastodonId,  item.ResponseId, Mastonet.Visibility.Unlisted);
                        item.ResponseId = mastodonResponse?.Id;
                        item.State = RequestItem.RequestStates.Posted;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed Hashtagresponse, {item.Id}");
                        item.State = RequestItem.RequestStates.GivingUp;
                    }
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
                        requestItem.State = RequestItem.RequestStates.InvalidUrl;
                        _logger.LogInformation("Url was empty.");
                        continue;
                    }
                    var matchingSite = _config.Sites.FirstOrDefault(q => requestItem.Url.Contains(q.Domain, StringComparison.InvariantCultureIgnoreCase));
                    if (matchingSite == null)
                    {
                        requestItem.State = RequestItem.RequestStates.InvalidUrl;
                        _logger.LogDebug("No matching site for '{url}' found", requestItem.Url);
                        continue;
                    }
                    requestItem.Site = matchingSite;
                    requestItem.Updated = DateTime.Now;
                    _logger.LogDebug("Added url '{url}' for hashtag", requestItem.Url);
                }
                hashtagConfig.RequestItems = await StoreBunchOfItems(hashtagConfig.RequestItems);
                Thread.Sleep(10000);
                await CheckHashtagItems(hashtagConfig, RequestItem.RequestStates.Success);
                await RespondHashtagResults();
            }
        }

        private async Task CheckHashtagItems(HashtagItem hashtagConfig, RequestItem.RequestStates state)
        {
            var items = hashtagConfig.RequestItems.Where(q => q.State == state && q.ErrorCount < 5);
            if (items.Any()) _logger.LogInformation("^Checking Hashtagitems by state '{state}' for '{tag}': {count} items found", state, items.Count(), hashtagConfig.Tag);
            foreach (var item in items)
            {
                if (item.Site == null || item.ArchiveUrl == null || item.Url == null) continue;
                try
                {
                    if (await _store.UrlHasContent(item.ArchiveUrl, item.Site.FailureContent))
                    {
                        item.State = RequestItem.RequestStates.AlreadyBlocked;
                        _logger.LogWarning("💣 Url '{url}' is already blocked -  {archive}", item.Url, item.ArchiveUrl);
                    }
                    else
                    {
                        item.State = RequestItem.RequestStates.Success;
                        _logger.LogDebug("That was a success -  {archive}", item.ArchiveUrl);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed checking url '{url}', {message}", item.ArchiveUrl, ex.Message);
                    item.ErrorCount++;
                    item.State = RequestItem.RequestStates.Error;
                }
            }
            await _database.UpsertItem(hashtagConfig);
        }

        private async Task RecheckUrls()
        {
            foreach (var hashtag in _config.HashTags)
            {
                await _toot.GetFeaturedTags(hashtag);
                var hashtagConfigs = await _database.GetAllItems<HashtagItem>();
                var hashtagConfig = hashtagConfigs.FirstOrDefault(q => q.Tag == hashtag);
                if (hashtagConfig == null) continue;

                await CheckHashtagItems(hashtagConfig, RequestItem.RequestStates.Error);
            
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
            _logger.LogInformation("🤖 Sending {count} urls to archive", items.Count);
            foreach (var item in items.Where(q => q.State == RequestItem.RequestStates.Pending))
            {
                item.State = RequestItem.RequestStates.Running;
                openTasks.Add(_store.GetResponseFromSnapshotRequest(item));
                await _database.UpsertItem(item);
            }

            var responses = await Task.WhenAll(openTasks);
            _logger.LogInformation("🤖 Archive finished");

            foreach (var response in responses)
            {
                try
                {
                    var item = items.First(q => q.MastodonId == response.RequestId);
                    if (response.ResponseCode == Store.ResponseCodes.Error)
                    {
                        item.State = RequestItem.RequestStates.Error;
                        _logger.LogWarning("🤖 Could not store '{url}'", item.Url);
                    }
                    else
                    {
                        item.State = RequestItem.RequestStates.Success;
                        item.ArchiveUrl = response.ArchiveUrl;
                        _logger.LogInformation("🤖 stored '{url}' as '{archive}'", item.Url, item.ArchiveUrl);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "🤖 Error on retrieving response for '{id}'", response.RequestId);
                }
            }
            return items;
        }



        public async Task CheckNotifications()
        {
            try
            {
                await _toot.GetNotifications();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "👁 Cannot get notifications");
            }
        }

        private async Task SendReplies()
        {
            var replyItems = await _database.GetItemsForReply();
            foreach (var item in replyItems.Where(q=>q.State!= RequestItem.RequestStates.Running))
            {
                try
                {
                    _logger.LogDebug("🐘 Sending response for item '{item}' ", item);
                    switch (item.State)
                    {
                        case RequestItem.RequestStates.Success:
                            item.OldState = item.State;
                            await SendMastodonResponse(item, $"@{item.RequestedBy} Here is your archived URL: {item.ArchiveUrl}.");
                            _logger.LogDebug("🐘 Sent Success response for item '{item}' ", item);
                            break;

                        case RequestItem.RequestStates.Error:
                            await SendMastodonResponse(item, $"I'm sorry, @{item.RequestedBy} , I cannot do that. \n (Archiving failed for that url)");
                            _logger.LogDebug("🐘 Sent Error response for item '{item}' ", item);
                            break;

                        case RequestItem.RequestStates.AlreadyBlocked:
                            await SendMastodonResponse(item, $"I'm sorry, @{item.RequestedBy} , looks like we are too late. \n (The Paywall kicked in)");
                            _logger.LogDebug("🐘 Sent Blocked response for item '{item}' ", item);
                            break;

                        case RequestItem.RequestStates.InvalidUrl:
                            await SendMastodonResponse(item, $"You are a funny guy, @{item.RequestedBy}. \n (There was no URL anywhere)");
                            _logger.LogDebug("🐘 Sent Invalid response for item '{item}' ", item);
                            break;

                        default:
                            continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed sending toot. Will not retry");
                    item.State = RequestItem.RequestStates.GivingUp;
                }
            }
        }

        private async Task SendMastodonResponse(RequestItem item, string text)
        {
            try
            {
                var responseStatus = await _toot.SendToot(text, item.MastodonId, item.ResponseId, item.Visibility);
                if (responseStatus == null)
                {
                    _logger.LogError("🐘 Cannot reply to mastodon. Giving up");
                    item.State = RequestItem.RequestStates.GivingUp;
                    await _database.UpsertItem(item);
                }
                else
                {
                    item.ResponseId = responseStatus.Id;
                    item.OldState = item.State;
                    item.State = RequestItem.RequestStates.Posted;
                    item.Updated = DateTime.Now;
                    await _database.UpsertItem(item);
                    _logger.LogDebug("sent Message '{msg}'", text);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🐘 Cannot reply to mastodon. Exception");
                item.State = RequestItem.RequestStates.GivingUp;
                await _database.UpsertItem(item);
            }
        }

        private void InitTimer(TimerCallback timerCallback, SingleTimer timerSettings)
        {
            _logger.LogDebug("Init timer {timer}, {name}", timerSettings, timerCallback.Method.Name);
            _timers.Add(new Timer(timerCallback, null, timerSettings.Delay, timerSettings.Interval));
        }

        public void StartTimers()
        {
            var copyright = FileVersionInfo.GetVersionInfo(Assembly.GetEntryAssembly().Location).LegalCopyright;

            _logger.LogInformation("⌚ Starting Timers. {copyright}", copyright);
            InitTimer(CheckMastodonRequestsTimerCallback, _config.Timers.CheckForMastodonRequests);
            InitTimer(StoreUrlsInArchiveTimerCallback, _config.Timers.SendRequestsToArchive);
            InitTimer(ReplyTimerCallback, _config.Timers.SendRepliesToMastodon);
            InitTimer(CleanupTimerCallback, _config.Timers.CleanUp);
            InitTimer(HashtagTimerCallback, _config.Timers.HashTagCheck);
            InitTimer(WatchDogCallBack, _config.Timers.WatchDog);
            InitTimer(ReCheckCallBack, _config.Timers.RecheckArchiveUrls);
            _logger.LogInformation("⌚ Up and running");
        }

        private void WatchDogSince(ref StringBuilder output, DateTime? startDate, List<RequestItem> requestItems, List<RequestItem> hashtagItems)
        {
            string lastRunHumanReadable = "program start";
            if (startDate != null) lastRunHumanReadable = startDate.Value.ToString();

            IEnumerable<RequestItem> filteerdRequestItems = requestItems;
            IEnumerable<RequestItem> filteredHashtagItems = hashtagItems;
            output.AppendLine($"    Stats since {lastRunHumanReadable}");
            if (startDate != null)
            {
                filteerdRequestItems = requestItems.Where(q => q.Updated != null && q.Updated >= startDate || q.Created >= startDate).ToList();
                filteredHashtagItems = hashtagItems.Where(q => q.Updated != null && q.Updated >= startDate || q.Created >= startDate).ToList();
            }

            output.AppendLine("        User Requests by state:");
            foreach (var userRequestByState in filteerdRequestItems.GroupBy(q => q.State))
            {
                if (userRequestByState.Any())
                {
                    output.AppendLine($"            {userRequestByState.First().State}:{userRequestByState.Count()}");
                }
            }

            output.AppendLine("        Hashtag Requests by state:");
            foreach (var hashTagRequestByState in filteredHashtagItems.GroupBy(q => q.State))
            {
                if (hashTagRequestByState.Any())
                {
                    output.AppendLine($"            {hashTagRequestByState.First().State}:{hashTagRequestByState.Count()}");
                }
            }
        }

        private void ReCheckCallBack(object? state)
        {
            try
            {
                RecheckUrls().Wait();
                AddTimerRun();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in recheckTimer");
            }
        }

        private async void WatchDogCallBack(object? state)
        {
            try
            {
                var output = new StringBuilder();
                output.AppendLine("---Stats---");
                output.AppendLine("Last Timer Runs:");
                foreach (var timerStats in _timerRuns)
                {
                    output.AppendLine($"    {timerStats.Value.ToString("HH:mm:ss")}    {timerStats.Key}");
                }

                var allRequests = await _database.GetAllItems<RequestItem>();
                var allHashTags = await _database.GetAllItems<HashtagItem>();

                output.AppendLine("    Totals:");
                output.AppendLine($"        User-Requests:{allRequests.Count}");
                output.AppendLine($"        HashTag-Configs:{allHashTags.Count}");
                output.AppendLine($"        HashTag-Requests:{allHashTags.Sum(q => q.RequestItems.Count)}");

                var allHashtagRequests = allHashTags.SelectMany(q => q.RequestItems).ToList();
                WatchDogSince(ref output, null, allRequests, allHashtagRequests);
                WatchDogSince(ref output, DateTime.Today.AddDays(-30), allRequests, allHashtagRequests);
                WatchDogSince(ref output, DateTime.Today.AddDays(-7), allRequests, allHashtagRequests);
                WatchDogSince(ref output, DateTime.Today.AddDays(-1), allRequests, allHashtagRequests);
                WatchDogSince(ref output, _lastWatchDogRun, allRequests, allHashtagRequests);

                _lastWatchDogRun = DateTime.Now;
                _logger.LogInformation(output.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🐶 error in watchdog");
            }
        }

        private void AddTimerRun([CallerMemberName] string timerName = "unknown")
        {
            _timerRuns[timerName] = DateTime.Now;
        }

        private void CheckMastodonRequestsTimerCallback(object? state)
        {
            try
            {
                CheckNotifications().Wait();
                AddTimerRun();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🐘 Error getting Notifications");
            }
        }

        private void StoreUrlsInArchiveTimerCallback(object? state)
        {
            try
            {
                var newItems = _database.GetNewRequestItems().Result;
                foreach (var item in StoreBunchOfItems(newItems).Result)
                {
                    item.Updated = DateTime.Now;
                    _database.UpsertItem(item).Wait();
                }
                AddTimerRun();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🤖 Error storing URL");
            }
        }

        private async void ReplyTimerCallback(object? state)
        {
            try
            {
                await SendReplies();
                await RespondHashtagResults();
                AddTimerRun();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🐘 Error replying to mastodon");
            }
        }

        private void CleanupTimerCallback(object? state)
        {
            try
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

                Task.WaitAll(
                 _database.DeleteFinishedItems(successStates, DateTime.Now.AddDays(-_config.DeleteSuccessFulRequestAfterDays)),
                 _database.DeleteFinishedItems(errorStates, DateTime.Now.AddDays(-_config.DeleteFailedRequestsAfterDays))
                );

                foreach (var hasthags in _database.GetAllItems<HashtagItem>().Result)
                {
                    hasthags.RequestItems.RemoveAll(q => successStates.Contains(q.State) && q.Created < DateTime.Now.AddDays(-_config.DeleteSuccessFulRequestAfterDays));
                    hasthags.RequestItems.RemoveAll(q => errorStates.Contains(q.State) && q.Created < DateTime.Now.AddDays(-_config.DeleteFailedRequestsAfterDays));
                }
                AddTimerRun();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🧹 Error cleaning up");
            }
        }

        private void HashtagTimerCallback(object? state)
        {
            try
            {
                ArchiveUrlsForHashtag().Wait();
                AddTimerRun();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "#️ Error on Hashtagtimer");
            }
        }
    }
}