namespace cAlgo.Robots.Risk;

public static class RiskRejectionReasons
{
    public const string InvalidParameters = "InvalidParameters";
    public const string InvalidEquity = "InvalidEquity";
    public const string InvalidAtr = "InvalidAtr";
    public const string StopLossWrongSide = "StopLossWrongSide";
    public const string InvalidStopDistance = "InvalidStopDistance";
    public const string InvalidSymbolData = "InvalidSymbolData";
    public const string VolumeBelowMinimum = "VolumeBelowMinimum";
    public const string MinVolumeExceedsRiskBudget = "MinVolumeExceedsRiskBudget";
    public const string ExcessiveMonetaryRisk = "ExcessiveMonetaryRisk";
    public const string MaxOpenPositionsReached = "MaxOpenPositionsReached";
    public const string InvalidTakeProfit = "InvalidTakeProfit";
}
