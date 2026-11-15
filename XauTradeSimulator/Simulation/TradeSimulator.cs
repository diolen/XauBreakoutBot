using cAlgo.Robots.Risk;
using cAlgo.Robots.SupportResistance;

namespace XauTradeSimulator.Simulation;

/// <summary>
/// Simulador histórico end-to-end. No envía órdenes reales ni DEMO.
/// Entrada solo en apertura de N+1 tras señal en cierre de N.
/// Máximo una posición abierta; equity se actualiza tras cada cierre.
/// </summary>
public static class TradeSimulator
{
    private sealed class OpenPosition
    {
        public required string SignalId { get; init; }
        public required long LevelId { get; init; }
        public required SignalDirection Direction { get; init; }
        public required int SignalBarIndex { get; init; }
        public required int EntryBarIndex { get; init; }
        public required DateTime EntryTime { get; init; }
        public required double EntryPrice { get; init; }
        public required double StopLoss { get; init; }
        public required double TakeProfit { get; init; }
        public required double VolumeInUnits { get; init; }
        public required double RiskBudget { get; init; }
        public required double EquityAtEntry { get; init; }
        public required double R { get; init; }
    }

    public static SimulationResult Run(
        IReadOnlyList<OhlcBar> bars,
        IReadOnlyList<T3BreakoutSignal> signals,
        SimulatorConfig config)
    {
        var symbol = config.CreateSymbolApi();
        var risk = config.Risk;
        var spread = config.SpreadPrice;

        var trades = new List<SimulatedTrade>();
        var rejected = new List<RejectedSignal>();
        var equityCurve = new List<EquityPoint>();

        var equity = config.InitialEquity;
        var peakEquity = equity;
        OpenPosition? open = null;

        // Señales indexadas por barra de confirmación (BreakoutBarIndex).
        var byBar = signals
            .GroupBy(s => s.BreakoutBarIndex)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.LevelId).ToList());

        // Pending entries: señal confirmada en N → intentar abrir en open de N+1.
        var pending = new List<T3BreakoutSignal>();

        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            var quoteBar = BidAskQuote.FromBar(bar, spread);

            // 1) Resolver entradas pendientes al open de esta vela (N+1).
            if (pending.Count > 0)
            {
                foreach (var signal in pending)
                    TryOpen(signal, bar, quoteBar, ref open, ref equity, trades, rejected, symbol, risk, config);

                pending.Clear();
            }

            // 2) Evaluar salida de la posición abierta (incluye vela de entrada).
            if (open != null)
            {
                var decision = PositionExitEvaluator.EvaluateBar(
                    open.Direction, open.StopLoss, open.TakeProfit, quoteBar);

                if (decision.Exited)
                {
                    ClosePosition(
                        open, bar, decision.ExitPrice, decision.ExitReason,
                        decision.AmbiguousSameBar, decision.GapExit,
                        ref equity, ref peakEquity, trades, equityCurve, symbol, config);
                    open = null;
                }
            }

            // 3) Señales confirmadas al cierre de esta vela → pendientes para N+1.
            if (byBar.TryGetValue(i, out var atBar))
            {
                foreach (var signal in atBar)
                {
                    if (i + 1 >= bars.Count)
                    {
                        rejected.Add(Reject(
                            signal, RejectionReasons.MissingNextBar, config.ScenarioName,
                            equityAtAttempt: equity));
                        continue;
                    }

                    pending.Add(signal);
                }
            }
        }

        // Fin de datos con posición abierta.
        if (open != null)
        {
            var last = bars[^1];
            var lastQuote = BidAskQuote.FromBar(last, spread);
            var exitPrice = open.Direction == SignalDirection.Buy
                ? lastQuote.Close.Bid
                : lastQuote.Close.Ask;

            ClosePosition(
                open, last, exitPrice, ExitReasons.EndOfData,
                ambiguous: false, gapExit: false,
                ref equity, ref peakEquity, trades, equityCurve, symbol, config);
        }

        var metrics = MetricsCalculator.Compute(
            config.ScenarioName,
            signals.Count,
            trades,
            rejected,
            equityCurve,
            config.InitialEquity,
            equity);

        return new SimulationResult
        {
            ScenarioName = config.ScenarioName,
            Signals = signals,
            Trades = trades,
            Rejected = rejected,
            EquityCurve = equityCurve,
            Metrics = metrics,
            InitialEquity = config.InitialEquity,
            SpreadPrice = config.SpreadPrice,
            CommissionModeled = config.CommissionModeled,
            SlippageModeled = config.SlippageModeled
        };
    }

    private static void TryOpen(
        T3BreakoutSignal signal,
        OhlcBar entryBar,
        BidAskBar quoteBar,
        ref OpenPosition? open,
        ref double equity,
        List<SimulatedTrade> trades,
        List<RejectedSignal> rejected,
        ISymbolRiskApi symbol,
        RiskParameters risk,
        SimulatorConfig config)
    {
        var signalId = RiskPlanCalculator.BuildSignalId(signal);

        if (open != null)
        {
            rejected.Add(Reject(
                signal, RejectionReasons.PositionAlreadyOpen, config.ScenarioName,
                equityAtAttempt: equity));
            return;
        }

        var entryPrice = signal.Direction == SignalDirection.Buy
            ? quoteBar.Open.Ask
            : quoteBar.Open.Bid;

        var sized = TradeSizer.Size(signal, entryPrice, equity, risk, symbol);
        if (!sized.IsValid || sized.VolumeInUnits is not { } volume)
        {
            rejected.Add(Reject(
                signal,
                sized.RejectionReason ?? RejectionReasons.InvalidSymbolData,
                config.ScenarioName,
                entryPrice,
                sized.StructuralStop,
                sized.TakeProfit,
                sized.RiskBudget,
                equity));
            return;
        }

        open = new OpenPosition
        {
            SignalId = signalId,
            LevelId = signal.LevelId,
            Direction = signal.Direction,
            SignalBarIndex = signal.BreakoutBarIndex,
            EntryBarIndex = entryBar.Index,
            EntryTime = entryBar.OpenTime,
            EntryPrice = entryPrice,
            StopLoss = sized.StructuralStop,
            TakeProfit = sized.TakeProfit,
            VolumeInUnits = volume,
            RiskBudget = sized.RiskBudget,
            EquityAtEntry = equity,
            R = sized.R
        };

        // Silenciar unused (trades se llenan al cerrar).
        _ = trades;
    }

    private static void ClosePosition(
        OpenPosition open,
        OhlcBar exitBar,
        double exitPrice,
        string exitReason,
        bool ambiguous,
        bool gapExit,
        ref double equity,
        ref double peakEquity,
        List<SimulatedTrade> trades,
        List<EquityPoint> equityCurve,
        ISymbolRiskApi symbol,
        SimulatorConfig config)
    {
        var gross = TradeSizer.PricePnL(
            open.Direction, open.EntryPrice, exitPrice, open.VolumeInUnits, symbol);

        // Comisión: MVP no modelada (0). Configurable; no se inventa doble coste de spread.
        var commission = config.CommissionModeled ? config.CommissionRoundTrip : 0.0;
        var net = gross - commission;

        equity += net;
        if (equity > peakEquity)
            peakEquity = equity;

        var ddUsd = peakEquity - equity;
        var ddPct = peakEquity > 0 ? ddUsd / peakEquity * 100.0 : 0;

        var realizedR = open.R > 0
            ? (open.Direction == SignalDirection.Buy
                ? (exitPrice - open.EntryPrice) / open.R
                : (open.EntryPrice - exitPrice) / open.R)
            : 0;

        var holding = Math.Max(0, exitBar.Index - open.EntryBarIndex + 1);

        trades.Add(new SimulatedTrade
        {
            SignalId = open.SignalId,
            LevelId = open.LevelId,
            Direction = open.Direction,
            SignalBarIndex = open.SignalBarIndex,
            EntryBarIndex = open.EntryBarIndex,
            EntryTime = open.EntryTime,
            EntryPrice = open.EntryPrice,
            StopLoss = open.StopLoss,
            TakeProfit = open.TakeProfit,
            VolumeInUnits = open.VolumeInUnits,
            RiskBudget = open.RiskBudget,
            EquityAtEntry = open.EquityAtEntry,
            ExitBarIndex = exitBar.Index,
            ExitTime = exitBar.OpenTime,
            ExitPrice = exitPrice,
            ExitReason = exitReason,
            GrossPnL = gross,
            NetPnL = net,
            RealizedR = realizedR,
            AmbiguousSameBar = ambiguous,
            GapExit = gapExit,
            HoldingBars = holding,
            ScenarioName = config.ScenarioName
        });

        equityCurve.Add(new EquityPoint
        {
            TradeSequence = trades.Count,
            SignalId = open.SignalId,
            ExitBarIndex = exitBar.Index,
            ExitTime = exitBar.OpenTime,
            Equity = equity,
            DrawdownUsd = ddUsd,
            DrawdownPct = ddPct,
            ScenarioName = config.ScenarioName
        });
    }

    private static RejectedSignal Reject(
        T3BreakoutSignal signal,
        string reason,
        string scenario,
        double? entry = null,
        double? sl = null,
        double? tp = null,
        double? budget = null,
        double? equityAtAttempt = null) =>
        new()
        {
            SignalId = RiskPlanCalculator.BuildSignalId(signal),
            LevelId = signal.LevelId,
            Direction = signal.Direction,
            SignalBarIndex = signal.BreakoutBarIndex,
            Reason = reason,
            ScenarioName = scenario,
            EntryPrice = entry,
            StopLoss = sl,
            TakeProfit = tp,
            RiskBudget = budget,
            EquityAtAttempt = equityAtAttempt
        };
}
