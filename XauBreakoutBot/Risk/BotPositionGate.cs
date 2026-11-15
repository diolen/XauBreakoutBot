using System;
using System.Collections.Generic;

namespace cAlgo.Robots.Risk;

/// <summary>
/// Elegibilidad por <see cref="RiskParameters.MaxOpenPositions"/> contando solo
/// posiciones del propio bot (misma Label). No bloquea manuales ni otros robots.
/// En esta fase solo produce un resultado de elegibilidad; no gestiona posiciones.
/// </summary>
public static class BotPositionGate
{
    public sealed class OpenPositionRef
    {
        public OpenPositionRef(string? label, string? symbolName)
        {
            Label = label;
            SymbolName = symbolName;
        }

        public string? Label { get; }
        public string? SymbolName { get; }
    }

    public sealed class EligibilityResult
    {
        public bool IsEligible { get; init; }
        public int BotOpenPositionCount { get; init; }
        public int MaxOpenPositions { get; init; }
        public string Reason { get; init; } = "";
    }

    public static EligibilityResult Evaluate(
        IEnumerable<OpenPositionRef> openPositions,
        string botPositionLabel,
        string? symbolName,
        int maxOpenPositions)
    {
        if (maxOpenPositions < 1)
        {
            return new EligibilityResult
            {
                IsEligible = false,
                BotOpenPositionCount = 0,
                MaxOpenPositions = maxOpenPositions,
                Reason = RiskRejectionReasons.InvalidParameters
            };
        }

        if (string.IsNullOrWhiteSpace(botPositionLabel))
        {
            return new EligibilityResult
            {
                IsEligible = false,
                BotOpenPositionCount = 0,
                MaxOpenPositions = maxOpenPositions,
                Reason = RiskRejectionReasons.InvalidParameters
            };
        }

        var count = 0;
        foreach (var p in openPositions)
        {
            if (!string.Equals(p.Label, botPositionLabel, StringComparison.Ordinal))
                continue;

            if (!string.IsNullOrEmpty(symbolName) &&
                !string.IsNullOrEmpty(p.SymbolName) &&
                !string.Equals(p.SymbolName, symbolName, StringComparison.OrdinalIgnoreCase))
                continue;

            count++;
        }

        if (count >= maxOpenPositions)
        {
            return new EligibilityResult
            {
                IsEligible = false,
                BotOpenPositionCount = count,
                MaxOpenPositions = maxOpenPositions,
                Reason = RiskRejectionReasons.MaxOpenPositionsReached
            };
        }

        return new EligibilityResult
        {
            IsEligible = true,
            BotOpenPositionCount = count,
            MaxOpenPositions = maxOpenPositions,
            Reason = ""
        };
    }
}
