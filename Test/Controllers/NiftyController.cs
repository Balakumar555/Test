using Microsoft.AspNetCore.Mvc;
using Test.Models.Signal;
using Test.Services.Interfaces;

namespace Test.Controllers
{
    /// <summary>
    /// Serves the NIFTY 50 live chart + signal dashboard.
    /// </summary>
    public class NiftyController : Controller
    {
        private readonly INiftyMarketService   _marketService;
        private readonly INiftyDailyBiasService _biasService;
        private readonly ITradingSignalEngine   _signalEngine;
        private readonly ILogger<NiftyController> _logger;

        public NiftyController(
            INiftyMarketService    marketService,
            INiftyDailyBiasService biasService,
            ITradingSignalEngine   signalEngine,
            ILogger<NiftyController> logger)
        {
            _marketService = marketService;
            _biasService   = biasService;
            _signalEngine  = signalEngine;
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

                // 3. Feed new closed candles into the signal engine
                _signalEngine.ProcessCandles(chartData.Candles, levels);

                // 4. Assemble the combined view model
                var vm = new SignalDataViewModel
                {
                    // ── Chart fields ────────────────────────────────────────
                    Success              = true,
                    CurrentPrice         = chartData.CurrentPrice,
                    PreviousDayHigh      = levels.IsValid ? levels.PDH : chartData.PreviousDayHigh,
                    PreviousDayLow       = levels.IsValid ? levels.PDL : chartData.PreviousDayLow,
                    PreviousDayClose     = levels.IsValid ? levels.PDC : chartData.PreviousDayClose,
                    CurrentDayHigh       = chartData.CurrentDayHigh,
                    CurrentDayLow        = chartData.CurrentDayLow,
                    CurrentTradingDate   = chartData.CurrentTradingDate,
                    PreviousTradingDate  = chartData.PreviousTradingDate,
                    Candles              = chartData.Candles,

                    // ── Signal fields ───────────────────────────────────────
                    DailyBias        = levels.Bias.ToString(),
                    PrevDayUpperWick = levels.PrevDayUpperWick,
                    PrevDayLowerWick = levels.PrevDayLowerWick,
                    SignalState      = _signalEngine.CurrentState.ToString(),
                    StatusMessage    = _signalEngine.StatusMessage,
                    NextCondition    = _signalEngine.NextCondition,
                    LastEvent        = _signalEngine.LastEvent,
                    LastEventTime    = _signalEngine.LastEventTime,
                    SignalsToday     = _signalEngine.SignalsToday,
                    MaxSignalsPerDay = 3,   // reflected from config for UI
                    ActiveSignal     = _signalEngine.ActiveSignal,
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
    }
}
