using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Test.Models;
using Test.Models.Signal;
using Test.Services.Interfaces;

namespace Test.Controllers
{
    /// <summary>
    /// Serves the NIFTY 50 live chart + signal dashboard.
    /// </summary>
    public class NiftyController : Controller
    {
        private readonly INiftyMarketService    _marketService;
        private readonly INiftyDailyBiasService _biasService;
        private readonly ITradingSignalEngine   _signalEngine;
        private readonly ISignalHistoryService  _signalHistory;
        private readonly TradingStrategyOptions _opts;
        private readonly ILogger<NiftyController> _logger;

        private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

        public NiftyController(
            INiftyMarketService    marketService,
            INiftyDailyBiasService biasService,
            ITradingSignalEngine   signalEngine,
            ISignalHistoryService  signalHistory,
            IOptions<TradingStrategyOptions> opts,
            ILogger<NiftyController> logger)
        {
            _marketService = marketService;
            _biasService   = biasService;
            _signalEngine  = signalEngine;
            _signalHistory = signalHistory;
            _opts          = opts.Value;
            _logger        = logger;
        }

        /// <summary>GET /Nifty — Razor page shell.</summary>
        [HttpGet]
        public IActionResult Index() => View();

        /// <summary>
        /// GET /Nifty/GetSignalData
        /// 30-second AJAX endpoint. Returns candlestick data + signal state in one JSON payload.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetSignalData()
        {
            try
            {
                // 1. Fetch today's 1-min candles + PD levels via the existing service
                var chartData = await _marketService.GetChartDataAsync();

                if (!chartData.Success)
                {
                    return Json(new SignalDataViewModel
                    {
                        Success      = false,
                        ErrorMessage = chartData.ErrorMessage
                    });
                }

                // 2. Fetch / cache today's daily bias + PDH/PDC/PDL from 1-day candle
                var levels = await _biasService.GetTodayLevelsAsync();

                // 3. Compute today's Opening Range (first N minutes, default 09:15–09:30)
                var levelsWithOR = ComputeAndMergeOpeningRange(levels, chartData.Candles);

                // 4. Feed new closed candles into the signal engine
                _signalEngine.ProcessCandles(chartData.Candles, levelsWithOR);

                // 5. Assemble the combined view model
                var vm = new SignalDataViewModel
                {
                    // ── Chart fields ────────────────────────────────────────
                    Success              = true,
                    CurrentPrice         = chartData.CurrentPrice,
                    PreviousDayHigh      = levelsWithOR.IsValid ? levelsWithOR.PDH : chartData.PreviousDayHigh,
                    PreviousDayLow       = levelsWithOR.IsValid ? levelsWithOR.PDL : chartData.PreviousDayLow,
                    PreviousDayClose     = levelsWithOR.IsValid ? levelsWithOR.PDC : chartData.PreviousDayClose,
                    CurrentDayHigh       = chartData.CurrentDayHigh,
                    CurrentDayLow        = chartData.CurrentDayLow,
                    CurrentTradingDate   = chartData.CurrentTradingDate,
                    PreviousTradingDate  = chartData.PreviousTradingDate,
                    Candles              = chartData.Candles,

                    // ── Signal fields ───────────────────────────────────────
                    DailyBias        = levelsWithOR.Bias.ToString(),
                    PrevDayUpperWick = levelsWithOR.PrevDayUpperWick,
                    PrevDayLowerWick = levelsWithOR.PrevDayLowerWick,
                    OpeningRangeHigh = levelsWithOR.ORH,
                    OpeningRangeLow  = levelsWithOR.ORL,
                    OpeningRangeReady = levelsWithOR.OpeningRangeReady,
                    SignalState      = _signalEngine.CurrentState.ToString(),
                    StatusMessage    = _signalEngine.StatusMessage,
                    NextCondition    = _signalEngine.NextCondition,
                    LastEvent        = _signalEngine.LastEvent,
                    LastEventTime    = _signalEngine.LastEventTime,
                    SignalsToday     = _signalEngine.SignalsToday,
                    MaxSignalsPerDay = _opts.MaxSignalsPerDay,
                    ActiveSignal     = _signalEngine.ActiveSignal,
                    AllSignalsToday  = _signalEngine.TodaySignals.ToList(),
                    Markers          = _signalEngine.Markers.ToList()
                };

                return Json(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NiftyController.GetSignalData");
                return Json(new SignalDataViewModel
                {
                    Success      = false,
                    ErrorMessage = "Server error while computing signal data."
                });
            }
        }

        /// <summary>
        /// GET /Nifty/GetSignalHistory
        /// Returns all archived daily signal records (most-recent first).
        /// Call this once on page load to populate the history panel.
        /// </summary>
        [HttpGet]
        public IActionResult GetSignalHistory()
        {
            try
            {
                var history = _signalHistory.GetHistory();
                return Json(new { success = true, days = history });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NiftyController.GetSignalHistory");
                return Json(new { success = false, errorMessage = "Server error fetching signal history." });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Computes ORH/ORL from today's candles and returns a new TradingLevels
        /// that merges the bias-service result with the Opening Range values.
        ///
        /// Opening Range = first <see cref="TradingStrategyOptions.OpeningRangeMinutes"/> minutes
        /// starting at MarketStartTime.  ORH/ORL are only locked once that window has passed.
        /// </summary>
        private TradingLevels ComputeAndMergeOpeningRange(
            TradingLevels      src,
            IList<CandleViewModel> candles)
        {
            TimeOnly.TryParse(_opts.MarketStartTime, out var mktOpen);
            var orEnd    = mktOpen.AddMinutes(_opts.OpeningRangeMinutes);
            var nowIst   = DateTimeOffset.UtcNow.ToOffset(IstOffset);
            var nowTime  = TimeOnly.FromTimeSpan(nowIst.TimeOfDay);
            var todayIst = DateOnly.FromDateTime(nowIst.DateTime);

            // Collect candles that fall within the OR window and belong to today
            var orCandles = candles
                .Where(c =>
                {
                    if (!DateTimeOffset.TryParse(c.Time, out var ist)) return false;
                    if (DateOnly.FromDateTime(ist.DateTime) != todayIst) return false;
                    var t = TimeOnly.FromTimeSpan(ist.TimeOfDay);
                    return t >= mktOpen && t < orEnd;
                })
                .ToList();

            // OR is only ready after the window has closed AND we have at least one candle
            bool orReady = nowTime >= orEnd && orCandles.Count > 0;
            double orh   = orReady ? orCandles.Max(c => c.High) : 0;
            double orl   = orReady ? orCandles.Min(c => c.Low)  : 0;

            if (orReady)
                _logger.LogDebug("Opening Range locked: ORH={ORH} ORL={ORL} ({Count} candles)",
                    orh, orl, orCandles.Count);

            // Return a new TradingLevels with OR values merged in
            return new TradingLevels
            {
                PDH              = src.PDH,
                PDC              = src.PDC,
                PDL              = src.PDL,
                PrevDayUpperWick = src.PrevDayUpperWick,
                PrevDayLowerWick = src.PrevDayLowerWick,
                PrevDayBody      = src.PrevDayBody,
                Bias             = src.Bias,
                BiasDate         = src.BiasDate,
                ORH              = orh,
                ORL              = orl,
                OpeningRangeReady = orReady
            };
        }
    }
}
