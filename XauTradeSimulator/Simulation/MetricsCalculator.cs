namespace XauTradeSimulator.Simulation;

public static class MetricsCalculator
{
    public static SimulationMetrics Compute(
        string scenarioName,
        int totalSignals,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<RejectedSignal> rejected,
        IReadOnlyList<EquityPoint> equityCurve,
        double initialEquity,
        double endingEquity)
    {
        var wins = trades.Count(t => t.NetPnL > 0);
        var losses = trades.Count(t => t.NetPnL < 0);
        var closed = trades.Count;

        var grossProfit = trades.Where(t => t.NetPnL > 0).Sum(t => t.NetPnL);
        var grossLossAbs = Math.Abs(trades.Where(t => t.NetPnL < 0).Sum(t => t.NetPnL));
        var netPnL = trades.Sum(t => t.NetPnL);

        var profitFactor = grossLossAbs > 1e-12
            ? grossProfit / grossLossAbs
            : (grossProfit > 0 ? double.PositiveInfinity : 0);

        var expectancy = closed > 0 ? netPnL / closed : 0;
        var averageR = closed > 0 ? trades.Average(t => t.RealizedR) : 0;
        var avgHold = closed > 0 ? trades.Average(t => (double)t.HoldingBars) : 0;
        var winRate = closed > 0 ? (double)wins / closed * 100.0 : 0;

        var maxDdUsd = equityCurve.Count > 0 ? equityCurve.Max(e => e.DrawdownUsd) : 0;
        var maxDdPct = equityCurve.Count > 0 ? equityCurve.Max(e => e.DrawdownPct) : 0;

        // Drawdown también desde equity inicial si la curva empieza debajo.
        if (equityCurve.Count == 0 && endingEquity < initialEquity)
        {
            maxDdUsd = initialEquity - endingEquity;
            maxDdPct = initialEquity > 0 ? maxDdUsd / initialEquity * 100.0 : 0;
        }

        var rejectionCounts = rejected
            .GroupBy(r => r.Reason)
            .ToDictionary(g => g.Key, g => g.Count());

        var exitCounts = trades
            .GroupBy(t => t.ExitReason)
            .ToDictionary(g => g.Key, g => g.Count());

        return new SimulationMetrics
        {
            ScenarioName = scenarioName,
            TotalSignals = totalSignals,
            TradesOpened = closed,
            TradesRejected = rejected.Count,
            ClosedTrades = closed,
            Wins = wins,
            Losses = losses,
            WinRate = winRate,
            GrossProfit = grossProfit,
            GrossLoss = -grossLossAbs,
            NetPnL = netPnL,
            ProfitFactor = profitFactor,
            ExpectancyPerTrade = expectancy,
            AverageR = averageR,
            MaxDrawdownUsd = maxDdUsd,
            MaxDrawdownPct = maxDdPct,
            EndingEquity = endingEquity,
            AverageHoldingBars = avgHold,
            AmbiguousSameBarCount = trades.Count(t => t.AmbiguousSameBar),
            GapExitCount = trades.Count(t => t.GapExit),
            RejectionReasonCounts = rejectionCounts,
            ExitReasonCounts = exitCounts
        };
    }
}
