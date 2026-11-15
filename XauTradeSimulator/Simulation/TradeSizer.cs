using cAlgo.Robots.Risk;
using cAlgo.Robots.SupportResistance;

namespace XauTradeSimulator.Simulation;

/// <summary>
/// Dimensiona SL estructural, TP desde entrada efectiva y volumen con semántica Risk.
/// No modifica RiskPlanCalculator; reutiliza ISymbolRiskApi y las mismas reglas de floor.
/// </summary>
public static class TradeSizer
{
    public sealed class SizedTrade
    {
        public required double StructuralStop { get; init; }
        public required double TakeProfit { get; init; }
        public required double R { get; init; }
        public required double RiskBudget { get; init; }
        public required double? VolumeInUnits { get; init; }
        public required bool IsValid { get; init; }
        public required string? RejectionReason { get; init; }
    }

    public static double StructuralStop(T3BreakoutSignal signal, RiskParameters risk)
    {
        var buffer = risk.StopBufferAtr * signal.AtrAtBreakout;
        return signal.Direction == SignalDirection.Buy
            ? signal.LowerBoundAtBreakout - buffer
            : signal.UpperBoundAtBreakout + buffer;
    }

    public static SizedTrade Size(
        T3BreakoutSignal signal,
        double entryPrice,
        double equity,
        RiskParameters risk,
        ISymbolRiskApi symbol)
    {
        if (!risk.TryValidate(out _))
            return Fail(0, 0, 0, 0, RejectionReasons.InvalidEquity);

        if (!IsFinitePositive(equity))
            return Fail(0, 0, 0, 0, RejectionReasons.InvalidEquity);

        var stop = StructuralStop(signal, risk);
        if (!IsFinite(stop))
            return Fail(stop, 0, 0, 0, RejectionReasons.InvalidStopDistance);

        if (signal.Direction == SignalDirection.Buy && !(stop < entryPrice))
            return Fail(stop, 0, 0, 0, RejectionReasons.StopLossWrongSide);

        if (signal.Direction == SignalDirection.Sell && !(stop > entryPrice))
            return Fail(stop, 0, 0, 0, RejectionReasons.StopLossWrongSide);

        var r = Math.Abs(entryPrice - stop);
        if (!IsFinitePositive(r))
            return Fail(stop, 0, 0, 0, RejectionReasons.InvalidStopDistance);

        var tp = signal.Direction == SignalDirection.Buy
            ? entryPrice + risk.RewardRiskRatio * r
            : entryPrice - risk.RewardRiskRatio * r;

        if (!IsFinite(tp) || Math.Abs(tp - entryPrice) <= 0)
            return Fail(stop, tp, r, 0, RejectionReasons.InvalidTakeProfit);

        var riskBudget = equity * risk.RiskPercent / 100.0;
        if (!IsFinitePositive(riskBudget))
            return Fail(stop, tp, r, 0, RejectionReasons.InvalidEquity);

        if (!IsSymbolUsable(symbol))
            return Fail(stop, tp, r, riskBudget, RejectionReasons.InvalidSymbolData);

        var stopPips = r / symbol.PipSize;
        if (!IsFinitePositive(stopPips))
            return Fail(stop, tp, r, riskBudget, RejectionReasons.InvalidStopDistance);

        var minRisk = symbol.AmountRisked(symbol.VolumeInUnitsMin, stopPips);
        if (!IsFinite(minRisk) || minRisk < 0)
            return Fail(stop, tp, r, riskBudget, RejectionReasons.InvalidSymbolData);

        if (minRisk > riskBudget + 1e-9)
            return Fail(stop, tp, r, riskBudget, RejectionReasons.MinVolumeExceedsRiskBudget);

        var rawVolume = symbol.VolumeForFixedRisk(riskBudget, stopPips);
        if (!IsFinite(rawVolume) || rawVolume <= 0)
            return Fail(stop, tp, r, riskBudget, RejectionReasons.VolumeBelowMinimum);

        var volume = symbol.NormalizeVolumeInUnitsDown(rawVolume);
        if (volume > symbol.VolumeInUnitsMax)
            volume = symbol.NormalizeVolumeInUnitsDown(symbol.VolumeInUnitsMax);

        if (!IsFinite(volume) || volume < symbol.VolumeInUnitsMin - 1e-12)
            return Fail(stop, tp, r, riskBudget, RejectionReasons.VolumeBelowMinimum);

        var priceRisk = symbol.AmountRisked(volume, stopPips);
        while (volume >= symbol.VolumeInUnitsMin &&
               (priceRisk > riskBudget + 1e-9 || !IsFinite(priceRisk)))
        {
            var next = volume - symbol.VolumeInUnitsStep;
            volume = symbol.NormalizeVolumeInUnitsDown(next);
            if (volume < symbol.VolumeInUnitsMin - 1e-12)
                break;
            priceRisk = symbol.AmountRisked(volume, stopPips);
        }

        if (volume < symbol.VolumeInUnitsMin - 1e-12 || priceRisk > riskBudget + 1e-9 || !IsFinite(priceRisk))
            return Fail(stop, tp, r, riskBudget, RejectionReasons.ExcessiveMonetaryRisk);

        return new SizedTrade
        {
            StructuralStop = stop,
            TakeProfit = tp,
            R = r,
            RiskBudget = riskBudget,
            VolumeInUnits = volume,
            IsValid = true,
            RejectionReason = null
        };
    }

    /// <summary>
    /// PnL monetario: volume × (priceDiff / PipSize) × PipValue.
    /// El spread ya está embebido en Entry/Exit Bid-Ask; no se resta otra vez.
    /// </summary>
    public static double PricePnL(
        SignalDirection direction,
        double entryPrice,
        double exitPrice,
        double volumeInUnits,
        ISymbolRiskApi symbol)
    {
        var signed = direction == SignalDirection.Buy
            ? exitPrice - entryPrice
            : entryPrice - exitPrice;
        var pips = signed / symbol.PipSize;
        return volumeInUnits * pips * symbol.PipValue;
    }

    private static SizedTrade Fail(
        double stop, double tp, double r, double budget, string reason) =>
        new()
        {
            StructuralStop = stop,
            TakeProfit = tp,
            R = r,
            RiskBudget = budget,
            VolumeInUnits = null,
            IsValid = false,
            RejectionReason = reason
        };

    private static bool IsSymbolUsable(ISymbolRiskApi s) =>
        IsFinitePositive(s.PipSize) &&
        IsFinitePositive(s.PipValue) &&
        IsFinitePositive(s.VolumeInUnitsMin) &&
        IsFinitePositive(s.VolumeInUnitsMax) &&
        IsFinitePositive(s.VolumeInUnitsStep) &&
        s.VolumeInUnitsMax >= s.VolumeInUnitsMin &&
        s.VolumeInUnitsStep <= s.VolumeInUnitsMax;

    private static bool IsFinite(double v) => double.IsFinite(v);
    private static bool IsFinitePositive(double v) => double.IsFinite(v) && v > 0;
}
