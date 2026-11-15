using System;
using System.Collections.Generic;
using cAlgo.Robots.Risk;
using cAlgo.Robots.SupportResistance;

namespace cAlgo.Robots.Execution;

/// <summary>
/// Construye <see cref="ExecutionPlan"/> con validaciones de seguridad DEMO.
/// Recalcula volumen y TP desde cotización Ask/Bid; conserva SL estructural.
/// No envía órdenes. Si todo pasa y <c>OrdersEnabled=false</c>, el plan queda
/// elegible para preparación con <see cref="ExecutionRejectionReasons.OrdersDisabled"/>.
/// </summary>
public static class ExecutionPlanBuilder
{
    public static ExecutionPlan Build(
        T3BreakoutSignal signal,
        RiskParameters riskParameters,
        ExecutionParameters executionParameters,
        ExecutionMarketContext market,
        ISymbolRiskApi? symbolRisk,
        BotPositionGate.EligibilityResult? positionEligibility = null,
        ISet<string>? alreadyPlannedSignalIds = null)
    {
        var signalId = RiskPlanCalculator.BuildSignalId(signal);

        if (!executionParameters.TryValidate(out var execParamReason))
            return Reject(signal, signalId, riskParameters, market, execParamReason);

        if (!riskParameters.TryValidate(out var riskParamReason))
            return Reject(signal, signalId, riskParameters, market, riskParamReason);

        if (market.AccountIsLive)
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.LiveAccountForbidden);

        if (alreadyPlannedSignalIds != null && alreadyPlannedSignalIds.Contains(signalId))
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.SignalDuplicate);

        if (market.CurrentClosedBarIndex < signal.BreakoutBarIndex ||
            market.CurrentClosedBarIndex > signal.BreakoutBarIndex + executionParameters.MaxSignalAgeBars)
        {
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.SignalExpired);
        }

        if (positionEligibility is { IsEligible: false })
        {
            return Reject(
                signal,
                signalId,
                riskParameters,
                market,
                string.IsNullOrEmpty(positionEligibility.Reason)
                    ? ExecutionRejectionReasons.MaxOpenPositionsReached
                    : MapPositionReason(positionEligibility.Reason));
        }

        if (!market.IsTradingEnabled)
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.SymbolTradingDisabled);

        if (!IsFinitePositive(market.Bid) || !IsFinitePositive(market.Ask) || market.Ask < market.Bid)
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.InvalidQuote);

        var spread = market.Spread;
        if (!double.IsFinite(spread) || spread < 0)
            spread = market.Ask - market.Bid;

        if (!double.IsFinite(spread) || spread < 0)
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.InvalidOrExcessiveSpread);

        if (!IsFinitePositive(market.PipSize))
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.InvalidSymbolData);

        var spreadPips = spread / market.PipSize;
        if (!double.IsFinite(spreadPips) || spreadPips > executionParameters.MaxSpreadPips)
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.InvalidOrExcessiveSpread,
                spread: spread, spreadPips: spreadPips);

        if (!IsFinitePositive(signal.AtrAtBreakout))
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.InvalidAtr,
                spread: spread, spreadPips: spreadPips);

        if (!IsFinite(signal.UpperBoundAtBreakout) || !IsFinite(signal.LowerBoundAtBreakout))
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.InvalidStopDistance,
                spread: spread, spreadPips: spreadPips);

        // SL estructural: geometría causal del snapshot de breakout (no posterior).
        var buffer = riskParameters.StopBufferAtr * signal.AtrAtBreakout;
        double structuralStop = signal.Direction == SignalDirection.Buy
            ? signal.LowerBoundAtBreakout - buffer
            : signal.UpperBoundAtBreakout + buffer;

        if (!IsFinite(structuralStop))
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.InvalidStopDistance,
                spread: spread, spreadPips: spreadPips);

        var executableEntry = signal.Direction == SignalDirection.Buy ? market.Ask : market.Bid;

        if (signal.Direction == SignalDirection.Buy && !(structuralStop < executableEntry))
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.StopLossWrongSide,
                spread: spread, spreadPips: spreadPips, entry: executableEntry, stop: structuralStop);

        if (signal.Direction == SignalDirection.Sell && !(structuralStop > executableEntry))
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.StopLossWrongSide,
                spread: spread, spreadPips: spreadPips, entry: executableEntry, stop: structuralStop);

        var r = Math.Abs(executableEntry - structuralStop);
        if (!IsFinitePositive(r))
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.InvalidStopDistance,
                spread: spread, spreadPips: spreadPips, entry: executableEntry, stop: structuralStop);

        var takeProfit = signal.Direction == SignalDirection.Buy
            ? executableEntry + riskParameters.RewardRiskRatio * r
            : executableEntry - riskParameters.RewardRiskRatio * r;

        if (!IsFinite(takeProfit) || Math.Abs(takeProfit - executableEntry) <= 0)
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.InvalidTakeProfit,
                spread: spread, spreadPips: spreadPips, entry: executableEntry, stop: structuralStop, tp: takeProfit);

        if (!MeetsMinDistance(executableEntry, structuralStop, market.MinStopLossDistance, market.MinDistanceUnit, market.PipSize) ||
            !MeetsMinDistance(executableEntry, takeProfit, market.MinTakeProfitDistance, market.MinDistanceUnit, market.PipSize))
        {
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.SymbolConstraintsViolated,
                spread: spread, spreadPips: spreadPips, entry: executableEntry, stop: structuralStop, tp: takeProfit);
        }

        if (!IsFinitePositive(market.Equity))
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.InvalidEquity,
                spread: spread, spreadPips: spreadPips, entry: executableEntry, stop: structuralStop, tp: takeProfit);

        var riskBudget = market.Equity * riskParameters.RiskPercent / 100.0;
        if (!IsFinitePositive(riskBudget))
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.InvalidEquity,
                spread: spread, spreadPips: spreadPips, entry: executableEntry, stop: structuralStop, tp: takeProfit);

        if (symbolRisk == null || !IsSymbolRiskUsable(symbolRisk))
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.InsufficientRiskData,
                spread: spread, spreadPips: spreadPips, entry: executableEntry, stop: structuralStop, tp: takeProfit,
                equity: market.Equity, budget: riskBudget);

        var stopPips = r / symbolRisk.PipSize;
        if (!IsFinitePositive(stopPips))
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.InvalidStopDistance,
                spread: spread, spreadPips: spreadPips, entry: executableEntry, stop: structuralStop, tp: takeProfit,
                equity: market.Equity, budget: riskBudget);

        var minRisk = symbolRisk.AmountRisked(symbolRisk.VolumeInUnitsMin, stopPips);
        if (!IsFinite(minRisk) || minRisk < 0)
            return Reject(signal, signalId, riskParameters, market, ExecutionRejectionReasons.InsufficientRiskData,
                spread: spread, spreadPips: spreadPips, entry: executableEntry, stop: structuralStop, tp: takeProfit,
                equity: market.Equity, budget: riskBudget);

        if (minRisk > riskBudget + 1e-9)
        {
            return new ExecutionPlan
            {
                SignalId = signalId,
                LevelId = signal.LevelId,
                BreakoutBarIndex = signal.BreakoutBarIndex,
                Direction = signal.Direction,
                ExecutableEntryPrice = executableEntry,
                SignalConfirmationClose = signal.ConfirmationClose,
                StructuralStopLossPrice = structuralStop,
                TakeProfitPrice = takeProfit,
                StopDistancePips = stopPips,
                RewardRiskRatio = riskParameters.RewardRiskRatio,
                VolumeInUnits = null,
                Equity = market.Equity,
                RiskBudget = riskBudget,
                EstimatedMonetaryRisk = minRisk,
                ObservedSpread = spread,
                ObservedSpreadPips = spreadPips,
                IsEligible = false,
                RejectionReason = ExecutionRejectionReasons.MinVolumeExceedsRiskBudget
            };
        }

        var rawVolume = symbolRisk.VolumeForFixedRisk(riskBudget, stopPips);
        if (!IsFinite(rawVolume) || rawVolume <= 0)
            return VolumeReject(signal, signalId, riskParameters, market, executableEntry, structuralStop, takeProfit,
                stopPips, spread, spreadPips, riskBudget, ExecutionRejectionReasons.VolumeBelowMinimum);

        var volume = symbolRisk.NormalizeVolumeInUnitsDown(rawVolume);
        if (volume > symbolRisk.VolumeInUnitsMax)
            volume = symbolRisk.NormalizeVolumeInUnitsDown(symbolRisk.VolumeInUnitsMax);

        if (!IsFinite(volume) || volume < symbolRisk.VolumeInUnitsMin - 1e-12)
            return VolumeReject(signal, signalId, riskParameters, market, executableEntry, structuralStop, takeProfit,
                stopPips, spread, spreadPips, riskBudget, ExecutionRejectionReasons.VolumeBelowMinimum);

        var monetaryRisk = symbolRisk.AmountRisked(volume, stopPips);
        while (volume >= symbolRisk.VolumeInUnitsMin &&
               (monetaryRisk > riskBudget + 1e-9 || !IsFinite(monetaryRisk)))
        {
            var next = volume - symbolRisk.VolumeInUnitsStep;
            volume = symbolRisk.NormalizeVolumeInUnitsDown(next);
            if (volume < symbolRisk.VolumeInUnitsMin - 1e-12)
                break;
            monetaryRisk = symbolRisk.AmountRisked(volume, stopPips);
        }

        if (volume < symbolRisk.VolumeInUnitsMin - 1e-12 || monetaryRisk > riskBudget + 1e-9 || !IsFinite(monetaryRisk))
            return VolumeReject(signal, signalId, riskParameters, market, executableEntry, structuralStop, takeProfit,
                stopPips, spread, spreadPips, riskBudget, ExecutionRejectionReasons.ExcessiveMonetaryRisk,
                monetaryRisk: IsFinite(monetaryRisk) ? monetaryRisk : null);

        // Preparación DEMO OK. OrdersEnabled=false → elegible para prep, motivo OrdersDisabled.
        // El host v1.7 no envía órdenes aunque IsEligible sea true.
        return new ExecutionPlan
        {
            SignalId = signalId,
            LevelId = signal.LevelId,
            BreakoutBarIndex = signal.BreakoutBarIndex,
            Direction = signal.Direction,
            ExecutableEntryPrice = executableEntry,
            SignalConfirmationClose = signal.ConfirmationClose,
            StructuralStopLossPrice = structuralStop,
            TakeProfitPrice = takeProfit,
            StopDistancePips = stopPips,
            RewardRiskRatio = riskParameters.RewardRiskRatio,
            VolumeInUnits = volume,
            Equity = market.Equity,
            RiskBudget = riskBudget,
            EstimatedMonetaryRisk = monetaryRisk,
            ObservedSpread = spread,
            ObservedSpreadPips = spreadPips,
            IsEligible = true,
            RejectionReason = executionParameters.OrdersEnabled
                ? null
                : ExecutionRejectionReasons.OrdersDisabled
        };
    }

    private static string MapPositionReason(string reason) =>
        reason == RiskRejectionReasons.MaxOpenPositionsReached
            ? ExecutionRejectionReasons.MaxOpenPositionsReached
            : reason;

    private static bool MeetsMinDistance(
        double entry,
        double level,
        double minDistance,
        MinDistanceUnit unit,
        double pipSize)
    {
        if (!double.IsFinite(minDistance) || minDistance <= 0)
            return true;

        var actual = Math.Abs(entry - level);
        if (!IsFinitePositive(actual))
            return false;

        double required = unit switch
        {
            MinDistanceUnit.Pips => minDistance * pipSize,
            MinDistanceUnit.Percentage => Math.Abs(entry) * (minDistance / 100.0),
            _ => minDistance * pipSize
        };

        return actual + 1e-12 >= required;
    }

    private static bool IsSymbolRiskUsable(ISymbolRiskApi s) =>
        IsFinitePositive(s.PipSize) &&
        IsFinitePositive(s.PipValue) &&
        IsFinitePositive(s.VolumeInUnitsMin) &&
        IsFinitePositive(s.VolumeInUnitsMax) &&
        IsFinitePositive(s.VolumeInUnitsStep) &&
        s.VolumeInUnitsMax >= s.VolumeInUnitsMin;

    private static ExecutionPlan VolumeReject(
        T3BreakoutSignal signal,
        string signalId,
        RiskParameters riskParameters,
        ExecutionMarketContext market,
        double entry,
        double stop,
        double tp,
        double stopPips,
        double spread,
        double spreadPips,
        double budget,
        string reason,
        double? monetaryRisk = null) =>
        new()
        {
            SignalId = signalId,
            LevelId = signal.LevelId,
            BreakoutBarIndex = signal.BreakoutBarIndex,
            Direction = signal.Direction,
            ExecutableEntryPrice = entry,
            SignalConfirmationClose = signal.ConfirmationClose,
            StructuralStopLossPrice = stop,
            TakeProfitPrice = tp,
            StopDistancePips = stopPips,
            RewardRiskRatio = riskParameters.RewardRiskRatio,
            VolumeInUnits = null,
            Equity = market.Equity,
            RiskBudget = budget,
            EstimatedMonetaryRisk = monetaryRisk,
            ObservedSpread = spread,
            ObservedSpreadPips = spreadPips,
            IsEligible = false,
            RejectionReason = reason
        };

    private static ExecutionPlan Reject(
        T3BreakoutSignal signal,
        string signalId,
        RiskParameters riskParameters,
        ExecutionMarketContext market,
        string reason,
        double spread = double.NaN,
        double spreadPips = double.NaN,
        double entry = double.NaN,
        double stop = double.NaN,
        double tp = double.NaN,
        double equity = double.NaN,
        double budget = double.NaN) =>
        new()
        {
            SignalId = signalId,
            LevelId = signal.LevelId,
            BreakoutBarIndex = signal.BreakoutBarIndex,
            Direction = signal.Direction,
            ExecutableEntryPrice = entry,
            SignalConfirmationClose = signal.ConfirmationClose,
            StructuralStopLossPrice = stop,
            TakeProfitPrice = tp,
            StopDistancePips = 0,
            RewardRiskRatio = riskParameters.RewardRiskRatio,
            VolumeInUnits = null,
            Equity = double.IsFinite(equity) ? equity : market.Equity,
            RiskBudget = double.IsFinite(budget) ? budget : 0,
            EstimatedMonetaryRisk = null,
            ObservedSpread = spread,
            ObservedSpreadPips = spreadPips,
            IsEligible = false,
            RejectionReason = reason
        };

    private static bool IsFinite(double v) => double.IsFinite(v);
    private static bool IsFinitePositive(double v) => double.IsFinite(v) && v > 0;
}
