namespace cAlgo.Robots.Execution;

public static class ExecutionRejectionReasons
{
    public const string LiveAccountForbidden = "LiveAccountForbidden";
    public const string InvalidQuote = "InvalidQuote";
    public const string InvalidOrExcessiveSpread = "InvalidOrExcessiveSpread";
    public const string SymbolConstraintsViolated = "SymbolConstraintsViolated";
    public const string SymbolTradingDisabled = "SymbolTradingDisabled";
    public const string InvalidSymbolData = "InvalidSymbolData";
    public const string InvalidEquity = "InvalidEquity";
    public const string InvalidAtr = "InvalidAtr";
    public const string StopLossWrongSide = "StopLossWrongSide";
    public const string InvalidStopDistance = "InvalidStopDistance";
    public const string InvalidTakeProfit = "InvalidTakeProfit";
    public const string MinVolumeExceedsRiskBudget = "MinVolumeExceedsRiskBudget";
    public const string VolumeBelowMinimum = "VolumeBelowMinimum";
    public const string ExcessiveMonetaryRisk = "ExcessiveMonetaryRisk";
    public const string MaxOpenPositionsReached = "MaxOpenPositionsReached";
    public const string SignalExpired = "SignalExpired";
    public const string SignalDuplicate = "SignalDuplicate";
    public const string InvalidParameters = "InvalidParameters";
    public const string InsufficientRiskData = "InsufficientRiskData";
    public const string OrdersDisabled = "OrdersDisabled";
}
