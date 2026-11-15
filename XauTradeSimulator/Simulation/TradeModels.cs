using cAlgo.Robots.SupportResistance;

namespace XauTradeSimulator.Simulation;

public static class ExitReasons
{
    public const string StopLoss = "StopLoss";
    public const string TakeProfit = "TakeProfit";
    public const string EndOfData = "EndOfData";
}

public static class RejectionReasons
{
    public const string MissingNextBar = "MissingNextBar";
    public const string PositionAlreadyOpen = "PositionAlreadyOpen";
    public const string StopLossWrongSide = "StopLossWrongSide";
    public const string InvalidStopDistance = "InvalidStopDistance";
    public const string InvalidTakeProfit = "InvalidTakeProfit";
    public const string MinVolumeExceedsRiskBudget = "MinVolumeExceedsRiskBudget";
    public const string VolumeBelowMinimum = "VolumeBelowMinimum";
    public const string ExcessiveMonetaryRisk = "ExcessiveMonetaryRisk";
    public const string InvalidSymbolData = "InvalidSymbolData";
    public const string InvalidEquity = "InvalidEquity";
}

public sealed class SimulatedTrade
{
    public required string SignalId { get; init; }
    public required long LevelId { get; init; }
    public required SignalDirection Direction { get; init; }
    public required int SignalBarIndex { get; init; }
    public required int EntryBarIndex { get; init; }
    public required DateTime EntryTime { get; init; }
    public required double EntryPrice { get; init; }
    public required double StopLoss { get; init; }
    public required double TakeProfit { get; init; }
    public required double VolumeInUnits { get; init; }
    public required double RiskBudget { get; init; }
    public required double EquityAtEntry { get; init; }
    public required int ExitBarIndex { get; init; }
    public required DateTime ExitTime { get; init; }
    public required double ExitPrice { get; init; }
    public required string ExitReason { get; init; }
    public required double GrossPnL { get; init; }
    public required double NetPnL { get; init; }
    public required double RealizedR { get; init; }
    public required bool AmbiguousSameBar { get; init; }
    public required bool GapExit { get; init; }
    public required int HoldingBars { get; init; }
    public required string ScenarioName { get; init; }
}

public sealed class RejectedSignal
{
    public required string SignalId { get; init; }
    public required long LevelId { get; init; }
    public required SignalDirection Direction { get; init; }
    public required int SignalBarIndex { get; init; }
    public required string Reason { get; init; }
    public required string ScenarioName { get; init; }
    public double? EntryPrice { get; init; }
    public double? StopLoss { get; init; }
    public double? TakeProfit { get; init; }
    public double? RiskBudget { get; init; }
    public double? EquityAtAttempt { get; init; }
}

public sealed class EquityPoint
{
    public required int TradeSequence { get; init; }
    public required string SignalId { get; init; }
    public required int ExitBarIndex { get; init; }
    public required DateTime ExitTime { get; init; }
    public required double Equity { get; init; }
    public required double DrawdownUsd { get; init; }
    public required double DrawdownPct { get; init; }
    public required string ScenarioName { get; init; }
}

public sealed class SimulationMetrics
{
    public required string ScenarioName { get; init; }
    public required int TotalSignals { get; init; }
    public required int TradesOpened { get; init; }
    public required int TradesRejected { get; init; }
    public required int ClosedTrades { get; init; }
    public required int Wins { get; init; }
    public required int Losses { get; init; }
    public required double WinRate { get; init; }
    public required double GrossProfit { get; init; }
    public required double GrossLoss { get; init; }
    public required double NetPnL { get; init; }
    public required double ProfitFactor { get; init; }
    public required double ExpectancyPerTrade { get; init; }
    public required double AverageR { get; init; }
    public required double MaxDrawdownUsd { get; init; }
    public required double MaxDrawdownPct { get; init; }
    public required double EndingEquity { get; init; }
    public required double AverageHoldingBars { get; init; }
    public required int AmbiguousSameBarCount { get; init; }
    public required int GapExitCount { get; init; }
    public required IReadOnlyDictionary<string, int> RejectionReasonCounts { get; init; }
    public required IReadOnlyDictionary<string, int> ExitReasonCounts { get; init; }
}

public sealed class SimulationResult
{
    public required string ScenarioName { get; init; }
    public required IReadOnlyList<T3BreakoutSignal> Signals { get; init; }
    public required IReadOnlyList<SimulatedTrade> Trades { get; init; }
    public required IReadOnlyList<RejectedSignal> Rejected { get; init; }
    public required IReadOnlyList<EquityPoint> EquityCurve { get; init; }
    public required SimulationMetrics Metrics { get; init; }
    public required double InitialEquity { get; init; }
    public required double SpreadPrice { get; init; }
    public required bool CommissionModeled { get; init; }
    public required bool SlippageModeled { get; init; }
}
