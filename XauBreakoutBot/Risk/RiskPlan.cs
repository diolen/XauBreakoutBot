using cAlgo.Robots.SupportResistance;

namespace cAlgo.Robots.Risk;

/// <summary>
/// Plan de riesgo derivado de una <see cref="T3BreakoutSignal"/>.
/// Contrato separado de la señal. No implica ejecución de órdenes.
/// </summary>
public sealed class RiskPlan
{
    /// <summary>
    /// El precio de entrada de referencia es el ConfirmationClose de la señal:
    /// teórico / no necesariamente ejecutable. El módulo de ejecución futuro
    /// debe recalcular el riesgo con precio real o cotización antes de enviar órdenes.
    /// </summary>
    public const string EntryReferenceKind = "ConfirmationCloseTheoretical";

    public string SignalId { get; init; } = "";
    public SignalDirection Direction { get; init; }
    public double EntryReferencePrice { get; init; }
    public double StopLossPrice { get; init; }
    public double TakeProfitPrice { get; init; }
    public double StopDistancePips { get; init; }
    public double RewardRiskRatio { get; init; }
    public double Equity { get; init; }
    public double RiskBudget { get; init; }
    public double? VolumeInUnits { get; init; }
    public double? EstimatedPriceRisk { get; init; }
    public double? EstimatedTotalRisk { get; init; }
    public bool IsValid { get; init; }
    public string? RejectionReason { get; init; }
    public bool CostsNotFullyModeled { get; init; }

    /// <summary>Metadato explícito: la entrada no es un precio de ejecución.</summary>
    public string EntryPriceKind => EntryReferenceKind;
}
