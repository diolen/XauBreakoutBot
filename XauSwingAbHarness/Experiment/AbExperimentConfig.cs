using cAlgo.Robots.SupportResistance;

namespace XauSwingAbHarness.Experiment;

/// <summary>
/// Configuración fija a priori. No se ajusta con el 20 % OOS.
/// </summary>
public sealed class AbExperimentConfig
{
    public const string ExpectedSha256 =
        "023c31d2e0b45a85ad1411350d28539e32c36a60cc96f05d0c8945cbe2bce95d";

    public int LookbackBars { get; init; } = SupportResistanceEngine.DefaultLookbackBars;
    public int AtrPeriod { get; init; } = SupportResistanceEngine.DefaultAtrPeriod;

    /// <summary>Horizontes predictivos en velas M15 cerradas posteriores al evento.</summary>
    public int[] PredictiveHorizonsBars { get; init; } = { 16, 32 };

    /// <summary>Umbral ATR para éxito favorable / fracaso adverso.</summary>
    public double ReactionThresholdAtr { get; init; } = 0.5;

    public double DevelopFraction { get; init; } = 0.60;
    public double ValidateFraction { get; init; } = 0.20;
    // Remaining → OOS (0.20)

    public string DefaultCsvPath { get; init; } =
        "/home/emsetec/cAlgo/Data/cBots/XauM15HistoryExporter/xauusd_m15_2025-11_2026-11.csv";
}
