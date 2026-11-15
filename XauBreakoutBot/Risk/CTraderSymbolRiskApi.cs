using System;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots.Risk;

/// <summary>
/// Adapter live sobre <see cref="Symbol"/> de cTrader Automate.
/// Usa VolumeForFixedRisk / AmountRisked / NormalizeVolumeInUnits reales.
/// Comisión solo se estima cuando CommissionType = UsdPerOneLot (datos fiables).
/// </summary>
public sealed class CTraderSymbolRiskApi : ISymbolRiskApi
{
    private readonly Symbol _symbol;

    public CTraderSymbolRiskApi(Symbol symbol)
    {
        _symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
    }

    public double PipSize => _symbol.PipSize;
    public double PipValue => _symbol.PipValue;
    public double VolumeInUnitsMin => _symbol.VolumeInUnitsMin;
    public double VolumeInUnitsMax => _symbol.VolumeInUnitsMax;
    public double VolumeInUnitsStep => _symbol.VolumeInUnitsStep;

    public double? SpreadPrice
    {
        get
        {
            var spread = _symbol.Spread;
            return double.IsFinite(spread) && spread >= 0 ? spread : null;
        }
    }

    public double VolumeForFixedRisk(double amount, double stopLossInPips) =>
        _symbol.VolumeForFixedRisk(amount, stopLossInPips);

    public double AmountRisked(double volumeInUnits, double stopLossInPips) =>
        _symbol.AmountRisked(volumeInUnits, stopLossInPips);

    public double NormalizeVolumeInUnitsDown(double volumeInUnits) =>
        _symbol.NormalizeVolumeInUnits(volumeInUnits, RoundingMode.Down);

    public double? TryEstimateRoundTripCommission(double volumeInUnits)
    {
        // Solo modelamos UsdPerOneLot: Commission es USD por 1 lote.
        // Ida+vuelta ≈ 2 × commission × (volume / LotSize).
        // Otros tipos (p.ej. UsdPerMillionUsdVolume) requieren conversión no garantizada.
        if (_symbol.CommissionType != SymbolCommissionType.UsdPerOneLot)
            return null;

        var commission = _symbol.Commission;
        var lotSize = _symbol.LotSize;
        if (!double.IsFinite(commission) || commission < 0)
            return null;
        if (lotSize <= 0)
            return null;
        if (!double.IsFinite(volumeInUnits) || volumeInUnits <= 0)
            return null;

        return 2.0 * commission * (volumeInUnits / lotSize);
    }
}
