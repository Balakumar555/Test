using Test.Models;
using Test.Models.Signal;

namespace Test.Services.Interfaces
{
    /// <summary>
    /// Singleton state machine that processes live 1-minute NIFTY 50 candles,
    /// detects liquidity sweeps, waits for confirmation candles, and emits
    /// BUY / SELL signals when all conditions are satisfied.
    ///
    /// Registered as a Singleton so state persists across HTTP requests.
    /// Thread-safe via internal lock.
    /// </summary>
    public interface ITradingSignalEngine
    {
        /// <summary>
        /// Feed today's live 1-min candles and the current trading levels into the engine.
        /// The engine processes only candles it has not yet seen (watermark-based).
        /// Call this once per AJAX poll cycle.
        /// </summary>
        /// <param name="candles">Today's 1-min candles ordered by time ascending.</param>
        /// <param name="levels">PDH / PDC / PDL and daily bias for today.</param>
        void ProcessCandles(IReadOnlyList<CandleViewModel> candles, TradingLevels levels);

        /// <summary>Current state-machine state.</summary>
        SignalEngineState CurrentState { get; }

        /// <summary>The active (most-recently emitted) signal; null until a signal fires.</summary>
        TradingSignal? ActiveSignal { get; }

        /// <summary>All chart markers accumulated this session (sweep, confirmation, buy/sell).</summary>
        IReadOnlyList<ChartMarker> Markers { get; }

        /// <summary>Human-readable status for the signal panel.</summary>
        string StatusMessage { get; }

        /// <summary>Next condition the engine is waiting for.</summary>
        string NextCondition { get; }

        /// <summary>Description of the last significant event.</summary>
        string LastEvent { get; }

        /// <summary>IST time of the last significant event.</summary>
        string LastEventTime { get; }

        /// <summary>Number of signals generated today.</summary>
        int SignalsToday { get; }

        /// <summary>All BUY / SELL signals generated today (in chronological order).</summary>
        IReadOnlyList<TradingSignal> TodaySignals { get; }
    }
}
