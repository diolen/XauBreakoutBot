using System;

namespace cAlgo.Robots.Risk;

/// <summary>
/// Implementación de referencia basada en PipSize/PipValue y pasos de volumen.
/// Refleja la semántica típica de AmountRisked ≈ volume × pips × PipValue.
/// Usada en tests y como fallback documentado; en live se prefiere el adapter cTrader.
/// </summary>
public sealed class PipValueSymbolRiskApi : ISymbolRiskApi
{
    public double PipSize { get; }
    public double PipValue { get; }
    public double VolumeInUnitsMin { get; }
    public double VolumeInUnitsMax { get; }
    public double VolumeInUnitsStep { get; }
    public double? SpreadPrice { get; init; }
    public double? RoundTripCommissionPerLot { get; init; }
    public double LotSizeInUnits { get; init; } = 100_000;

    public PipValueSymbolRiskApi(
        double pipSize,
        double pipValue,
        double volumeInUnitsMin,
        double volumeInUnitsMax,
        double volumeInUnitsStep)
    {
        PipSize = pipSize;
        PipValue = pipValue;
        VolumeInUnitsMin = volumeInUnitsMin;
        VolumeInUnitsMax = volumeInUnitsMax;
        VolumeInUnitsStep = volumeInUnitsStep;
    }

    public double VolumeForFixedRisk(double amount, double stopLossInPips)
    {
        if (!double.IsFinite(amount) || amount <= 0)
            return 0;
        if (!double.IsFinite(stopLossInPips) || stopLossInPips <= 0)
            return 0;
        if (!double.IsFinite(PipValue) || PipValue <= 0)
            return 0;

        return amount / (stopLossInPips * PipValue);
    }

    public double AmountRisked(double volumeInUnits, double stopLossInPips)
    {
        if (!double.IsFinite(volumeInUnits) || volumeInUnits <= 0)
            return 0;
        if (!double.IsFinite(stopLossInPips) || stopLossInPips <= 0)
            return 0;
        if (!double.IsFinite(PipValue) || PipValue <= 0)
            return 0;

        return volumeInUnits * stopLossInPips * PipValue;
    }

    public double NormalizeVolumeInUnitsDown(double volumeInUnits)
    {
        if (!double.IsFinite(volumeInUnits) || volumeInUnits <= 0)
            return 0;
        if (!double.IsFinite(VolumeInUnitsStep) || VolumeInUnitsStep <= 0)
            return 0;

        var steps = Math.Floor(volumeInUnits / VolumeInUnitsStep + 1e-12);
        var normalized = steps * VolumeInUnitsStep;

        // Evitar residuos de coma flotante por encima del step.
        if (normalized > volumeInUnits)
            normalized = Math.Max(0, (steps - 1) * VolumeInUnitsStep);

        return normalized;
    }

    public double? TryEstimateRoundTripCommission(double volumeInUnits)
    {
        if (RoundTripCommissionPerLot is not { } perLot)
            return null;
        if (!double.IsFinite(perLot) || perLot < 0)
            return null;
        if (!double.IsFinite(LotSizeInUnits) || LotSizeInUnits <= 0)
            return null;
        if (!double.IsFinite(volumeInUnits) || volumeInUnits <= 0)
            return null;

        return perLot * (volumeInUnits / LotSizeInUnits);
    }
}
