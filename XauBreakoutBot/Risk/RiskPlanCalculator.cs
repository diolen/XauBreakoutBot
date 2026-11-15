using System;
using cAlgo.Robots.SupportResistance;

namespace cAlgo.Robots.Risk;

/// <summary>
/// Calcula SL/TP/volumen a partir de una <see cref="T3BreakoutSignal"/> y datos de cuenta/símbolo.
/// No ejecuta órdenes. No reconstruye geometría posterior al breakout.
/// </summary>
public static class RiskPlanCalculator
{
    public static string BuildSignalId(T3BreakoutSignal signal) =>
        $"L{signal.LevelId}-B{signal.BreakoutBarIndex}";

    /// <summary>
    /// Construye un plan. Si <paramref name="symbol"/> es null, calcula SL/TP teóricos
    /// y rechaza el volumen (plan parcial inválido). Equity null/ inválido rechaza.
    /// </summary>
    public static RiskPlan Build(
        T3BreakoutSignal signal,
        RiskParameters parameters,
        double? equity,
        ISymbolRiskApi? symbol,
        BotPositionGate.EligibilityResult? positionEligibility = null)
    {
        var signalId = BuildSignalId(signal);

        if (!parameters.TryValidate(out var paramReason))
            return Reject(signalId, signal.Direction, parameters, equity, paramReason);

        if (positionEligibility is { IsEligible: false })
        {
            return Reject(
                signalId,
                signal.Direction,
                parameters,
                equity,
                string.IsNullOrEmpty(positionEligibility.Reason)
                    ? RiskRejectionReasons.MaxOpenPositionsReached
                    : positionEligibility.Reason,
                entry: signal.ConfirmationClose);
        }

        if (!IsFinitePositive(signal.AtrAtBreakout))
            return Reject(signalId, signal.Direction, parameters, equity, RiskRejectionReasons.InvalidAtr,
                entry: signal.ConfirmationClose);

        if (!IsFinite(signal.ConfirmationClose) ||
            !IsFinite(signal.UpperBoundAtBreakout) ||
            !IsFinite(signal.LowerBoundAtBreakout))
        {
            return Reject(signalId, signal.Direction, parameters, equity, RiskRejectionReasons.InvalidStopDistance,
                entry: signal.ConfirmationClose);
        }

        var entry = signal.ConfirmationClose;
        var buffer = parameters.StopBufferAtr * signal.AtrAtBreakout;
        double stop;

        if (signal.Direction == SignalDirection.Buy)
            stop = signal.LowerBoundAtBreakout - buffer;
        else
            stop = signal.UpperBoundAtBreakout + buffer;

        if (!IsFinite(stop))
            return Reject(signalId, signal.Direction, parameters, equity, RiskRejectionReasons.InvalidStopDistance,
                entry: entry);

        if (signal.Direction == SignalDirection.Buy && !(stop < entry))
            return Reject(signalId, signal.Direction, parameters, equity, RiskRejectionReasons.StopLossWrongSide,
                entry: entry, stop: stop);

        if (signal.Direction == SignalDirection.Sell && !(stop > entry))
            return Reject(signalId, signal.Direction, parameters, equity, RiskRejectionReasons.StopLossWrongSide,
                entry: entry, stop: stop);

        var r = Math.Abs(entry - stop);
        if (!IsFinitePositive(r))
            return Reject(signalId, signal.Direction, parameters, equity, RiskRejectionReasons.InvalidStopDistance,
                entry: entry, stop: stop);

        double takeProfit;
        if (signal.Direction == SignalDirection.Buy)
            takeProfit = entry + parameters.RewardRiskRatio * r;
        else
            takeProfit = entry - parameters.RewardRiskRatio * r;

        if (!IsFinite(takeProfit) || Math.Abs(takeProfit - entry) <= 0)
            return Reject(signalId, signal.Direction, parameters, equity, RiskRejectionReasons.InvalidTakeProfit,
                entry: entry, stop: stop, tp: takeProfit);

        if (equity is not { } eq || !IsFinitePositive(eq))
        {
            return new RiskPlan
            {
                SignalId = signalId,
                Direction = signal.Direction,
                EntryReferencePrice = entry,
                StopLossPrice = stop,
                TakeProfitPrice = takeProfit,
                StopDistancePips = 0,
                RewardRiskRatio = parameters.RewardRiskRatio,
                Equity = equity ?? double.NaN,
                RiskBudget = 0,
                VolumeInUnits = null,
                EstimatedPriceRisk = null,
                EstimatedTotalRisk = null,
                IsValid = false,
                RejectionReason = RiskRejectionReasons.InvalidEquity,
                CostsNotFullyModeled = true
            };
        }

        var riskBudget = eq * parameters.RiskPercent / 100.0;
        if (!IsFinitePositive(riskBudget))
            return Reject(signalId, signal.Direction, parameters, eq, RiskRejectionReasons.InvalidEquity,
                entry: entry, stop: stop, tp: takeProfit);

        if (symbol == null || !IsSymbolUsable(symbol))
        {
            return new RiskPlan
            {
                SignalId = signalId,
                Direction = signal.Direction,
                EntryReferencePrice = entry,
                StopLossPrice = stop,
                TakeProfitPrice = takeProfit,
                StopDistancePips = 0,
                RewardRiskRatio = parameters.RewardRiskRatio,
                Equity = eq,
                RiskBudget = riskBudget,
                VolumeInUnits = null,
                EstimatedPriceRisk = null,
                EstimatedTotalRisk = null,
                IsValid = false,
                RejectionReason = RiskRejectionReasons.InvalidSymbolData,
                CostsNotFullyModeled = true
            };
        }

        var stopDistancePips = r / symbol.PipSize;
        if (!IsFinitePositive(stopDistancePips))
        {
            return new RiskPlan
            {
                SignalId = signalId,
                Direction = signal.Direction,
                EntryReferencePrice = entry,
                StopLossPrice = stop,
                TakeProfitPrice = takeProfit,
                StopDistancePips = stopDistancePips,
                RewardRiskRatio = parameters.RewardRiskRatio,
                Equity = eq,
                RiskBudget = riskBudget,
                VolumeInUnits = null,
                EstimatedPriceRisk = null,
                EstimatedTotalRisk = null,
                IsValid = false,
                RejectionReason = RiskRejectionReasons.InvalidStopDistance,
                CostsNotFullyModeled = true
            };
        }

        // Riesgo del volumen mínimo: si supera el presupuesto → rechazar (nunca inventar fallback).
        var minRisk = symbol.AmountRisked(symbol.VolumeInUnitsMin, stopDistancePips);
        if (!IsFinite(minRisk) || minRisk < 0)
        {
            return FailVolume(signalId, signal.Direction, parameters, eq, riskBudget, entry, stop, takeProfit,
                stopDistancePips, RiskRejectionReasons.InvalidSymbolData);
        }

        if (minRisk > riskBudget + 1e-9)
        {
            return FailVolume(signalId, signal.Direction, parameters, eq, riskBudget, entry, stop, takeProfit,
                stopDistancePips, RiskRejectionReasons.MinVolumeExceedsRiskBudget,
                estimatedPriceRisk: minRisk);
        }

        var rawVolume = symbol.VolumeForFixedRisk(riskBudget, stopDistancePips);
        if (!IsFinite(rawVolume) || rawVolume <= 0)
        {
            return FailVolume(signalId, signal.Direction, parameters, eq, riskBudget, entry, stop, takeProfit,
                stopDistancePips, RiskRejectionReasons.VolumeBelowMinimum);
        }

        var volume = symbol.NormalizeVolumeInUnitsDown(rawVolume);
        if (volume > symbol.VolumeInUnitsMax)
            volume = symbol.NormalizeVolumeInUnitsDown(symbol.VolumeInUnitsMax);

        if (!IsFinite(volume) || volume < symbol.VolumeInUnitsMin - 1e-12)
        {
            return FailVolume(signalId, signal.Direction, parameters, eq, riskBudget, entry, stop, takeProfit,
                stopDistancePips, RiskRejectionReasons.VolumeBelowMinimum);
        }

        // Garantizar que el floor no exceda el presupuesto (nunca redondear hacia arriba).
        var priceRisk = symbol.AmountRisked(volume, stopDistancePips);
        while (volume >= symbol.VolumeInUnitsMin &&
               (priceRisk > riskBudget + 1e-9 || !IsFinite(priceRisk)))
        {
            var next = volume - symbol.VolumeInUnitsStep;
            volume = symbol.NormalizeVolumeInUnitsDown(next);
            if (volume < symbol.VolumeInUnitsMin - 1e-12)
                break;
            priceRisk = symbol.AmountRisked(volume, stopDistancePips);
        }

        if (volume < symbol.VolumeInUnitsMin - 1e-12 || priceRisk > riskBudget + 1e-9 || !IsFinite(priceRisk))
        {
            return FailVolume(signalId, signal.Direction, parameters, eq, riskBudget, entry, stop, takeProfit,
                stopDistancePips, RiskRejectionReasons.ExcessiveMonetaryRisk,
                estimatedPriceRisk: IsFinite(priceRisk) ? priceRisk : null);
        }

        double? totalRisk = null;
        var costsNotFullyModeled = true;

        var spreadRisk = TrySpreadRisk(symbol, volume);
        var commission = symbol.TryEstimateRoundTripCommission(volume);
        if (spreadRisk is { } sr && commission is { } cm && IsFinite(sr) && IsFinite(cm) && sr >= 0 && cm >= 0)
        {
            totalRisk = priceRisk + sr + cm;
            costsNotFullyModeled = false;
            // Slippage/gaps siguen sin modelarse; documentado en docs. El flag solo
            // indica si spread+comisión pudieron estimarse con datos fiables.
        }

        return new RiskPlan
        {
            SignalId = signalId,
            Direction = signal.Direction,
            EntryReferencePrice = entry,
            StopLossPrice = stop,
            TakeProfitPrice = takeProfit,
            StopDistancePips = stopDistancePips,
            RewardRiskRatio = parameters.RewardRiskRatio,
            Equity = eq,
            RiskBudget = riskBudget,
            VolumeInUnits = volume,
            EstimatedPriceRisk = priceRisk,
            EstimatedTotalRisk = totalRisk,
            IsValid = true,
            RejectionReason = null,
            CostsNotFullyModeled = costsNotFullyModeled
        };
    }

    private static double? TrySpreadRisk(ISymbolRiskApi symbol, double volume)
    {
        if (symbol.SpreadPrice is not { } spread || !IsFinite(spread) || spread < 0)
            return null;
        if (!IsFinitePositive(symbol.PipSize))
            return null;

        var spreadPips = spread / symbol.PipSize;
        if (!IsFinite(spreadPips) || spreadPips < 0)
            return null;

        var risk = symbol.AmountRisked(volume, spreadPips);
        return IsFinite(risk) && risk >= 0 ? risk : null;
    }

    private static bool IsSymbolUsable(ISymbolRiskApi s) =>
        IsFinitePositive(s.PipSize) &&
        IsFinitePositive(s.PipValue) &&
        IsFinitePositive(s.VolumeInUnitsMin) &&
        IsFinitePositive(s.VolumeInUnitsMax) &&
        IsFinitePositive(s.VolumeInUnitsStep) &&
        s.VolumeInUnitsMax >= s.VolumeInUnitsMin &&
        s.VolumeInUnitsStep <= s.VolumeInUnitsMax;

    private static RiskPlan FailVolume(
        string signalId,
        SignalDirection direction,
        RiskParameters parameters,
        double equity,
        double riskBudget,
        double entry,
        double stop,
        double tp,
        double stopPips,
        string reason,
        double? estimatedPriceRisk = null) =>
        new()
        {
            SignalId = signalId,
            Direction = direction,
            EntryReferencePrice = entry,
            StopLossPrice = stop,
            TakeProfitPrice = tp,
            StopDistancePips = stopPips,
            RewardRiskRatio = parameters.RewardRiskRatio,
            Equity = equity,
            RiskBudget = riskBudget,
            VolumeInUnits = null,
            EstimatedPriceRisk = estimatedPriceRisk,
            EstimatedTotalRisk = null,
            IsValid = false,
            RejectionReason = reason,
            CostsNotFullyModeled = true
        };

    private static RiskPlan Reject(
        string signalId,
        SignalDirection direction,
        RiskParameters parameters,
        double? equity,
        string reason,
        double entry = double.NaN,
        double stop = double.NaN,
        double tp = double.NaN) =>
        new()
        {
            SignalId = signalId,
            Direction = direction,
            EntryReferencePrice = entry,
            StopLossPrice = stop,
            TakeProfitPrice = tp,
            StopDistancePips = 0,
            RewardRiskRatio = parameters.RewardRiskRatio,
            Equity = equity ?? double.NaN,
            RiskBudget = equity is { } e && IsFinitePositive(e)
                ? e * parameters.RiskPercent / 100.0
                : 0,
            VolumeInUnits = null,
            EstimatedPriceRisk = null,
            EstimatedTotalRisk = null,
            IsValid = false,
            RejectionReason = reason,
            CostsNotFullyModeled = true
        };

    private static bool IsFinite(double v) => double.IsFinite(v);
    private static bool IsFinitePositive(double v) => double.IsFinite(v) && v > 0;
}
