namespace cAlgo.Robots.SupportResistance;

public enum SignalDirection
{
    Buy = 0,
    Sell = 1
}

/// <summary>
/// Señal de breakout estructural tras T=3. No implica orden ni gestión de riesgo.
/// </summary>
public sealed class T3BreakoutSignal
{
    public long LevelId { get; init; }
    public LevelRole LevelRole { get; init; }
    public SignalDirection Direction { get; init; }
    public int T3BarIndex { get; init; }
    public int BreakoutBarIndex { get; init; }
    public DateTime BreakoutTime { get; init; }
    public double ConfirmationClose { get; init; }
    public double LevelMidAtBreakout { get; init; }
    public double UpperBoundAtBreakout { get; init; }
    public double LowerBoundAtBreakout { get; init; }
    public double HalfWidthAtBreakout { get; init; }
    public double AtrAtBreakout { get; init; }
    public double? AtrRefAtBreakout { get; init; }
    public string BreakoutReason { get; init; } = "";
    public LevelGeometry Geometry { get; init; }
}

/// <summary>
/// Snapshot causal capturado en el instante previo a marcar Invalidated (replay OHLC).
/// </summary>
public sealed class StructuralInvalidationSnapshot
{
    public long LevelId { get; init; }
    public LevelRole Role { get; init; }
    public LevelGeometry Geometry { get; init; }
    public LevelState StateBefore { get; init; }
    public int BarIndex { get; init; }
    public DateTime Time { get; init; }
    public double Close { get; init; }
    public double Mid { get; init; }
    public double UpperBound { get; init; }
    public double LowerBound { get; init; }
    public double HalfWidth { get; init; }
    public double AtrCurrent { get; init; }
    public double? AtrRef { get; init; }
    public string ReasonTag { get; init; } = "";
    public bool WasOhlcProxy { get; init; }
    public int IndependentTouchCount { get; init; }
}
