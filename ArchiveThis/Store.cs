﻿using ArchiveThis.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

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

        public async Task<ResponseItem> GetResponseFromSnapshotRequest(RequestItem requestItem)
        {
            var responseItem = new ResponseItem
            {
                RequestId = requestItem.MastodonId,
                ResponseCode = ResponseCodes.Error
            };
            try
            {
                if (requestItem.Url != null)
                {
                    bool urlIsValid = Uri.TryCreate(requestItem.Url, UriKind.Absolute, out Uri uriResult) && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
                    if (!urlIsValid)
                    {
                        _logger.LogWarning("Invalid url '{url}' ", requestItem.Url);
                        return responseItem;
                    }

                    var response = await TakeSnapshotFrom(requestItem.Url);
                    responseItem.ArchiveUrl = response.Value;
                    responseItem.ResponseCode = response.Key;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,"Cannot store url '{url}'", requestItem?.Url);
            }
            return responseItem;
        }

        public async Task<bool> UrlHasContent(string url, List<string> errorContent)
        {
            try
            {
                bool urlIsValid = Uri.TryCreate(url, UriKind.Absolute, out Uri? uriResult) && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
                if (!urlIsValid)
                {
                    throw new UriFormatException($"Invalid Url '{url}'");
                }
                HttpClient client = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };
                var checkingResponse = await client.GetAsync(url);
                if (!checkingResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Unsuccessful request for url '{url}': {checkingResponse.StatusCode}");
                }
                var content = await checkingResponse.Content.ReadAsStringAsync();
                return errorContent.Any(q=>content.Contains(q, StringComparison.InvariantCultureIgnoreCase)) || content.Contains("The Wayback Machine has not archived that URL", StringComparison.InvariantCultureIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "failed retrieving content: {msg}",ex.Message);
                throw new Exception($"failed checking url '{url}' for contents", ex);
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
                //     _logger.LogDebug("Stored '{url}' as '{archive}'", urlToStore, saveResponse);
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