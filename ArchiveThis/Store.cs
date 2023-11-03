using Microsoft.Extensions.Logging;

namespace ArchiveThis
{
    public class Store
    {
        private readonly ILogger<Store> _logger;
        
        public enum ResponseCodes
        { AlreadyExists, Stored, Error };

        public Store(ILogger<Store> logger)
        {
            _logger = logger;
        }

        public async Task<ResponseItem> GetMastodonResponseForRequest(RequestItem requestItem)
        {
            var responseItem=new ResponseItem  {
                RequestId=requestItem.Id,
                ResponseCode= ResponseCodes.Error
            };

            if (requestItem.Url!=null) {
                var response = await TakeSnapshotFrom(requestItem.Url);
                responseItem.ArchiveUrl=response.Value;
                responseItem.ResponseCode=response.Key;
            }
            return responseItem;
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