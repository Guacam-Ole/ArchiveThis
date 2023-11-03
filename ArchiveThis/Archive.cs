using ArchiveThis.Models;
using Microsoft.Extensions.Logging;

namespace ArchiveThis
{
    public class Archive
    {
        public Archive(Database database, Store store, ILogger<Archive> logger)
        {
            _database = database;
            _store = store;
            _logger = logger;
        }

        private readonly Database _database;
        private readonly Store _store;
        private readonly ILogger<Archive> _logger;
        private int _storeIntervalMinutes;
        private int _replyIntervalMinutes;
        private int _cleanupIntervalMinutes;
        private int _hashtagIntervalMinutes;
        private const int _requestIntervalMinutes = 1;

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

        private Timer InitTimer(TimerCallback timerCallback, int intervalMinutes) {
            return new Timer(timerCallback, null, 0, 60000*intervalMinutes);
        }

        private void StartTimers()
        {
            InitTimer(RequestTimerCallback, _requestIntervalMinutes);
            InitTimer(StoreTimerCallback, _storeIntervalMinutes);
            InitTimer(ReplyTimerExecute, _replyIntervalMinutes);
            InitTimer(CleanupTimerCallback, _cleanupIntervalMinutes);
            InitTimer(HashtagTimerCallback, _hashtagIntervalMinutes);
        }

        private void HashtagTimerCallback(object? state)
        {
            throw new NotImplementedException();
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
            var newItems = await _database.GetNewItems();
            var openTasks = new List<Task<ResponseItem>>();

            if (newItems.Any())
            {
                _logger.LogDebug("Sending {count} urls to archive", openTasks.Count);
                foreach (var item in newItems) openTasks.Add(_store.GetMastodonResponseForRequest(item));

                var responses = await Task.WhenAll(openTasks);
                _logger.LogDebug("ARchive finished");

                foreach (var response in responses)
                {
                    var item = newItems.First(q => q.Id == response.RequestId);
                    if (response.ResponseCode == Store.ResponseCodes.Error)
                    {
                        item.State = RequestItem.RequestStates.Error;
                        _logger.LogWarning("Could not store '{url}'", item.Url);
                    }
                    else
                    {
                        item.State = RequestItem.RequestStates.Success;
                        item.Url = response.ArchiveUrl;
                    }
                    await _database.UpdateItem(item);
                }
                _logger.LogInformation("Stored new Urls");
            }
            else
            {
                _logger.LogDebug("Nothing new");
            }
        }

        private async void RequestTimerCallback(object? state)
        {
            // TODO: Check Mastodon for mentions

            throw new NotImplementedException();
        }
    }
}