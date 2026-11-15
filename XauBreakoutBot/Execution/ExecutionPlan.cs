using cAlgo.Robots.SupportResistance;

namespace cAlgo.Robots.Execution;

/// <summary>
/// Plan de ejecución DEMO preparado a partir de una señal T3.
/// Contrato independiente de <c>RiskPlan</c>. No implica envío de órdenes.
/// </summary>
public sealed class ExecutionPlan
{
    public const string EntryPriceKind = "AskForBuy_BidForSell";

    public string SignalId { get; init; } = "";
    public long LevelId { get; init; }
    public int BreakoutBarIndex { get; init; }
    public SignalDirection Direction { get; init; }

    /// <summary>Ask (BUY) o Bid (SELL). Cotización ejecutable de referencia.</summary>
    public double ExecutableEntryPrice { get; init; }

    /// <summary>ConfirmationClose de la señal (auditoría; no es la entrada del plan).</summary>
    public double SignalConfirmationClose { get; init; }

    /// <summary>SL estructural del snapshot de breakout (no recalculado desde geometría posterior).</summary>
    public double StructuralStopLossPrice { get; init; }

    /// <summary>TP = Entry ± RewardRiskRatio × R, con R desde el precio ejecutable.</summary>
    public double TakeProfitPrice { get; init; }

    public double StopDistancePips { get; init; }
    public double RewardRiskRatio { get; init; }
    public double? VolumeInUnits { get; init; }
    public double Equity { get; init; }
    public double RiskBudget { get; init; }
    public double? EstimatedMonetaryRisk { get; init; }
    public double ObservedSpread { get; init; }
    public double ObservedSpreadPips { get; init; }
    public bool IsEligible { get; init; }
    public string? RejectionReason { get; init; }

    public string EntryKind => EntryPriceKind;
}
