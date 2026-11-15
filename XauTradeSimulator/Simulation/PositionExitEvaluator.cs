using cAlgo.Robots.SupportResistance;

namespace XauTradeSimulator.Simulation;

/// <summary>
/// Evaluación causal de SL/TP sobre velas OHLC con Bid/Ask sintéticos.
/// Política documentada:
/// 1) Gap en apertura (open ya más allá de SL o TP) → salir al open simulado (GapExit).
/// 2) Si High/Low tocan SL y TP en la misma vela → SL primero + AmbiguousSameBar.
/// 3) Si solo SL o solo TP → ese motivo.
/// 4) No se asume fill perfecto a través de gaps (nunca forzar precio ideal SL/TP tras gap).
/// </summary>
public static class PositionExitEvaluator
{
    public sealed class ExitDecision
    {
        public required bool Exited { get; init; }
        public required double ExitPrice { get; init; }
        public required string ExitReason { get; init; }
        public required bool AmbiguousSameBar { get; init; }
        public required bool GapExit { get; init; }
    }

    public static ExitDecision EvaluateBar(
        SignalDirection direction,
        double stopLoss,
        double takeProfit,
        BidAskBar bar)
    {
        if (direction == SignalDirection.Buy)
            return EvaluateBuy(stopLoss, takeProfit, bar);
        return EvaluateSell(stopLoss, takeProfit, bar);
    }

    private static ExitDecision EvaluateBuy(double sl, double tp, BidAskBar bar)
    {
        // Salida BUY a Bid.
        var open = bar.Open.Bid;
        var high = bar.High.Bid;
        var low = bar.Low.Bid;

        if (open <= sl)
            return Gap(open, ExitReasons.StopLoss);
        if (open >= tp)
            return Gap(open, ExitReasons.TakeProfit);

        var hitSl = low <= sl;
        var hitTp = high >= tp;

        if (hitSl && hitTp)
            return new ExitDecision
            {
                Exited = true,
                ExitPrice = sl,
                ExitReason = ExitReasons.StopLoss,
                AmbiguousSameBar = true,
                GapExit = false
            };

        if (hitSl)
            return Hit(sl, ExitReasons.StopLoss);

        if (hitTp)
            return Hit(tp, ExitReasons.TakeProfit);

        return Continue();
    }

    private static ExitDecision EvaluateSell(double sl, double tp, BidAskBar bar)
    {
        // Salida SELL a Ask.
        var open = bar.Open.Ask;
        var high = bar.High.Ask;
        var low = bar.Low.Ask;

        if (open >= sl)
            return Gap(open, ExitReasons.StopLoss);
        if (open <= tp)
            return Gap(open, ExitReasons.TakeProfit);

        var hitSl = high >= sl;
        var hitTp = low <= tp;

        if (hitSl && hitTp)
            return new ExitDecision
            {
                Exited = true,
                ExitPrice = sl,
                ExitReason = ExitReasons.StopLoss,
                AmbiguousSameBar = true,
                GapExit = false
            };

        if (hitSl)
            return Hit(sl, ExitReasons.StopLoss);

        if (hitTp)
            return Hit(tp, ExitReasons.TakeProfit);

        return Continue();
    }

    private static ExitDecision Gap(double price, string reason) =>
        new()
        {
            Exited = true,
            ExitPrice = price,
            ExitReason = reason,
            AmbiguousSameBar = false,
            GapExit = true
        };

    private static ExitDecision Hit(double price, string reason) =>
        new()
        {
            Exited = true,
            ExitPrice = price,
            ExitReason = reason,
            AmbiguousSameBar = false,
            GapExit = false
        };

    private static ExitDecision Continue() =>
        new()
        {
            Exited = false,
            ExitPrice = double.NaN,
            ExitReason = "",
            AmbiguousSameBar = false,
            GapExit = false
        };
}
