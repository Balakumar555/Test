using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Test.Models;
using Test.Services.Interfaces;

namespace Test.Services
{
    /// <summary>
    /// Orchestrates fetching, IST conversion, trading-day identification,
    /// and Previous Day High / Low / Close calculation for NIFTY 50.
    ///
    /// NSE session boundaries (IST):
    ///   Regular session  : 09:15 – 15:30
    ///   Closing Auction Session (CAS) starts after 15:10
    ///
    /// Per exchange rules the LAST regular-session candle (timestamped 15:10)
    /// provides the official Previous Day Close.  All candles after 15:10 are
    /// CAS candles and are excluded from PDH / PDL / PDC calculations.
    /// </summary>
    public class NiftyMarketService : INiftyMarketService
    {
        private readonly IMarketDataProvider _provider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<NiftyMarketService> _logger;

        // IST = UTC+5:30
        private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

        /// <summary>
        /// Last regular-session candle timestamp (IST).
        /// CAS begins after this; candles beyond it are excluded from PDH/PDL/PDC.
        /// Configurable via GrowwApi:RegularSessionEndTime (default "15:10").
        /// </summary>
        private static readonly TimeOnly DefaultRegularSessionEnd = new(15, 10);

        /// <summary>
        /// Market open time (IST) – used to filter out pre-market candles if any.
        /// </summary>
        private static readonly TimeOnly MarketOpen = new(9, 15);

        public NiftyMarketService(
            IMarketDataProvider provider,
            IConfiguration configuration,
            ILogger<NiftyMarketService> logger)
        {
            _provider = provider;
            _configuration = configuration;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<ChartDataViewModel> GetChartDataAsync()
        {
            try
            {
                int lookbackDays = _configuration.GetValue<int>("GrowwApi:LookbackDays", 7);

                // Parse configurable session-end time (default 15:10)
                var sessionEndStr = _configuration["GrowwApi:RegularSessionEndTime"] ?? "15:10";
                var regularSessionEnd = TimeOnly.TryParse(sessionEndStr, out var parsed)
                    ? parsed
                    : DefaultRegularSessionEnd;

                // Fetch a generous lookback — captures weekends + holidays naturally
                var nowUtc   = DateTimeOffset.UtcNow;
                var startUtc = nowUtc.AddDays(-lookbackDays);

                long startMs = startUtc.ToUnixTimeMilliseconds();
                long endMs   = nowUtc.ToUnixTimeMilliseconds();

                var dto = await _provider.GetRawDataAsync(startMs, endMs);

                if (dto?.Candles == null || dto.Candles.Length == 0)
                {
                    return Error("Market data is currently unavailable. " +
                                 "The Groww API may be unreachable or markets may be closed.");
                }

                // ── Convert raw candles → typed records with IST DateTimeOffset ──────
                var allCandles = ParseCandles(dto.Candles);

                if (allCandles.Count == 0)
                    return Error("No valid candles could be parsed from the API response.");

                // ── Group by IST calendar date ────────────────────────────────────────
                var byDate = allCandles
                    .GroupBy(c => DateOnly.FromDateTime(c.IstTime.DateTime))
                    .OrderBy(g => g.Key)
                    .ToList();

                if (byDate.Count < 2)
                {
                    _logger.LogWarning(
                        "Only {DayCount} trading day(s) found. Previous-day levels unavailable.",
                        byDate.Count);
                }

                // Most-recent = current trading day; second-most-recent = previous
                var currentDayGroup  = byDate.Last();
                var previousDayGroup = byDate.Count >= 2 ? byDate[^2] : null;

                // Current day: all candles ordered by time (include CAS for live display)
                var currentCandles = currentDayGroup
                    .OrderBy(c => c.IstTime)
                    .ToList();

                // ── Previous Day levels (regular session only, up to 15:10) ──────────
                double pdHigh = 0, pdLow = 0, pdClose = 0;
                string prevDate = string.Empty;

                if (previousDayGroup != null)
                {
                    // Filter to regular session candles only: 09:15 ≤ candle ≤ 15:10
                    // The 15:10 candle IS included — it is the last regular candle
                    // and its Close is the official Previous Day Close.
                    var regularPrevCandles = previousDayGroup
                        .Where(c =>
                        {
                            var ist = TimeOnly.FromTimeSpan(c.IstTime.TimeOfDay);
                            return ist >= MarketOpen && ist <= regularSessionEnd;
                        })
                        .OrderBy(c => c.IstTime)
                        .ToList();

                    if (regularPrevCandles.Count > 0)
                    {
                        pdHigh  = regularPrevCandles.Max(c => c.High);
                        pdLow   = regularPrevCandles.Min(c => c.Low);
                        pdClose = regularPrevCandles.Last().Close;  // close of 15:10 candle

                        _logger.LogInformation(
                            "Previous day ({Date}): {Count} regular candles. " +
                            "PDH={PDH}, PDL={PDL}, PDC={PDC} (last candle @ {LastCandle} IST)",
                            previousDayGroup.Key,
                            regularPrevCandles.Count,
                            pdHigh, pdLow, pdClose,
                            regularPrevCandles.Last().IstTime.ToString("HH:mm"));
                    }
                    else
                    {
                        _logger.LogWarning(
                            "No regular-session candles found for previous day {Date}.",
                            previousDayGroup.Key);
                    }

                    prevDate = previousDayGroup.Key.ToString("dd MMM yyyy");
                }

                // ── Current Day aggregates (regular session only for High/Low) ────────
                var regularCurrentCandles = currentCandles
                    .Where(c =>
                    {
                        var ist = TimeOnly.FromTimeSpan(c.IstTime.TimeOfDay);
                        return ist >= MarketOpen && ist <= regularSessionEnd;
                    })
                    .ToList();

                double cdHigh    = regularCurrentCandles.Count > 0
                    ? regularCurrentCandles.Max(c => c.High)  : 0;
                double cdLow     = regularCurrentCandles.Count > 0
                    ? regularCurrentCandles.Min(c => c.Low)   : 0;

                // Current price = close of the most recent candle (any session)
                double currentPx = currentCandles.Count > 0
                    ? currentCandles.Last().Close : 0;

                // ── Build view model ─────────────────────────────────────────────────
                // Send ALL current-day candles to the chart (so CAS is visible as extra candles)
                var candleVMs = currentCandles.Select(c => new CandleViewModel
                {
                    Time  = c.IstTime.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                    Open  = c.Open,
                    High  = c.High,
                    Low   = c.Low,
                    Close = c.Close
                }).ToList();

                return new ChartDataViewModel
                {
                    Success             = true,
                    CurrentPrice        = currentPx,
                    PreviousDayHigh     = pdHigh,
                    PreviousDayLow      = pdLow,
                    PreviousDayClose    = pdClose,
                    CurrentDayHigh      = cdHigh,
                    CurrentDayLow       = cdLow,
                    CurrentTradingDate  = currentDayGroup.Key.ToString("dd MMM yyyy"),
                    PreviousTradingDate = prevDate,
                    Candles             = candleVMs
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in NiftyMarketService.GetChartDataAsync");
                return Error("An unexpected error occurred while processing market data.");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses raw double?[][] candles.
        /// Groww format: [unixSeconds, open, high, low, close, volume?]
        /// Index 5 (volume) is often null — ignored.
        /// </summary>
        private List<RawCandle> ParseCandles(double?[][] rawCandles)
        {
            var result = new List<RawCandle>(rawCandles.Length);

            foreach (var row in rawCandles)
            {
                if (row.Length < 5) continue;
                if (row[0] == null || row[1] == null || row[2] == null ||
                    row[3] == null || row[4] == null) continue;

                long unixSeconds = (long)row[0]!.Value;

                var utcTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                var istTime = utcTime.ToOffset(IstOffset);

                result.Add(new RawCandle
                {
                    IstTime = istTime,
                    Open    = row[1]!.Value,
                    High    = row[2]!.Value,
                    Low     = row[3]!.Value,
                    Close   = row[4]!.Value
                });
            }

            return result;
        }

        private static ChartDataViewModel Error(string message) => new()
        {
            Success      = false,
            ErrorMessage = message
        };

        // ─────────────────────────────────────────────────────────────────────────
        private record RawCandle
        {
            public DateTimeOffset IstTime { get; init; }
            public double Open  { get; init; }
            public double High  { get; init; }
            public double Low   { get; init; }
            public double Close { get; init; }
        }
    }
}
