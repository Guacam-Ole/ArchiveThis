using Microsoft.Extensions.Logging;

namespace ArchiveThis
{
    public class Store
    {
        private readonly ILogger<Store> _logger;
        private ResponseItem
        private enum ResponseCodes
        { AlreadyExists, Stored, Error };

        public Store(ILogger<Store> logger)
        {
            _logger = logger;
        }

             public async Task<ResponseItem> GetMastodonResponseForRequest(RequestItem requestItem)
        {
            if (requestItem.Url==null) return null;
            var response = await TakeSnapshotFrom(requestItem.Url);
            switch (response.Key)
            {
                case ResponseCodes.Error:
                    return new KeyValuePair<long, string?>(replyToToot, $"Sorry. Could not store that URL. Will not retry, either");
                case ResponseCodes.AlreadyExists:
                    return new KeyValuePair<long, string?>(replyToToot, $"That URL had already been stored into the archive before. Here it is nonetheless:\n\n{response.Value}");
                case ResponseCodes.Stored:
                    return new KeyValuePair<long, string?>(replyToToot, $"URL has been archived as:\n\n{response.Value}");
                default:
                    return new KeyValuePair<long, string?>(replyToToot, null);
            }
        }

        private async Task<KeyValuePair<ResponseCodes, string?>> TakeSnapshotFrom(string urlToStore)
        {
            var waybackClient = new WaybackMachineWrapper.WaybackClient();
            try
            {
                var uri = new Uri(urlToStore);
                var storedSite = await waybackClient.AvailableAsync(uri);
                var closestSnapShot = storedSite?.archived_snapshots?.closest;
                if (closestSnapShot != null)
                {
                    _logger.LogDebug("'{url}' was already stored as '{archive}' at '{timestamp}'", urlToStore, closestSnapShot.url, closestSnapShot.timestamp);
                    return new KeyValuePair<ResponseCodes, string?>(ResponseCodes.AlreadyExists, closestSnapShot.url.ToString());
                }
                var saveResponse = await waybackClient.SaveAsyncV2(uri, false);
                if (saveResponse == null)
                {
                    _logger.LogError("Cannot store URL '{url}'", urlToStore);
                    return new KeyValuePair<ResponseCodes, string?>(ResponseCodes.Error, null);
                }
                _logger.LogDebug("Stored '{url}' as '{archive}'", urlToStore, saveResponse);
                return new KeyValuePair<ResponseCodes, string?>(ResponseCodes.Stored, saveResponse.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot store URL '{url}'", urlToStore);
                return new KeyValuePair<ResponseCodes, string?>(ResponseCodes.Error, ex.ToString());
            }
        }
    }
}