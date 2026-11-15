namespace cAlgo.Robots.Execution;

/// <summary>
/// Especificaciones de símbolo/cuenta seguras para log DEMO.
/// Sin números de cuenta ni credenciales.
/// </summary>
public sealed class SymbolSpecSnapshot
{
    public string SymbolName { get; init; } = "";
    public double PipSize { get; init; }
    public double TickSize { get; init; }
    public double PipValue { get; init; }
    public double VolumeInUnitsMin { get; init; }
    public double VolumeInUnitsMax { get; init; }
    public double VolumeInUnitsStep { get; init; }
    public long LotSize { get; init; }
    public double Bid { get; init; }
    public double Ask { get; init; }
    public double Spread { get; init; }
    public int Digits { get; init; }
    public bool IsTradingEnabled { get; init; }
    public double MinStopLossDistance { get; init; }
    public double MinTakeProfitDistance { get; init; }
    public MinDistanceUnit MinDistanceUnit { get; init; }
    public bool AccountIsLive { get; init; }
    public string AccountAsset { get; init; } = "";

    public string FormatSafeLogLine() =>
        $"SymbolSpec DEMO-safe: Symbol={SymbolName} PipSize={PipSize} TickSize={TickSize} PipValue={PipValue} " +
        $"VolMin={VolumeInUnitsMin} VolMax={VolumeInUnitsMax} VolStep={VolumeInUnitsStep} LotSize={LotSize} " +
        $"Bid={Bid:0.#####} Ask={Ask:0.#####} Spread={Spread:0.#####} Digits={Digits} TradingEnabled={IsTradingEnabled} " +
        $"MinSL={MinStopLossDistance} MinTP={MinTakeProfitDistance} MinDistUnit={MinDistanceUnit} " +
        $"AccountIsLive={AccountIsLive} AccountAsset={AccountAsset}";
}
