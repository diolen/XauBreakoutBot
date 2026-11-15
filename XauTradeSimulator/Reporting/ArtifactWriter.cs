using System.Globalization;
using System.Text;
using XauTradeSimulator.Simulation;

namespace XauTradeSimulator.Reporting;

public static class ArtifactWriter
{
    public static void WriteAll(
        string outputDir,
        SimulationResult frictionless,
        SimulationResult spread040,
        string datasetSha,
        int barCount,
        DateTime firstBar,
        DateTime lastBar)
    {
        Directory.CreateDirectory(outputDir);

        WriteTrades(Path.Combine(outputDir, "trades_frictionless.csv"), frictionless.Trades);
        WriteTrades(Path.Combine(outputDir, "trades_spread040.csv"), spread040.Trades);
        WriteRejected(
            Path.Combine(outputDir, "rejected_signals.csv"),
            frictionless.Rejected.Concat(spread040.Rejected)
                .OrderBy(r => r.ScenarioName)
                .ThenBy(r => r.SignalBarIndex)
                .ThenBy(r => r.LevelId));
        WriteEquity(
            Path.Combine(outputDir, "equity_curve.csv"),
            frictionless.EquityCurve.Concat(spread040.EquityCurve)
                .OrderBy(e => e.ScenarioName)
                .ThenBy(e => e.TradeSequence));

        File.WriteAllText(
            Path.Combine(outputDir, "summary.md"),
            BuildSummary(frictionless, spread040, datasetSha, barCount, firstBar, lastBar),
            Encoding.UTF8);

        File.WriteAllText(
            Path.Combine(outputDir, "assumptions.md"),
            BuildAssumptions(frictionless, spread040, datasetSha, barCount),
            Encoding.UTF8);
    }

    public static void WriteTrades(string path, IEnumerable<SimulatedTrade> trades)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "SignalId,LevelId,Direction,SignalBarIndex,EntryBarIndex,EntryTime,EntryPrice,SL,TP," +
            "VolumeInUnits,RiskBudget,EquityAtEntry,ExitBarIndex,ExitTime,ExitPrice,ExitReason," +
            "GrossPnL,NetPnL,RealizedR,AmbiguousSameBar,GapExit,HoldingBars,ScenarioName");

        foreach (var t in trades)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{t.SignalId},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.LevelId},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.Direction},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.SignalBarIndex},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.EntryBarIndex},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.EntryTime:O},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.EntryPrice:R},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.StopLoss:R},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.TakeProfit:R},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.VolumeInUnits:R},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.RiskBudget:R},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.EquityAtEntry:R},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.ExitBarIndex},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.ExitTime:O},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.ExitPrice:R},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.ExitReason},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.GrossPnL:R},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.NetPnL:R},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.RealizedR:R},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.AmbiguousSameBar},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.GapExit},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.HoldingBars},");
            sb.AppendLine(t.ScenarioName);
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static void WriteRejected(string path, IEnumerable<RejectedSignal> rejected)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "SignalId,LevelId,Direction,SignalBarIndex,Reason,ScenarioName," +
            "EntryPrice,StopLoss,TakeProfit,RiskBudget,EquityAtAttempt");

        foreach (var r in rejected)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{r.SignalId},");
            sb.Append(CultureInfo.InvariantCulture, $"{r.LevelId},");
            sb.Append(CultureInfo.InvariantCulture, $"{r.Direction},");
            sb.Append(CultureInfo.InvariantCulture, $"{r.SignalBarIndex},");
            sb.Append(CultureInfo.InvariantCulture, $"{r.Reason},");
            sb.Append(CultureInfo.InvariantCulture, $"{r.ScenarioName},");
            sb.Append(CultureInfo.InvariantCulture, $"{Fmt(r.EntryPrice)},");
            sb.Append(CultureInfo.InvariantCulture, $"{Fmt(r.StopLoss)},");
            sb.Append(CultureInfo.InvariantCulture, $"{Fmt(r.TakeProfit)},");
            sb.Append(CultureInfo.InvariantCulture, $"{Fmt(r.RiskBudget)},");
            sb.AppendLine(Fmt(r.EquityAtAttempt));
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static void WriteEquity(string path, IEnumerable<EquityPoint> points)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TradeSequence,SignalId,ExitBarIndex,ExitTime,Equity,DrawdownUsd,DrawdownPct,ScenarioName");

        foreach (var e in points)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{e.TradeSequence},");
            sb.Append(CultureInfo.InvariantCulture, $"{e.SignalId},");
            sb.Append(CultureInfo.InvariantCulture, $"{e.ExitBarIndex},");
            sb.Append(CultureInfo.InvariantCulture, $"{e.ExitTime:O},");
            sb.Append(CultureInfo.InvariantCulture, $"{e.Equity:R},");
            sb.Append(CultureInfo.InvariantCulture, $"{e.DrawdownUsd:R},");
            sb.Append(CultureInfo.InvariantCulture, $"{e.DrawdownPct:R},");
            sb.AppendLine(e.ScenarioName);
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string Fmt(double? v) =>
        v is { } x && double.IsFinite(x)
            ? x.ToString("R", CultureInfo.InvariantCulture)
            : "";

    private static string BuildSummary(
        SimulationResult a,
        SimulationResult b,
        string sha,
        int bars,
        DateTime first,
        DateTime last)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Trade Simulator MVP — Summary");
        sb.AppendLine();
        sb.AppendLine("| Campo | Valor |");
        sb.AppendLine("|-------|-------|");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Dataset SHA-256 | `{sha}` |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Bars | {bars} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| First | {first:O} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Last | {last:O} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Signals (T3 structural) | {a.Signals.Count} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Expected signals | {SimulatorConfig.ExpectedSignalCount} |");
        sb.AppendLine("| Orders executed | **none** (simulación histórica) |");
        sb.AppendLine();
        sb.AppendLine("## Scenario A — Frictionless");
        sb.AppendLine();
        AppendMetricsTable(sb, a.Metrics);
        sb.AppendLine();
        sb.AppendLine("## Scenario B — Spread USD 0.40");
        sb.AppendLine();
        AppendMetricsTable(sb, b.Metrics);
        sb.AppendLine();
        sb.AppendLine("## Comparación A vs B");
        sb.AppendLine();
        sb.AppendLine("| Métrica | A Frictionless | B Spread 0.40 | Δ (B−A) |");
        sb.AppendLine("|---------|----------------|---------------|---------|");
        CompareRow(sb, "Net PnL", a.Metrics.NetPnL, b.Metrics.NetPnL);
        CompareRow(sb, "Profit Factor", a.Metrics.ProfitFactor, b.Metrics.ProfitFactor);
        CompareRow(sb, "Average R", a.Metrics.AverageR, b.Metrics.AverageR);
        CompareRow(sb, "Max DD USD", a.Metrics.MaxDrawdownUsd, b.Metrics.MaxDrawdownUsd);
        CompareRow(sb, "Max DD %", a.Metrics.MaxDrawdownPct, b.Metrics.MaxDrawdownPct);
        CompareRow(sb, "Ending Equity", a.Metrics.EndingEquity, b.Metrics.EndingEquity);
        CompareRow(sb, "Win Rate %", a.Metrics.WinRate, b.Metrics.WinRate);
        CompareRow(sb, "Trades opened", a.Metrics.TradesOpened, b.Metrics.TradesOpened);
        CompareRow(sb, "Ambiguous same-bar", a.Metrics.AmbiguousSameBarCount, b.Metrics.AmbiguousSameBarCount);
        CompareRow(sb, "Gap exits", a.Metrics.GapExitCount, b.Metrics.GapExitCount);
        sb.AppendLine();
        sb.AppendLine("## Rejection reasons (A)");
        sb.AppendLine();
        AppendCounts(sb, a.Metrics.RejectionReasonCounts);
        sb.AppendLine();
        sb.AppendLine("## Rejection reasons (B)");
        sb.AppendLine();
        AppendCounts(sb, b.Metrics.RejectionReasonCounts);
        sb.AppendLine();
        sb.AppendLine("## Exit reasons (A)");
        sb.AppendLine();
        AppendCounts(sb, a.Metrics.ExitReasonCounts);
        sb.AppendLine();
        sb.AppendLine("## Exit reasons (B)");
        sb.AppendLine();
        AppendCounts(sb, b.Metrics.ExitReasonCounts);
        sb.AppendLine();
        sb.AppendLine("## Notas");
        sb.AppendLine();
        sb.AppendLine("- Scenario B **no** representa costes reales completos (comisión y slippage no modelados).");
        sb.AppendLine("- OHLC tratado como mid/referencia; Bid/Ask sintéticos = mid ± spread/2.");
        sb.AppendLine("- Si SL y TP en la misma vela: se asume SL primero (`AmbiguousSameBar`).");
        sb.AppendLine("- No se calculó Sharpe Ratio en esta fase.");
        return sb.ToString();
    }

    private static void AppendMetricsTable(StringBuilder sb, SimulationMetrics m)
    {
        sb.AppendLine("| Métrica | Valor |");
        sb.AppendLine("|---------|-------|");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Total signals | {m.TotalSignals} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Trades opened | {m.TradesOpened} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Trades rejected | {m.TradesRejected} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Win rate % | {m.WinRate:F2} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Gross profit | {m.GrossProfit:F4} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Gross loss | {m.GrossLoss:F4} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Net PnL | {m.NetPnL:F4} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Profit Factor | {FormatPf(m.ProfitFactor)} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Expectancy / trade | {m.ExpectancyPerTrade:F4} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Average R | {m.AverageR:F4} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Max DD USD | {m.MaxDrawdownUsd:F4} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Max DD % | {m.MaxDrawdownPct:F4} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Ending Equity | {m.EndingEquity:F4} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Avg holding bars | {m.AverageHoldingBars:F2} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Ambiguous same-bar | {m.AmbiguousSameBarCount} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Gap exits | {m.GapExitCount} |");
    }

    private static void CompareRow(StringBuilder sb, string name, double a, double b)
    {
        var d = b - a;
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"| {name} | {FormatPf(a)} | {FormatPf(b)} | {FormatPf(d)} |");
    }

    private static void AppendCounts(StringBuilder sb, IReadOnlyDictionary<string, int> counts)
    {
        if (counts.Count == 0)
        {
            sb.AppendLine("_Ninguno_");
            return;
        }

        sb.AppendLine("| Reason | Count |");
        sb.AppendLine("|--------|-------|");
        foreach (var kv in counts.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {kv.Key} | {kv.Value} |");
    }

    private static string FormatPf(double v)
    {
        if (double.IsPositiveInfinity(v)) return "+Inf";
        if (double.IsNegativeInfinity(v)) return "-Inf";
        if (double.IsNaN(v)) return "NaN";
        return v.ToString("F4", CultureInfo.InvariantCulture);
    }

    private static string BuildAssumptions(
        SimulationResult a,
        SimulationResult b,
        string sha,
        int bars)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Trade Simulator MVP — Assumptions");
        sb.AppendLine();
        sb.AppendLine("## Datos");
        sb.AppendLine();
        sb.AppendLine($"- CSV SHA-256: `{sha}`");
        sb.AppendLine($"- Barras: {bars}");
        sb.AppendLine("- Gaps temporales: **no rellenados**.");
        sb.AppendLine("- Sin lookahead: señales solo con velas cerradas disponibles.");
        sb.AppendLine();
        sb.AppendLine("## OHLC / Bid-Ask");
        sb.AppendLine();
        sb.AppendLine("- El export histórico **no documenta** si OHLC es Bid, Ask o mid.");
        sb.AppendLine("- **Convención fijada:** OHLC = precio de referencia (mid).");
        sb.AppendLine("- `Bid = Mid − Spread/2`, `Ask = Mid + Spread/2`.");
        sb.AppendLine("- Spread=0 ⇒ Bid = Ask = Mid (Scenario A).");
        sb.AppendLine("- Entrada BUY = Ask open de N+1; SELL = Bid open de N+1.");
        sb.AppendLine("- Salida BUY = Bid; SELL = Ask (coherente; spread no se resta dos veces).");
        sb.AppendLine();
        sb.AppendLine("## Entrada");
        sb.AppendLine();
        sb.AppendLine("- Señal al cierre de N → entrada solo en apertura de N+1.");
        sb.AppendLine("- Sin N+1 → rechazo `MissingNextBar`.");
        sb.AppendLine("- High/Low de N+1 **no** deciden retrospectivamente la entrada.");
        sb.AppendLine();
        sb.AppendLine("## SL / TP");
        sb.AppendLine();
        sb.AppendLine("- SL = geometría causal del breakout (bounds ± 0.10×ATR); no reconstruida después.");
        sb.AppendLine("- TP recalculado desde entrada efectiva: R = |Entry−SL|, TP = Entry ± 2R.");
        sb.AppendLine("- No se usa el TP teórico desde ConfirmationClose.");
        sb.AppendLine();
        sb.AppendLine("## Riesgo / volumen (snapshot DEMO observado)");
        sb.AppendLine();
        sb.AppendLine("| Spec | Valor |");
        sb.AppendLine("|------|-------|");
        sb.AppendLine("| PipSize | 0.01 |");
        sb.AppendLine("| PipValue | 0.01 |");
        sb.AppendLine("| LotSize | 100 |");
        sb.AppendLine("| VolumeMin/Max/Step | 1 / 10000 / 1 |");
        sb.AppendLine("| Risk % | 0.5% del Equity actual |");
        sb.AppendLine("| Initial Equity | 10 000 |");
        sb.AppendLine();
        sb.AppendLine("Estas specs son un snapshot actual, **no** garantía histórica.");
        sb.AppendLine("Volumen: misma semántica Risk (`VolumeForFixedRisk` + floor). Nunca se incrementa.");
        sb.AppendLine();
        sb.AppendLine("## Costes");
        sb.AppendLine();
        sb.AppendLine($"| Escenario | Spread | Commission modeled | Slippage modeled |");
        sb.AppendLine($"|-----------|--------|--------------------|------------------|");
        sb.AppendLine($"| A `{a.ScenarioName}` | {a.SpreadPrice} | {a.CommissionModeled} | {a.SlippageModeled} |");
        sb.AppendLine($"| B `{b.ScenarioName}` | {b.SpreadPrice} | {b.CommissionModeled} | {b.SlippageModeled} |");
        sb.AppendLine();
        sb.AppendLine("- Commission = 0 y **explícitamente no modelada**.");
        sb.AppendLine("- Slippage = 0 y **explícitamente no modelado**.");
        sb.AppendLine("- Scenario B **no** afirma costes reales completos.");
        sb.AppendLine();
        sb.AppendLine("## Gestión de posición / gaps");
        sb.AppendLine();
        sb.AppendLine("- Máximo 1 posición simultánea; señales con posición abierta → `PositionAlreadyOpen`.");
        sb.AppendLine("- SL+TP misma vela → SL primero + `AmbiguousSameBar=true`.");
        sb.AppendLine("- Apertura más allá de SL/TP → salida al open simulado (`GapExit=true`), no al precio ideal.");
        sb.AppendLine("- Serie termina con posición → cierre al último precio disponible (`EndOfData`).");
        sb.AppendLine();
        sb.AppendLine("## Prohibiciones");
        sb.AppendLine();
        sb.AppendLine("- No se ejecutaron órdenes reales ni DEMO.");
        sb.AppendLine("- No se modificaron algoritmos productivos Swing / S/R / T3 / Risk / ExecutionPrep.");
        sb.AppendLine("- No se optimizaron parámetros.");
        return sb.ToString();
    }
}
