namespace Test.Models.Signal
{
    /// <summary>
    /// States of the trading-signal state machine.
    /// The machine starts at WaitingForBias once per trading day.
    /// </summary>
    public enum SignalEngineState
    {
        /// <summary>Bias has not yet been determined for today.</summary>
        WaitingForBias,

        /// <summary>Bias is Neutral — no trade setups will be generated today.</summary>
        Neutral,

        /// <summary>Bias is Bullish; scanning live 1-min candles for a liquidity sweep below PDH/PDC/PDL.</summary>
        BullishWaitingForSweep,

        /// <summary>A bullish liquidity sweep has been detected; waiting for a green (Close > Open) confirmation candle.</summary>
        BullishWaitingForConfirmation,

        /// <summary>A green confirmation candle has closed; waiting for the next candle to cross the confirmation-candle high.</summary>
        BullishWaitingForBreakout,

        /// <summary>BUY signal has been emitted.</summary>
        BuySignal,

        /// <summary>Bias is Bearish; scanning live 1-min candles for a liquidity sweep above PDH/PDC/PDL.</summary>
        BearishWaitingForSweep,

        /// <summary>A bearish liquidity sweep has been detected; waiting for a red (Close &lt; Open) confirmation candle.</summary>
        BearishWaitingForConfirmation,

        /// <summary>A red confirmation candle has closed; waiting for the next candle to cross the confirmation-candle low.</summary>
        BearishWaitingForBreakdown,

        /// <summary>SELL signal has been emitted.</summary>
        SellSignal
    }
}
