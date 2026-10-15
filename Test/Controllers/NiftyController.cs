using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Test.Models;
using Test.Models.Options;
using Test.Models.Signal;
using Test.Services.Interfaces;
using Test.Models.Dto;

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
        private readonly INiftyOptionService    _optionService;
        private readonly IPaperTradingService   _paperTrading;
        private readonly TradingStrategyOptions _opts;
        private readonly OptionChainOptions     _optionOpts;
        private readonly ILogger<NiftyController> _logger;

        private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

        public NiftyController(
            INiftyMarketService    marketService,
            INiftyDailyBiasService biasService,
            ITradingSignalEngine   signalEngine,
            ISignalHistoryService  signalHistory,
            INiftyOptionService    optionService,
            IPaperTradingService   paperTrading,
            IOptions<TradingStrategyOptions> opts,
            IOptions<OptionChainOptions>     optionOpts,
            ILogger<NiftyController> logger)
        {
            _marketService = marketService;
            _biasService   = biasService;
            _signalEngine  = signalEngine;
            _signalHistory = signalHistory;
            _optionService = optionService;
            _paperTrading  = paperTrading;
            _opts          = opts.Value;
            _optionOpts    = optionOpts.Value;
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

                // 6. Update mark-to-market FIRST (so SL check uses fresh premium).
                var activeTrade = _paperTrading.GetActiveTrade();
                string? optSym  = activeTrade?.OptionSymbol;

                if (activeTrade != null && optSym != null)
                {
                    try
                    {
                        var ltp = await _optionService.GetSingleOptionPriceAsync(optSym);
                        if (ltp > 0) _paperTrading.UpdateCurrentPrice(optSym, ltp);  // also trails SL
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "LTP refresh failed for {Symbol}", optSym); }
                }

                // 7. Stop-loss check against NIFTY price AND updated option premium.
                _paperTrading.CheckAndApplyStopLoss(vm.CurrentPrice);

                // 8. After potential SL exit, re-fetch active trade (may now be null).
                //    Enter the next unprocessed signal if no trade is active.
                activeTrade = _paperTrading.GetActiveTrade();

                if (activeTrade == null)
                {
                    var nextSignal = _signalEngine.TodaySignals
                        .FirstOrDefault(s => !_paperTrading.IsSignalAlreadyProcessed(s.TimestampSeconds));

                    if (nextSignal != null)
                    {
                        optSym = GetOptionSymbol(nextSignal, vm.CurrentPrice);
                        try
                        {
                            // Fetch today's OHLC candles once — use for both entry price and SL premium.
                            var candles = await GetTodayOptionCandles(optSym);
                            if (candles != null && candles.Count > 0)
                            {
                                // Entry price = Close of option candle at signal timestamp
                                var entryCandle = FindCandleAtTime(candles, nextSignal.TimestampSeconds);
                                double entryPrice = entryCandle?.Close ?? candles[^1].Close;

                                // SL premium = HIGH of option candle at sweep-candle timestamp
                                // (= option's highest price when NIFTY was at the "first red candle high")
                                var sweepCandle = FindCandleAtTime(candles, nextSignal.SweepTimestampSeconds);
                                double slPremium = sweepCandle?.High ?? 0;

                                if (entryPrice > 0)
                                {
                                    _paperTrading.RecordEntry(nextSignal, optSym, entryPrice, slPremium);
                                    _logger.LogInformation(
                                        "Trade opened: {Sym} entry={E:F2} slPremium={SL:F2} (sweep@{ST}) niftySL={NSL:F2}",
                                        optSym, entryPrice, slPremium, nextSignal.SweepTime, nextSignal.SweepCandleHigh);
                                }
                                else _logger.LogWarning("No entry price for {Symbol}; trade not opened.", optSym);
                            }
                            else _logger.LogWarning("No option candles for {Symbol}; trade not opened.", optSym);
                        }
                        catch (Exception ex) { _logger.LogWarning(ex, "Could not open trade for {Symbol}", optSym); }
                    }
                }

                // 9. Expose last trade (active or stopped-out) for the right-side option chart
                var lastTrade = _paperTrading.GetLastTrade();
                optSym ??= lastTrade?.OptionSymbol;

                vm.OptionSymbol     = optSym;
                vm.ActivePaperTrade = lastTrade;

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
        /// GET /Nifty/GetOptionChart?symbol=NIFTY26O1922750PE
        /// Returns 1-minute OHLCV candles for the given option symbol from today's session.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetOptionChart([FromQuery] string symbol = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(symbol))
                    return Json(new { success = false, error = "Symbol is required." });

                var nowIst   = DateTimeOffset.UtcNow.ToOffset(IstOffset);
                var todayIst = DateOnly.FromDateTime(nowIst.DateTime);

                // Start: 09:15 AM IST today
                var startIst = new DateTimeOffset(
                    todayIst.Year, todayIst.Month, todayIst.Day, 9, 15, 0, IstOffset);

                long startMs = startIst.ToUnixTimeMilliseconds();
                long endMs   = nowIst.ToUnixTimeMilliseconds();

                var candles = await _optionService.GetOptionChartAsync(symbol, startMs, endMs);

                return Json(new { success = true, symbol, candles });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NiftyController.GetOptionChart");
                return Json(new { success = false, error = "Server error fetching option chart." });
            }
        }

        /// <summary>
        /// GET /Nifty/GetPaperTrades
        /// Returns all paper trades grouped by date, most-recent first.
        /// </summary>
        [HttpGet]
        public IActionResult GetPaperTrades()
        {
            try
            {
                var days      = _paperTrading.GetDayWisePnL();
                var lastTrade = _paperTrading.GetLastTrade();   // active OR most-recently closed
                return Json(new { success = true, days, lastTrade });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NiftyController.GetPaperTrades");
                return Json(new { success = false, error = "Server error fetching paper trades." });
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

        /// <summary>
        /// GET /Nifty/GetOptionChain?atm=22850
        /// Returns live NIFTY option chain (CE + PE) for strikes around ATM.
        /// Proxies through server to avoid CORS. Expiry + strike config from appsettings.json.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetOptionChain([FromQuery] double atm = 0)
        {
            try
            {
                // If no ATM given, use current price from market service
                if (atm <= 0)
                {
                    var chart = await _marketService.GetChartDataAsync();
                    atm = chart.Success && chart.CurrentPrice > 0 ? chart.CurrentPrice : 22500;
                }
                var result = await _optionService.GetOptionChainAsync(atm);
                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NiftyController.GetOptionChain");
                return Json(new OptionChainViewModel
                {
                    Success      = false,
                    ErrorMessage = "Server error fetching option chain."
                });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fetches today's 1-minute option OHLC candles (09:15 → now) for <paramref name="optSym"/>.
        /// Returns null if the API call fails or returns no data.
        /// Called once per trade entry to get both the entry price and the SL premium.
        /// </summary>
        private async Task<List<OptionCandleDto>?> GetTodayOptionCandles(string optSym)
        {
            try
            {
                var nowIst   = DateTimeOffset.UtcNow.ToOffset(IstOffset);
                var todayIst = DateOnly.FromDateTime(nowIst.DateTime);
                var startIst = new DateTimeOffset(
                    todayIst.Year, todayIst.Month, todayIst.Day, 9, 15, 0, IstOffset);

                var candles = await _optionService.GetOptionChartAsync(
                    optSym,
                    startIst.ToUnixTimeMilliseconds(),
                    nowIst.ToUnixTimeMilliseconds());

                return candles.Count > 0 ? candles : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch option candles for {Symbol}", optSym);
                return null;
            }
        }

        /// <summary>
        /// Returns the first candle whose fake-UTC timestamp is at or after
        /// <paramref name="targetFakeUtcSeconds"/>. Falls back to the last candle.
        /// </summary>
        private static OptionCandleDto? FindCandleAtTime(
            List<OptionCandleDto> candles, long targetFakeUtcSeconds)
        {
            if (candles.Count == 0) return null;
            if (targetFakeUtcSeconds <= 0)  return candles[^1];

            return candles
                .Select(c => new { Candle = c, FakeUtc = ToDisplaySecondsFromIso(c.Time) })
                .Where(x => x.FakeUtc >= targetFakeUtcSeconds)
                .OrderBy(x => x.FakeUtc)
                .FirstOrDefault()?.Candle
                ?? candles[^1];
        }

        /// <summary>
        /// Converts an ISO 8601 IST string to the fake-UTC Unix seconds used by the chart JS.
        /// Strips the offset and treats IST local time as UTC — same as ToDisplaySeconds() in the engine.
        /// </summary>
        private static long ToDisplaySecondsFromIso(string isoStr)
        {
            try
            {
                var local = isoStr.Length >= 19 ? isoStr[..19] : isoStr;
                var dt    = DateTime.Parse(local, null,
                    System.Globalization.DateTimeStyles.None);
                return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
            }
            catch { return 0; }
        }

        /// <summary>
        /// Derives the paper-trade option symbol from an active signal.
        ///
        /// Bullish (BUY signal) → short PUT 2 strikes OTM → ATM - 2×interval  PE
        /// Bearish (SELL signal)→ short CALL 2 strikes OTM → ATM + 2×interval  CE
        ///
        /// Example: ATM=22900, interval=50, BUY → 22800 PE → "NIFTY26O1922800PE"
        /// </summary>
        private string GetOptionSymbol(TradingSignal signal, double currentPrice)
        {
            int    interval = _optionOpts.StrikeInterval > 0 ? _optionOpts.StrikeInterval : 50;
            string expiry   = _optionOpts.Expiry;
            double atm      = Math.Round(currentPrice / interval) * interval;

            if (signal.Type == SignalType.Buy)
            {
                int strike = (int)(atm - 2 * interval);
                return $"NIFTY{expiry}{strike}PE";
            }
            else
            {
                int strike = (int)(atm + 2 * interval);
                return $"NIFTY{expiry}{strike}CE";
            }
        }

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
