namespace cAlgo.Robots.Risk;

/// <summary>
/// Abstracción testeable de las APIs de cTrader usadas para convertir
/// distancia de SL en riesgo monetario y volumen en unidades (no lotes).
/// </summary>
public interface ISymbolRiskApi
{
    double PipSize { get; }
    double PipValue { get; }
    double VolumeInUnitsMin { get; }
    double VolumeInUnitsMax { get; }
    double VolumeInUnitsStep { get; }

    /// <summary>Spread actual en precio (Ask − Bid), si está disponible.</summary>
    double? SpreadPrice { get; }

    /// <summary>
    /// Comisión estimable de ida+vuelta en moneda de cuenta para el volumen dado,
    /// solo cuando el tipo de comisión es fiable. Null → costes no modelados.
    /// </summary>
    double? TryEstimateRoundTripCommission(double volumeInUnits);

    /// <summary>
    /// Equivalente a <c>Symbol.VolumeForFixedRisk(amount, stopLossInPips)</c>
    /// sin redondeo de broker; el calculador aplica floor al step.
    /// </summary>
    double VolumeForFixedRisk(double amount, double stopLossInPips);

    /// <summary>Equivalente a <c>Symbol.AmountRisked(volume, stopLossInPips)</c>.</summary>
    double AmountRisked(double volumeInUnits, double stopLossInPips);

    /// <summary>
    /// Equivalente a <c>Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down)</c>.
    /// Nunca redondea hacia arriba.
    /// </summary>
    double NormalizeVolumeInUnitsDown(double volumeInUnits);
}
