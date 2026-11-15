using cAlgo.Robots.SupportResistance;

namespace XauSwingAbHarness.Breakout;

/// <summary>
/// Parámetros fijados a priori (no optimizados con OOS).
/// Buffer 0.15×ATR y ventana 8 son hipótesis experimentales, no reglas productivas.
/// </summary>
public sealed class BreakoutExperimentConfig
{
    public const string ExpectedSha256 =
        "023c31d2e0b45a85ad1411350d28539e32c36a60cc96f05d0c8945cbe2bce95d";

    public int LookbackBars { get; init; } = SupportResistanceEngine.DefaultLookbackBars;
    public int AtrPeriod { get; init; } = SupportResistanceEngine.DefaultAtrPeriod;
    public int ReactionHorizonBars { get; init; } = 16;
    public double ReactionThresholdAtr { get; init; } = 0.5;

    /// <summary>Hipótesis experimental (≠ BreakoutBufferAtr productivo 0.10).</summary>
    public double ConfirmationBufferAtr { get; init; } = 0.15;

    public int MaxBarsToConfirm { get; init; } = 8;
    public int DecimationMinGapBars { get; init; } = 16;
    public double DevelopFraction { get; init; } = 0.60;
    public double ValidateFraction { get; init; } = 0.20;

    public string DefaultCsvPath { get; init; } =
        "/home/emsetec/cAlgo/Data/cBots/XauM15HistoryExporter/xauusd_m15_2025-11_2026-11.csv";
}
