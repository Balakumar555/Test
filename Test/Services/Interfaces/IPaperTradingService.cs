using Test.Models.Signal;

namespace Test.Services.Interfaces
{
    /// <summary>
    /// Singleton service that manages sequential paper (simulated) option-selling trades.
    ///
    /// Rules:
    ///  • Maximum ONE active trade at a time.
    ///  • Each signal can only open ONE trade (deduplication by TimestampSeconds).
    ///  • Stop-loss is evaluated against the NIFTY 50 index price, not the option premium.
    ///  • A stopped-out trade must be fully closed before the next signal can be entered.
    /// </summary>
    public interface IPaperTradingService
    {
        /// <summary>
        /// Opens a new paper trade for the given signal and option symbol.
        /// No-op if the signal is already processed or a trade is currently active.
        /// <paramref name="slPremium"/> = option's HIGH at sweep-candle time (red SL line on chart).
        /// Pass 0 if unavailable; NIFTY index SL will still be checked.
        /// </summary>
        void RecordEntry(TradingSignal signal, string optionSymbol, double entryPremium, double slPremium = 0);

        /// <summary>
        /// Updates the mark-to-market LTP for the currently active trade.
        /// No-op if no active trade or symbol doesn't match.
        /// </summary>
        void UpdateCurrentPrice(string optionSymbol, double currentLtp);

        /// <summary>
        /// Evaluates whether the current NIFTY 50 index price has crossed the stop-loss level.
        /// If yes, closes the active trade as <see cref="TradeStatus.StoppedOut"/>.
        /// Returns true if a stop-loss exit was applied.
        /// </summary>
        bool CheckAndApplyStopLoss(double niftyCurrentPrice);

        /// <summary>Returns true if this signal's timestamp has already been entered.</summary>
        bool IsSignalAlreadyProcessed(long signalTimestampSeconds);

        /// <summary>Returns the currently ACTIVE trade, or null if no open position.</summary>
        PaperTrade? GetActiveTrade();

        /// <summary>Returns the most recent trade regardless of status (for chart display after SL).</summary>
        PaperTrade? GetLastTrade();

        /// <summary>Returns the option symbol for the active trade, or null.</summary>
        string? GetActiveOptionSymbol();

        /// <summary>Returns all trades grouped by trading date (most-recent first).</summary>
        List<DayWisePnL> GetDayWisePnL();
    }
}
