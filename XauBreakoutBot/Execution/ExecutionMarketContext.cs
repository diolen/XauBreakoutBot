namespace cAlgo.Robots.Execution;

/// <summary>
/// Contexto de mercado/cuenta para construir un <see cref="ExecutionPlan"/>.
/// Independiente de RiskPlan; inyectable en tests.
/// </summary>
public sealed class ExecutionMarketContext
{
    public bool AccountIsLive { get; init; }
    public string AccountAsset { get; init; } = "";
    public double Equity { get; init; }
    public double Bid { get; init; }
    public double Ask { get; init; }
    public double Spread { get; init; }
    public string SymbolName { get; init; } = "";
    public double PipSize { get; init; }
    public double TickSize { get; init; }
    public double PipValue { get; init; }
    public double VolumeInUnitsMin { get; init; }
    public double VolumeInUnitsMax { get; init; }
    public double VolumeInUnitsStep { get; init; }
    public long LotSize { get; init; }
    public int Digits { get; init; }
    public bool IsTradingEnabled { get; init; } = true;
    public double MinStopLossDistance { get; init; }
    public double MinTakeProfitDistance { get; init; }
    public MinDistanceUnit MinDistanceUnit { get; init; } = MinDistanceUnit.Pips;

    /// <summary>Índice de la última barra cerrada procesada (edad de señal).</summary>
    public int CurrentClosedBarIndex { get; init; }

    public SymbolSpecSnapshot ToSymbolSpecSnapshot() => new()
    {
        SymbolName = SymbolName,
        PipSize = PipSize,
        TickSize = TickSize,
        PipValue = PipValue,
        VolumeInUnitsMin = VolumeInUnitsMin,
        VolumeInUnitsMax = VolumeInUnitsMax,
        VolumeInUnitsStep = VolumeInUnitsStep,
        LotSize = LotSize,
        Bid = Bid,
        Ask = Ask,
        Spread = Spread,
        Digits = Digits,
        IsTradingEnabled = IsTradingEnabled,
        MinStopLossDistance = MinStopLossDistance,
        MinTakeProfitDistance = MinTakeProfitDistance,
        MinDistanceUnit = MinDistanceUnit,
        AccountIsLive = AccountIsLive,
        AccountAsset = AccountAsset
    };
}
