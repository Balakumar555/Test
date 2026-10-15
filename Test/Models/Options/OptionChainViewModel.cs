using System.Text.Json.Serialization;

namespace Test.Models.Options
{
    /// <summary>One row of the option chain: CE side | Strike | PE side.</summary>
    public class OptionChainRow
    {
        [JsonPropertyName("strike")]
        public double Strike { get; init; }

        [JsonPropertyName("isAtm")]
        public bool IsAtm { get; init; }

        // ── CALL side ──────────────────────────────────────────────────────
        [JsonPropertyName("ceLtp")]        public double CeLtp        { get; init; }
        [JsonPropertyName("ceChange")]     public double CeChange     { get; init; }
        [JsonPropertyName("ceChangePct")]  public double CeChangePct  { get; init; }
        [JsonPropertyName("ceOi")]         public double CeOi         { get; init; }
        [JsonPropertyName("ceOiChange")]   public double CeOiChange   { get; init; }
        [JsonPropertyName("ceOiChangePct")]public double CeOiChangePct{ get; init; }
        [JsonPropertyName("ceVolume")]     public long   CeVolume     { get; init; }
        [JsonPropertyName("ceBuyQty")]     public long   CeBuyQty     { get; init; }
        [JsonPropertyName("ceSellQty")]    public long   CeSellQty    { get; init; }
        [JsonPropertyName("ceSymbol")]     public string CeSymbol     { get; init; } = string.Empty;

        // ── PUT side ───────────────────────────────────────────────────────
        [JsonPropertyName("peLtp")]        public double PeLtp        { get; init; }
        [JsonPropertyName("peChange")]     public double PeChange     { get; init; }
        [JsonPropertyName("peChangePct")]  public double PeChangePct  { get; init; }
        [JsonPropertyName("peOi")]         public double PeOi         { get; init; }
        [JsonPropertyName("peOiChange")]   public double PeOiChange   { get; init; }
        [JsonPropertyName("peOiChangePct")]public double PeOiChangePct{ get; init; }
        [JsonPropertyName("peVolume")]     public long   PeVolume     { get; init; }
        [JsonPropertyName("peBuyQty")]     public long   PeBuyQty     { get; init; }
        [JsonPropertyName("peSellQty")]    public long   PeSellQty    { get; init; }
        [JsonPropertyName("peSymbol")]     public string PeSymbol     { get; init; } = string.Empty;
    }

    /// <summary>Full option chain payload returned to the browser.</summary>
    public class OptionChainViewModel
    {
        [JsonPropertyName("success")]      public bool   Success      { get; set; } = true;
        [JsonPropertyName("errorMessage")] public string? ErrorMessage { get; set; }
        [JsonPropertyName("expiry")]       public string Expiry       { get; set; } = string.Empty;
        [JsonPropertyName("atmStrike")]    public double AtmStrike    { get; set; }
        [JsonPropertyName("rows")]         public List<OptionChainRow> Rows { get; set; } = new();
        [JsonPropertyName("fetchedAt")]    public string FetchedAt    { get; set; } = string.Empty;
    }
}
