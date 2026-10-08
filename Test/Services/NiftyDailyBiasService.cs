using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Test.Models.Dto;
using Test.Models.Signal;
using Test.Services.Interfaces;

namespace Test.Services
{
    /// <summary>
    /// Fetches the PREVIOUS TRADING DAY's 1-day (interval=1440) NIFTY 50 candle
    /// from the Groww delayed API, computes wick structure, and returns:
    ///   - PDH / PDC / PDL
    ///   - Daily bias (Bullish / Bearish / Neutral)
    ///
    /// The result is cached under key "nifty_bias_{date}" for the current trading day.
    /// A new API call is made at most once per calendar day.
    /// </summary>
    public class NiftyDailyBiasService : INiftyDailyBiasService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _config;
        private readonly IMemoryCache _cache;
        private readonly ILogger<NiftyDailyBiasService> _logger;

        private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);
        private static readonly TimeOnly MarketOpen = new(9, 15);
        private static readonly TimeOnly RegularEnd  = new(15, 10);

        private const string HttpClientName = "GrowwClient";
        private const int    DailyIntervalMinutes = 1440;   // 1-day candle
        private const int    LookbackCalendarDays  = 7;      // fetch last 7 days, pick previous trading day

        public NiftyDailyBiasService(
            IHttpClientFactory httpFactory,
            IConfiguration config,
            IMemoryCache cache,
            ILogger<NiftyDailyBiasService> logger)
        {
            _httpFactory = httpFactory;
            _config      = config;
            _cache       = cache;
            _logger      = logger;
        }

        /// <inheritdoc/>
        public async Task<TradingLevels> GetTodayLevelsAsync()
        {
            // IST today
            var nowIst  = DateTimeOffset.UtcNow.ToOffset(IstOffset);
            var today   = DateOnly.FromDateTime(nowIst.DateTime);
            var cacheKey = $"nifty_bias_{today:yyyyMMdd}";

            if (_cache.TryGetValue(cacheKey, out TradingLevels? cached) && cached != null)
            {
                _logger.LogDebug("Daily bias cache hit for {Date}.", today);
                return cached;
            }

            var levels = await FetchAndComputeAsync(today);

            // Cache until next calendar day IST midnight
            var nextMidnightIst = nowIst.Date.AddDays(1) - nowIst.Offset;
            _cache.Set(cacheKey, levels, nextMidnightIst - nowIst.UtcDateTime);

            return levels;
        }

        // ─────────────────────────────────────────────────────────────────────
        private async Task<TradingLevels> FetchAndComputeAsync(DateOnly todayIst)
        {
            var baseUrl  = _config["GrowwApi:BaseUrl"]
                           ?? "https://groww.in/v1/api/charting_service/v2/chart/delayed";

            var nowUtc   = DateTimeOffset.UtcNow;
            var fromUtc  = nowUtc.AddDays(-LookbackCalendarDays);

            long startMs = fromUtc.ToUnixTimeMilliseconds();
            long endMs   = nowUtc.ToUnixTimeMilliseconds();

            var url = $"{baseUrl.TrimEnd('/')}/exchange/NSE/segment/CASH/NIFTY" +
                      $"?endTimeInMillis={endMs}&intervalInMinutes={DailyIntervalMinutes}&startTimeInMillis={startMs}";

            _logger.LogInformation("Fetching daily bias candle: {Url}", url);

            try
            {
                var client   = _httpFactory.CreateClient(HttpClientName);
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Daily bias fetch returned {Status}.", response.StatusCode);
                    return EmptyLevels(todayIst);
                }

                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var dto  = await response.Content.ReadFromJsonAsync<GrowwChartResponseDto>(opts);

                if (dto?.Candles == null || dto.Candles.Length == 0)
                {
                    _logger.LogWarning("No daily candles returned from Groww.");
                    return EmptyLevels(todayIst);
                }

                return ComputeLevels(dto.Candles, todayIst);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching daily bias.");
                return EmptyLevels(todayIst);
            }
        }

        private TradingLevels ComputeLevels(double?[][] rawCandles, DateOnly todayIst)
        {
            // Parse to (date, O, H, L, C) grouped by IST date
            var dailyCandles = rawCandles
                .Where(r => r.Length >= 5 && r[0] != null && r[1] != null &&
                             r[2] != null && r[3] != null && r[4] != null)
                .Select(r =>
                {
                    var ist = DateTimeOffset.FromUnixTimeSeconds((long)r[0]!.Value)
                                            .ToOffset(IstOffset);
                    return new
                    {
                        IstDate = DateOnly.FromDateTime(ist.DateTime),
                        Open    = r[1]!.Value,
                        High    = r[2]!.Value,
                        Low     = r[3]!.Value,
                        Close   = r[4]!.Value
                    };
                })
                .OrderBy(c => c.IstDate)
                .ToList();

            // For interval=1440, each candle represents one trading day.
            // We need the PREVIOUS trading day relative to todayIst.
            // Pick the most-recent candle whose date is BEFORE today.
            var prevDayCandle = dailyCandles
                .Where(c => c.IstDate < todayIst)
                .OrderByDescending(c => c.IstDate)
                .FirstOrDefault();

            if (prevDayCandle == null)
            {
                _logger.LogWarning("No previous-day 1D candle found before {Today}.", todayIst);
                return EmptyLevels(todayIst);
            }

            double upperWick = prevDayCandle.High - Math.Max(prevDayCandle.Open, prevDayCandle.Close);
            double lowerWick = Math.Min(prevDayCandle.Open, prevDayCandle.Close) - prevDayCandle.Low;
            double body      = Math.Abs(prevDayCandle.Close - prevDayCandle.Open);

            var bias = ComputeBias(upperWick, lowerWick, prevDayCandle.Open, prevDayCandle.Close);

            _logger.LogInformation(
                "Previous day ({PrevDate}): O={O} H={H} L={L} C={C} | " +
                "UpperWick={UW:F2} LowerWick={LW:F2} | Bias={Bias}",
                prevDayCandle.IstDate,
                prevDayCandle.Open, prevDayCandle.High, prevDayCandle.Low, prevDayCandle.Close,
                upperWick, lowerWick, bias);

            return new TradingLevels
            {
                PDH              = prevDayCandle.High,
                PDC              = prevDayCandle.Close,
                PDL              = prevDayCandle.Low,
                PrevDayUpperWick = Math.Round(upperWick, 2),
                PrevDayLowerWick = Math.Round(lowerWick, 2),
                PrevDayBody      = Math.Round(body, 2),
                Bias             = bias,
                BiasDate         = todayIst
            };
        }

        private static DailyBias ComputeBias(double upperWick, double lowerWick, double open, double close)
        {
            const double tol = 0.001;

            if (lowerWick > upperWick + tol) return DailyBias.Bullish;
            if (upperWick > lowerWick + tol) return DailyBias.Bearish;

            // Equal wicks — use body direction
            if (close > open + tol) return DailyBias.Bullish;
            if (open  > close + tol) return DailyBias.Bearish;

            return DailyBias.Neutral;
        }

        private static TradingLevels EmptyLevels(DateOnly today) => new()
        {
            Bias     = DailyBias.Neutral,
            BiasDate = today
        };
    }
}
