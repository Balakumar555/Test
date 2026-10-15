namespace Test.Models.Options
{
    /// <summary>
    /// Option chain configuration bound from appsettings.json → "OptionChain".
    /// Expiry format matches Groww symbol encoding: e.g. "26O19" = 2026-Oct-19.
    /// </summary>
    public class OptionChainOptions
    {
        public const string SectionName = "OptionChain";

        /// <summary>Expiry code as used in Groww symbols, e.g. "26O19".</summary>
        public string Expiry { get; set; } = "26O19";

        /// <summary>Strike price interval in points. NIFTY uses 50.</summary>
        public int StrikeInterval { get; set; } = 50;

        /// <summary>Number of strikes above AND below ATM to show (each side).</summary>
        public int NumStrikes { get; set; } = 6;
    }
}
