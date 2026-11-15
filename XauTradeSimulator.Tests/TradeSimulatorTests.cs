using System.Reflection;
using cAlgo.Robots.Risk;
using cAlgo.Robots.SupportResistance;
using XauTradeSimulator.Simulation;
using Xunit;

namespace XauTradeSimulator.Tests;

public class TradeSimulatorTests
{
    private static readonly DateTime T0 = new(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private static OhlcBar Bar(int index, double o, double h, double l, double c, int minutesOffset = -1) =>
        new(index, T0.AddMinutes(minutesOffset >= 0 ? minutesOffset : index * 15), o, h, l, c);

    private static T3BreakoutSignal BuySignal(
        int breakoutBar = 0,
        long levelId = 1,
        double close = 2650,
        double upper = 2648,
        double lower = 2640,
        double atr = 10) =>
        new()
        {
            LevelId = levelId,
            LevelRole = LevelRole.Resistance,
            Direction = SignalDirection.Buy,
            T3BarIndex = Math.Max(0, breakoutBar - 3),
            BreakoutBarIndex = breakoutBar,
            BreakoutTime = T0.AddMinutes(breakoutBar * 15),
            ConfirmationClose = close,
            LevelMidAtBreakout = (upper + lower) / 2,
            UpperBoundAtBreakout = upper,
            LowerBoundAtBreakout = lower,
            HalfWidthAtBreakout = (upper - lower) / 2,
            AtrAtBreakout = atr,
            BreakoutReason = "test-buy",
            Geometry = LevelGeometry.Horizontal
        };

    private static T3BreakoutSignal SellSignal(
        int breakoutBar = 0,
        long levelId = 2,
        double close = 2630,
        double upper = 2640,
        double lower = 2632,
        double atr = 10) =>
        new()
        {
            LevelId = levelId,
            LevelRole = LevelRole.Support,
            Direction = SignalDirection.Sell,
            T3BarIndex = Math.Max(0, breakoutBar - 3),
            BreakoutBarIndex = breakoutBar,
            BreakoutTime = T0.AddMinutes(breakoutBar * 15),
            ConfirmationClose = close,
            LevelMidAtBreakout = (upper + lower) / 2,
            UpperBoundAtBreakout = upper,
            LowerBoundAtBreakout = lower,
            HalfWidthAtBreakout = (upper - lower) / 2,
            AtrAtBreakout = atr,
            BreakoutReason = "test-sell",
            Geometry = LevelGeometry.Horizontal
        };

    private static SimulatorConfig Frictionless(double equity = 10_000) =>
        SimulatorConfig.Frictionless() with { InitialEquity = equity };

    private static SimulatorConfig SpreadCfg(double spread = 0.40, double equity = 10_000) =>
        SimulatorConfig.Spread040() with { InitialEquity = equity, SpreadPrice = spread };

    // -------------------------------------------------------------------------
    // 1. Entrada en N+1
    // -------------------------------------------------------------------------
    [Fact]
    public void Entry_OccursAtOpenOfNextBar_NotAtSignalClose()
    {
        // Signal bar 0 close=2650; bar 1 open=2651 → entry must be 2651, not 2650.
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2651, 2655, 2648, 2652), // open 2651; never hits SL 2639; hits TP later? keep open then TP
            Bar(2, 2652, 2700, 2650, 2690)  // TP = 2651 + 2*(2651-2639) = 2651+24=2675
        };
        var signal = BuySignal(breakoutBar: 0, close: 2650, lower: 2640, atr: 10);
        var result = TradeSimulator.Run(bars, new[] { signal }, Frictionless());

        Assert.Single(result.Trades);
        var t = result.Trades[0];
        Assert.Equal(0, t.SignalBarIndex);
        Assert.Equal(1, t.EntryBarIndex);
        Assert.Equal(2651, t.EntryPrice, 9);
        Assert.NotEqual(2650, t.EntryPrice); // no ejecución al cierre de N
    }

    // -------------------------------------------------------------------------
    // 2. Ausencia de lookahead
    // -------------------------------------------------------------------------
    [Fact]
    public void NoLookahead_EntryIgnoresFutureHighLowOfEntryBar()
    {
        // Even if bar 1 high would have been better / low worse, entry is open only.
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2651, 2800, 2000, 2652), // wild range; entry still open
            Bar(2, 2000, 2001, 1990, 1995)  // gap SL
        };
        var signal = BuySignal(0, close: 2650, lower: 2640, atr: 10);
        var result = TradeSimulator.Run(bars, new[] { signal }, Frictionless());

        Assert.Single(result.Trades);
        Assert.Equal(2651, result.Trades[0].EntryPrice, 9);
        // Exit via gap SL at open of bar2 (2000) or SL on bar1 (low 2000) — either way entry was open.
        Assert.Equal(1, result.Trades[0].EntryBarIndex);
    }

    // -------------------------------------------------------------------------
    // 3. BUY SL/TP
    // -------------------------------------------------------------------------
    [Fact]
    public void Buy_StructuralStopAndTakeProfit_FromEffectiveEntry()
    {
        // SL = 2640 - 1 = 2639; entry open N+1 = 2650; R=11; TP=2672
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2650, 2651, 2649, 2650),
            Bar(2, 2650, 2672, 2649, 2672) // hit TP
        };
        var signal = BuySignal(0, close: 2650, lower: 2640, atr: 10);
        var t = TradeSimulator.Run(bars, new[] { signal }, Frictionless()).Trades.Single();

        Assert.Equal(2639, t.StopLoss, 9);
        Assert.Equal(2672, t.TakeProfit, 9);
        Assert.Equal(ExitReasons.TakeProfit, t.ExitReason);
    }

    // -------------------------------------------------------------------------
    // 4. SELL SL/TP
    // -------------------------------------------------------------------------
    [Fact]
    public void Sell_StructuralStopAndTakeProfit_FromEffectiveEntry()
    {
        // SL = 2640 + 1 = 2641; entry open = 2630; R=11; TP=2608
        var bars = new[]
        {
            Bar(0, 2635, 2636, 2628, 2630),
            Bar(1, 2630, 2631, 2629, 2630),
            Bar(2, 2630, 2631, 2608, 2608)
        };
        var signal = SellSignal(0, close: 2630, upper: 2640, atr: 10);
        var t = TradeSimulator.Run(bars, new[] { signal }, Frictionless()).Trades.Single();

        Assert.Equal(2641, t.StopLoss, 9);
        Assert.Equal(2608, t.TakeProfit, 9);
        Assert.Equal(ExitReasons.TakeProfit, t.ExitReason);
    }

    // -------------------------------------------------------------------------
    // 5. TP = 2R (not from ConfirmationClose)
    // -------------------------------------------------------------------------
    [Fact]
    public void TakeProfit_IsTwoR_FromSimulatedEntry_NotConfirmationClose()
    {
        // ConfirmationClose=2650 but entry open=2655 → TP from 2655, not 2650.
        // SL=2639; R=16; TP=2655+32=2687
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2655, 2656, 2654, 2655),
            Bar(2, 2655, 2687, 2654, 2687)
        };
        var signal = BuySignal(0, close: 2650, lower: 2640, atr: 10);
        var t = TradeSimulator.Run(bars, new[] { signal }, Frictionless()).Trades.Single();

        Assert.Equal(2655, t.EntryPrice, 9);
        Assert.Equal(2639, t.StopLoss, 9);
        var r = Math.Abs(t.EntryPrice - t.StopLoss);
        Assert.Equal(t.EntryPrice + 2 * r, t.TakeProfit, 9);
        Assert.NotEqual(2650 + 2 * Math.Abs(2650 - 2639), t.TakeProfit);
    }

    // -------------------------------------------------------------------------
    // 6. Riesgo 0.5%
    // -------------------------------------------------------------------------
    [Fact]
    public void RiskBudget_IsHalfPercentOfCurrentEquity()
    {
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2650, 2651, 2649, 2650),
            Bar(2, 2650, 2672, 2649, 2672)
        };
        var signal = BuySignal(0);
        var t = TradeSimulator.Run(bars, new[] { signal }, Frictionless(20_000)).Trades.Single();
        Assert.Equal(100.0, t.RiskBudget, 9); // 20000 * 0.5%
        Assert.Equal(20_000, t.EquityAtEntry, 9);
    }

    // -------------------------------------------------------------------------
    // 7. Normalización de volumen (floor)
    // -------------------------------------------------------------------------
    [Fact]
    public void Volume_NormalizesDown_NeverUp()
    {
        // R=11, stopPips=1100, budget=50 → raw=4.545 → floor 4
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2650, 2651, 2649, 2650),
            Bar(2, 2650, 2672, 2649, 2672)
        };
        var t = TradeSimulator.Run(bars, new[] { BuySignal(0) }, Frictionless()).Trades.Single();
        Assert.Equal(4, t.VolumeInUnits);
    }

    // -------------------------------------------------------------------------
    // 8. Volumen mínimo rechazado
    // -------------------------------------------------------------------------
    [Fact]
    public void MinVolumeExceedsRiskBudget_RejectsEntry()
    {
        var cfg = Frictionless() with
        {
            VolumeInUnitsMin = 10,
            VolumeInUnitsStep = 10,
            VolumeInUnitsMax = 100
        };
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2650, 2651, 2649, 2650),
            Bar(2, 2650, 2672, 2649, 2672)
        };
        var result = TradeSimulator.Run(bars, new[] { BuySignal(0) }, cfg);
        Assert.Empty(result.Trades);
        Assert.Contains(result.Rejected, r => r.Reason == RejectionReasons.MinVolumeExceedsRiskBudget);
    }

    // -------------------------------------------------------------------------
    // 9. Posición simultánea rechazada
    // -------------------------------------------------------------------------
    [Fact]
    public void SecondSignal_WhilePositionOpen_IsRejected()
    {
        // Signal0 → enter bar1; still open on bar2 when signal1 would enter on bar3.
        // Keep position alive until bar4 TP.
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650), // signal A
            Bar(1, 2650, 2651, 2649, 2650), // enter A
            Bar(2, 2650, 2651, 2649, 2650), // signal B (position open)
            Bar(3, 2650, 2651, 2649, 2650), // would-be entry B → rejected
            Bar(4, 2650, 2672, 2649, 2672)  // TP A
        };
        var signals = new[]
        {
            BuySignal(0, levelId: 10),
            BuySignal(2, levelId: 20)
        };
        var result = TradeSimulator.Run(bars, signals, Frictionless());

        Assert.Single(result.Trades);
        Assert.Equal(10, result.Trades[0].LevelId);
        Assert.Contains(result.Rejected, r =>
            r.LevelId == 20 && r.Reason == RejectionReasons.PositionAlreadyOpen);
    }

    // -------------------------------------------------------------------------
    // 10. Spread aplicado una sola vez
    // -------------------------------------------------------------------------
    [Fact]
    public void Spread_AppliedOnce_NotDoubleCountedInPnL()
    {
        // Mid open entry=2650, spread=0.40 → BUY Ask=2650.20
        // Exit TP mid such that Bid hits TP; PnL from Ask entry to Bid exit only.
        var spread = 0.40;
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2650, 2650.1, 2649.9, 2650),
            // Need Bid high >= TP. Entry Ask=2650.2; SL=2639; R=11.2; TP=2672.6
            // Bid = mid - 0.2 → mid high must be >= 2672.8
            Bar(2, 2650, 2672.8, 2649, 2672.8)
        };
        var t = TradeSimulator.Run(bars, new[] { BuySignal(0) }, SpreadCfg(spread)).Trades.Single();

        Assert.Equal(2650.20, t.EntryPrice, 6);
        Assert.Equal(ExitReasons.TakeProfit, t.ExitReason);
        Assert.Equal(t.TakeProfit, t.ExitPrice, 6);

        // Gross = volume * (exit-entry)/pipSize * pipValue — sin restar spread otra vez.
        var symbol = SpreadCfg(spread).CreateSymbolApi();
        var expected = TradeSizer.PricePnL(SignalDirection.Buy, t.EntryPrice, t.ExitPrice, t.VolumeInUnits, symbol);
        Assert.Equal(expected, t.GrossPnL, 9);
        Assert.Equal(t.GrossPnL, t.NetPnL, 9); // commission not modeled
    }

    // -------------------------------------------------------------------------
    // 11. BUY/SELL con Bid/Ask sintéticos
    // -------------------------------------------------------------------------
    [Fact]
    public void BuyUsesAskEntry_SellUsesBidEntry()
    {
        var spread = 0.40;
        var buyBars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2650, 2650.1, 2649.9, 2650),
            Bar(2, 2650, 2680, 2649, 2680)
        };
        var buy = TradeSimulator.Run(buyBars, new[] { BuySignal(0) }, SpreadCfg(spread)).Trades.Single();
        Assert.Equal(2650 + spread / 2, buy.EntryPrice, 9);

        var sellBars = new[]
        {
            Bar(0, 2635, 2636, 2628, 2630),
            Bar(1, 2630, 2630.1, 2629.9, 2630),
            Bar(2, 2630, 2631, 2600, 2600)
        };
        var sell = TradeSimulator.Run(sellBars, new[] { SellSignal(0) }, SpreadCfg(spread)).Trades.Single();
        Assert.Equal(2630 - spread / 2, sell.EntryPrice, 9);
    }

    // -------------------------------------------------------------------------
    // 12. SL/TP en la misma vela
    // -------------------------------------------------------------------------
    [Fact]
    public void SameBar_SlAndTp_AssumesStopFirst_FlagsAmbiguous()
    {
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2650, 2700, 2600, 2650) // both SL and TP reachable
        };
        var t = TradeSimulator.Run(bars, new[] { BuySignal(0) }, Frictionless()).Trades.Single();

        Assert.Equal(ExitReasons.StopLoss, t.ExitReason);
        Assert.True(t.AmbiguousSameBar);
        Assert.Equal(t.StopLoss, t.ExitPrice, 9);
        Assert.False(t.GapExit);
    }

    // -------------------------------------------------------------------------
    // 13. Gap más allá del SL
    // -------------------------------------------------------------------------
    [Fact]
    public void GapBeyondStop_ExitsAtOpen_NotIdealStop()
    {
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2650, 2651, 2649, 2650),
            Bar(2, 2600, 2601, 2590, 2595) // open gap below SL 2639
        };
        var t = TradeSimulator.Run(bars, new[] { BuySignal(0) }, Frictionless()).Trades.Single();

        Assert.True(t.GapExit);
        Assert.Equal(ExitReasons.StopLoss, t.ExitReason);
        Assert.Equal(2600, t.ExitPrice, 9);
        Assert.NotEqual(t.StopLoss, t.ExitPrice);
    }

    // -------------------------------------------------------------------------
    // 14. Gap más allá del TP
    // -------------------------------------------------------------------------
    [Fact]
    public void GapBeyondTakeProfit_ExitsAtOpen_NotIdealTp()
    {
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2650, 2651, 2649, 2650),
            Bar(2, 2700, 2701, 2695, 2698) // open gap above TP 2672
        };
        var t = TradeSimulator.Run(bars, new[] { BuySignal(0) }, Frictionless()).Trades.Single();

        Assert.True(t.GapExit);
        Assert.Equal(ExitReasons.TakeProfit, t.ExitReason);
        Assert.Equal(2700, t.ExitPrice, 9);
        Assert.NotEqual(t.TakeProfit, t.ExitPrice);
    }

    // -------------------------------------------------------------------------
    // 15. Fin de datos
    // -------------------------------------------------------------------------
    [Fact]
    public void EndOfData_ClosesOpenPosition_AtLastAvailablePrice()
    {
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2650, 2651, 2649, 2650),
            Bar(2, 2650, 2651, 2649, 2655) // no SL/TP
        };
        var t = TradeSimulator.Run(bars, new[] { BuySignal(0) }, Frictionless()).Trades.Single();

        Assert.Equal(ExitReasons.EndOfData, t.ExitReason);
        Assert.Equal(2, t.ExitBarIndex);
        Assert.Equal(2655, t.ExitPrice, 9);
        Assert.False(t.GapExit);
    }

    // -------------------------------------------------------------------------
    // 16. Equity actualizado tras cada cierre
    // -------------------------------------------------------------------------
    [Fact]
    public void Equity_UpdatesAfterEachClose()
    {
        // Trade1: TP then Trade2 uses updated equity for risk budget.
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650), // S1
            Bar(1, 2650, 2651, 2649, 2650), // enter S1
            Bar(2, 2650, 2672, 2649, 2672), // TP S1
            Bar(3, 2645, 2652, 2644, 2650), // S2
            Bar(4, 2650, 2651, 2649, 2650), // enter S2
            Bar(5, 2650, 2672, 2649, 2672)  // TP S2
        };
        var signals = new[]
        {
            BuySignal(0, levelId: 1),
            BuySignal(3, levelId: 2)
        };
        var result = TradeSimulator.Run(bars, signals, Frictionless());

        Assert.Equal(2, result.Trades.Count);
        Assert.Equal(10_000, result.Trades[0].EquityAtEntry, 6);
        Assert.True(result.Trades[1].EquityAtEntry > 10_000);
        Assert.Equal(result.EquityCurve[0].Equity, result.Trades[1].EquityAtEntry, 6);
        Assert.Equal(result.Metrics.EndingEquity, result.EquityCurve[^1].Equity, 6);
    }

    // -------------------------------------------------------------------------
    // 17. Drawdown
    // -------------------------------------------------------------------------
    [Fact]
    public void Drawdown_TracksPeakToTrough()
    {
        // First trade wins, second loses via SL → drawdown from peak.
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2650, 2651, 2649, 2650),
            Bar(2, 2650, 2672, 2649, 2672), // TP
            Bar(3, 2645, 2652, 2644, 2650),
            Bar(4, 2650, 2651, 2649, 2650),
            Bar(5, 2650, 2651, 2639, 2640)  // SL
        };
        var signals = new[]
        {
            BuySignal(0, levelId: 1),
            BuySignal(3, levelId: 2)
        };
        var result = TradeSimulator.Run(bars, signals, Frictionless());

        Assert.Equal(2, result.Trades.Count);
        Assert.True(result.Metrics.MaxDrawdownUsd > 0);
        Assert.True(result.Metrics.MaxDrawdownPct > 0);
        Assert.True(result.EquityCurve[^1].DrawdownUsd > 0);
    }

    // -------------------------------------------------------------------------
    // 18. Determinismo del replay
    // -------------------------------------------------------------------------
    [Fact]
    public void Simulation_IsDeterministic()
    {
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650),
            Bar(1, 2650, 2651, 2649, 2650),
            Bar(2, 2650, 2672, 2649, 2672),
            Bar(3, 2645, 2652, 2644, 2650),
            Bar(4, 2650, 2651, 2639, 2640)
        };
        var signals = new[]
        {
            BuySignal(0, levelId: 1),
            BuySignal(3, levelId: 2)
        };

        var a = TradeSimulator.Run(bars, signals, Frictionless());
        var b = TradeSimulator.Run(bars, signals, Frictionless());

        Assert.Equal(a.Trades.Count, b.Trades.Count);
        for (var i = 0; i < a.Trades.Count; i++)
        {
            Assert.Equal(a.Trades[i].EntryPrice, b.Trades[i].EntryPrice);
            Assert.Equal(a.Trades[i].ExitPrice, b.Trades[i].ExitPrice);
            Assert.Equal(a.Trades[i].NetPnL, b.Trades[i].NetPnL);
            Assert.Equal(a.Trades[i].ExitReason, b.Trades[i].ExitReason);
        }

        Assert.Equal(a.Metrics.NetPnL, b.Metrics.NetPnL);
        Assert.Equal(a.Metrics.EndingEquity, b.Metrics.EndingEquity);
    }

    // -------------------------------------------------------------------------
    // 19. No ejecución de órdenes
    // -------------------------------------------------------------------------
    [Fact]
    public void Assembly_HasNoOrderExecutionApis()
    {
        var asm = typeof(TradeSimulator).Assembly;
        foreach (var type in asm.GetTypes())
        {
            foreach (var method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain("ExecuteMarketOrder", method.Name, StringComparison.Ordinal);
                Assert.DoesNotContain("PlaceLimitOrder", method.Name, StringComparison.Ordinal);
                Assert.DoesNotContain("PlaceStopOrder", method.Name, StringComparison.Ordinal);
                Assert.DoesNotContain("PlaceMarketOrder", method.Name, StringComparison.Ordinal);
            }
        }
    }

    // -------------------------------------------------------------------------
    // 20. Consistencia con 639 señales (+ MissingNextBar)
    // -------------------------------------------------------------------------
    [Fact]
    public void MissingNextBar_RejectsWhenNoBarAfterSignal()
    {
        var bars = new[]
        {
            Bar(0, 2645, 2652, 2644, 2650)
        };
        var result = TradeSimulator.Run(bars, new[] { BuySignal(0) }, Frictionless());
        Assert.Empty(result.Trades);
        Assert.Contains(result.Rejected, r => r.Reason == RejectionReasons.MissingNextBar);
    }

    [Fact]
    public void HistoricalSignalCount_MatchesExpected639_WhenCsvPresent()
    {
        var csv = SimulatorConfig.DefaultCsvPath;
        if (!File.Exists(csv))
        {
            // Entorno sin dataset: no fallar CI local vacío; el harness full lo valida.
            return;
        }

        var bars = XauSwingAbHarness.Dataset.CsvHistoryLoader.Load(csv);
        var dataset = XauSwingAbHarness.Dataset.DatasetValidator.Validate(csv, bars);
        Assert.Equal(SimulatorConfig.ExpectedSha256, dataset.Sha256Hex, ignoreCase: true);
        Assert.True(dataset.IsStructurallyValid);

        var replay1 = SignalReplay.Run(bars);
        var replay2 = SignalReplay.Run(bars);

        Assert.Equal(SimulatorConfig.ExpectedSignalCount, replay1.Signals.Count);
        Assert.Equal(replay1.Signals.Count, replay2.Signals.Count);
        for (var i = 0; i < replay1.Signals.Count; i++)
        {
            Assert.Equal(replay1.Signals[i].LevelId, replay2.Signals[i].LevelId);
            Assert.Equal(replay1.Signals[i].BreakoutBarIndex, replay2.Signals[i].BreakoutBarIndex);
            Assert.Equal(replay1.Signals[i].Direction, replay2.Signals[i].Direction);
            Assert.Equal(replay1.Signals[i].ConfirmationClose, replay2.Signals[i].ConfirmationClose);
        }
    }

    // Extra: StopLossWrongSide when spread pushes entry past SL
    [Fact]
    public void Spread_CanReject_WhenEntryOnWrongSideOfStructuralStop()
    {
        // Extremely tight geometry: close just above lower bound; large spread flips Ask vs SL.
        // Lower=2649.5, ATR=1 → SL=2649.4; mid open=2649.45 → Ask=2649.65 OK normally.
        // Use mid open below SL after half-spread: mid=2649.3, spread=0.40 → Ask=2649.5, SL=2649.4 still OK.
        // Force: lower=2650, atr=0 → wait atr must be >0. lower=2649.95, atr=0.1 → SL=2649.94
        // mid open entry=2649.5, spread=1.0 → Ask=2650.0 > SL. Need Ask < SL for BUY reject:
        // mid=2649.0, spread=0.4 → Ask=2649.2; SL = 2649.95 - 0.01 = 2649.94 → Ask < SL → reject.
        var signal = BuySignal(0, close: 2650, upper: 2650.05, lower: 2649.95, atr: 0.1);
        var bars = new[]
        {
            Bar(0, 2649, 2651, 2648, 2650),
            Bar(1, 2649.0, 2649.1, 2648.9, 2649.0),
            Bar(2, 2649, 2660, 2648, 2655)
        };
        var result = TradeSimulator.Run(bars, new[] { signal }, SpreadCfg(0.40));
        Assert.Empty(result.Trades);
        Assert.Contains(result.Rejected, r => r.Reason == RejectionReasons.StopLossWrongSide);
    }
}
