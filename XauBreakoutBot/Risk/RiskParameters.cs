using System;

namespace cAlgo.Robots.Risk;

/// <summary>
/// Parámetros configurables de gestión de riesgo (MVP). No optimizados.
/// </summary>
public sealed class RiskParameters
{
    public const double DefaultRiskPercent = 0.5;
    public const double DefaultStopBufferAtr = 0.10;
    public const double DefaultRewardRiskRatio = 2.0;
    public const int DefaultMaxOpenPositions = 1;
    public const string DefaultBotPositionLabel = "XauBreakoutBot";

    public double RiskPercent { get; init; } = DefaultRiskPercent;
    public double StopBufferAtr { get; init; } = DefaultStopBufferAtr;
    public double RewardRiskRatio { get; init; } = DefaultRewardRiskRatio;
    public int MaxOpenPositions { get; init; } = DefaultMaxOpenPositions;
    public string BotPositionLabel { get; init; } = DefaultBotPositionLabel;

    public bool TryValidate(out string rejectionReason)
    {
        if (!IsFinitePositive(RiskPercent))
        {
            rejectionReason = RiskRejectionReasons.InvalidParameters;
            return false;
        }

        if (!IsFiniteNonNegative(StopBufferAtr))
        {
            rejectionReason = RiskRejectionReasons.InvalidParameters;
            return false;
        }

        if (!IsFinitePositive(RewardRiskRatio))
        {
            rejectionReason = RiskRejectionReasons.InvalidParameters;
            return false;
        }

        if (MaxOpenPositions < 1)
        {
            rejectionReason = RiskRejectionReasons.InvalidParameters;
            return false;
        }

        if (string.IsNullOrWhiteSpace(BotPositionLabel))
        {
            rejectionReason = RiskRejectionReasons.InvalidParameters;
            return false;
        }

        rejectionReason = "";
        return true;
    }

    private static bool IsFinitePositive(double v) =>
        double.IsFinite(v) && v > 0;

    private static bool IsFiniteNonNegative(double v) =>
        double.IsFinite(v) && v >= 0;
}
