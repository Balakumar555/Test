using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Test.Models.Dto;
using Test.Services.Interfaces;

namespace Test.Services
{
    /// <summary>
    /// Fetches NIFTY 50 candle data from the Groww delayed charting API.
    /// The candle interval is read from GrowwApi:IntervalInMinutes (default 1).
    /// To switch to a live/authenticated feed, implement a new class that also
    /// implements <see cref="IMarketDataProvider"/> and register it in Program.cs.
    /// </summary>
    public class GrowwMarketDataProvider : IMarketDataProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GrowwMarketDataProvider> _logger;

        // Named client registered in Program.cs
        private const string HttpClientName = "GrowwClient";

        public GrowwMarketDataProvider(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<GrowwMarketDataProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<GrowwChartResponseDto?> GetRawDataAsync(long startMs, long endMs)
        {
            var baseUrl = _configuration["GrowwApi:BaseUrl"]
                ?? "https://groww.in/v1/api/charting_service/v2/chart/delayed";

            // Read interval from config (default 1 minute)
            int intervalMinutes = _configuration.GetValue<int>("GrowwApi:IntervalInMinutes", 1);

            // Build URL:
            // GET /exchange/NSE/segment/CASH/NIFTY?endTimeInMillis=...&intervalInMinutes=1&startTimeInMillis=...
            var url = $"{baseUrl.TrimEnd('/')}/exchange/NSE/segment/CASH/NIFTY" +
                      $"?endTimeInMillis={endMs}&intervalInMinutes={intervalMinutes}&startTimeInMillis={startMs}";

            _logger.LogInformation("Fetching Groww chart data: {Url}", url);

            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);

                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Groww API returned non-success status {StatusCode} for URL {Url}",
                        response.StatusCode, url);
                    return null;
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var dto = await response.Content.ReadFromJsonAsync<GrowwChartResponseDto>(options);

                if (dto == null)
                {
                    _logger.LogWarning("Groww API response could not be deserialized.");
                    return null;
                }

                _logger.LogInformation(
                    "Groww API returned {Count} candles.", dto.Candles?.Length ?? 0);

                return dto;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error while calling Groww API.");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Groww API request timed out.");
                return null;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize Groww API response.");
                return null;
            }
        }
    }
}
