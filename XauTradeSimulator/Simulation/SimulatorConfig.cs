using cAlgo.Robots.Risk;
using cAlgo.Robots.SupportResistance;

namespace XauTradeSimulator.Simulation;

/// <summary>
/// Configuración del simulador histórico. Parámetros fijados a priori (no optimizados).
/// </summary>
public sealed record SimulatorConfig
{
    public const string ExpectedSha256 =
        "023c31d2e0b45a85ad1411350d28539e32c36a60cc96f05d0c8945cbe2bce95d";

    public const int ExpectedSignalCount = 639;

    public const string DefaultCsvPath =
        "/home/emsetec/cAlgo/Data/cBots/XauM15HistoryExporter/xauusd_m15_2025-11_2026-11.csv";

    public int SwingStrength { get; init; } = SupportResistanceEngine.DefaultSwingStrength;
    public int LookbackBars { get; init; } = SupportResistanceEngine.DefaultLookbackBars;
    public int AtrPeriod { get; init; } = SupportResistanceEngine.DefaultAtrPeriod;

    public double InitialEquity { get; init; } = 10_000;
    public RiskParameters Risk { get; init; } = new();

    /// <summary>Spread total Ask−Bid en precio (USD). 0 = frictionless.</summary>
    public double SpreadPrice { get; init; }

    /// <summary>
    /// Comisión round-trip en moneda de cuenta. MVP: 0 y marcado como no modelado.
    /// Configurable para ejecuciones futuras sin optimización.
    /// </summary>
    public double CommissionRoundTrip { get; init; }

    public bool CommissionModeled { get; init; }
    public bool SlippageModeled { get; init; }

    public double PipSize { get; init; } = 0.01;
    public double PipValue { get; init; } = 0.01;
    public double VolumeInUnitsMin { get; init; } = 1;
    public double VolumeInUnitsMax { get; init; } = 10_000;
    public double VolumeInUnitsStep { get; init; } = 1;
    public double LotSizeInUnits { get; init; } = 100;

    public string ScenarioName { get; init; } = "custom";

    public static SimulatorConfig Frictionless(string? scenarioName = null) =>
        new()
        {
            ScenarioName = scenarioName ?? "frictionless",
            SpreadPrice = 0,
            CommissionRoundTrip = 0,
            CommissionModeled = false,
            SlippageModeled = false
        };

    public static SimulatorConfig Spread040(string? scenarioName = null) =>
        new()
        {
            ScenarioName = scenarioName ?? "spread040",
            SpreadPrice = 0.40,
            CommissionRoundTrip = 0,
            CommissionModeled = false,
            SlippageModeled = false
        };

    public PipValueSymbolRiskApi CreateSymbolApi() =>
        new(PipSize, PipValue, VolumeInUnitsMin, VolumeInUnitsMax, VolumeInUnitsStep)
        {
            SpreadPrice = SpreadPrice > 0 ? SpreadPrice : null,
            RoundTripCommissionPerLot = CommissionModeled ? CommissionRoundTrip : null,
            LotSizeInUnits = LotSizeInUnits
        };
}
