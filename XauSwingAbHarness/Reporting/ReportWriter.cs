using System.Globalization;
using System.Text;
using XauSwingAbHarness.Dataset;
using XauSwingAbHarness.Experiment;
using XauSwingAbHarness.Metrics;

namespace XauSwingAbHarness.Reporting;

public static class ReportWriter
{
    public static void WriteAll(
        string outputDir,
        DatasetValidationResult dataset,
        AbExperimentConfig config,
        VariantRunResult a,
        VariantRunResult b,
        IReadOnlyList<SegmentDescriptiveStats> descriptive,
        IReadOnlyList<LifetimeSummary> lifetimes,
        IReadOnlyList<PredictiveSummary> predictive)
    {
        Directory.CreateDirectory(outputDir);

        File.WriteAllText(
            Path.Combine(outputDir, "dataset_validation.txt"),
            DatasetValidator.FormatSummary(dataset),
            Encoding.UTF8);

        WriteGapsCsv(Path.Combine(outputDir, "temporal_gaps.csv"), dataset.Gaps);
        WriteEventsCsv(Path.Combine(outputDir, "events_A_K2.csv"), a.Events);
        WriteEventsCsv(Path.Combine(outputDir, "events_B_K3.csv"), b.Events);
        File.WriteAllText(Path.Combine(outputDir, "metrics_descriptive.csv"), DescriptiveMetrics.ToCsv(descriptive));
        File.WriteAllText(Path.Combine(outputDir, "metrics_lifetime.csv"), LevelLifetimeMetrics.ToCsv(lifetimes));
        File.WriteAllText(Path.Combine(outputDir, "metrics_predictive.csv"), PredictiveEvaluator.ToCsv(predictive));

        var report = BuildMarkdown(dataset, config, a, b, descriptive, lifetimes, predictive);
        File.WriteAllText(Path.Combine(outputDir, "report_k2_vs_k3.md"), report, Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(outputDir, "limitations.md"),
            BuildLimitations(a, b),
            Encoding.UTF8);
    }

    private static void WriteEventsCsv(string path, IReadOnlyList<ExperimentEvent> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "VariantId,SwingK,Kind,Observability,BarIndex,OpenTimeUtc,Segment,LevelId,SwingId," +
            "Role,Geometry,TouchCount,ConfirmLatencyBars,Price,AtrAtEvent,Detail");
        foreach (var e in events)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{e.VariantId},{e.SwingK},{e.Kind},{e.Observability},{e.BarIndex}," +
                $"{e.OpenTimeUtc:yyyy-MM-dd'T'HH:mm:ss'Z'},{e.Segment}," +
                $"{e.LevelId?.ToString(CultureInfo.InvariantCulture) ?? ""}," +
                $"{e.SwingId?.ToString(CultureInfo.InvariantCulture) ?? ""}," +
                $"{e.Role?.ToString() ?? ""},{e.Geometry?.ToString() ?? ""}," +
                $"{e.TouchCount?.ToString(CultureInfo.InvariantCulture) ?? ""}," +
                $"{e.ConfirmationLatencyBars?.ToString(CultureInfo.InvariantCulture) ?? ""}," +
                $"{Fmt(e.Price)},{Fmt(e.AtrAtEvent)},{Escape(e.Detail)}");
            sb.Append('\n');
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void WriteGapsCsv(string path, IReadOnlyList<TemporalGap> gaps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PreviousOpenUtc,NextOpenUtc,DeltaMinutes,Classification");
        foreach (var g in gaps)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{g.PreviousOpenUtc:yyyy-MM-dd'T'HH:mm:ss'Z'}," +
                $"{g.NextOpenUtc:yyyy-MM-dd'T'HH:mm:ss'Z'}," +
                $"{g.Delta.TotalMinutes:F2},{g.Classification}");
            sb.Append('\n');
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string BuildMarkdown(
        DatasetValidationResult dataset,
        AbExperimentConfig config,
        VariantRunResult a,
        VariantRunResult b,
        IReadOnlyList<SegmentDescriptiveStats> descriptive,
        IReadOnlyList<LifetimeSummary> lifetimes,
        IReadOnlyList<PredictiveSummary> predictive)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Informe comparativo Swing K=2 vs K=3 (XAUUSD M15)");
        sb.AppendLine();
        sb.AppendLine("## Dataset");
        sb.AppendLine();
        sb.AppendLine($"- Path: `{dataset.Path}`");
        sb.AppendLine($"- SHA-256: `{dataset.Sha256Hex}`");
        sb.AppendLine($"- Barras: {dataset.BarCount}");
        sb.AppendLine($"- Rango: {dataset.FirstOpenUtc:O} → {dataset.LastOpenUtc:O}");
        sb.AppendLine($"- Duplicados / OHLC / orden / non-finite: {dataset.DuplicateCount} / {dataset.OhlcErrorCount} / {dataset.OrderErrorCount} / {dataset.NonFiniteCount}");
        sb.AppendLine($"- Saltos temporales: {dataset.Gaps.Count}");
        foreach (var kv in dataset.GapClassCounts.OrderBy(k => k.Key.ToString()))
            sb.AppendLine($"  - {kv.Key}: {kv.Value}");
        sb.AppendLine();
        sb.AppendLine("## Diseño experimental");
        sb.AppendLine();
        sb.AppendLine("- Variante A: `SupportResistanceEngine(swingStrength: 2)`");
        sb.AppendLine("- Variante B: `SupportResistanceEngine(swingStrength: 3)`");
        sb.AppendLine($"- Lookback idéntico: {config.LookbackBars}");
        sb.AppendLine($"- ATR period idéntico: {config.AtrPeriod}");
        sb.AppendLine("- Procesamiento: `ProcessClosedBar` secuencial sobre **todo** el CSV (sin truncar con `ReplayClosedBars` lookback).");
        sb.AppendLine($"- Preload inherente al motor: A=`{a.PreloadBars}` (14+2), B=`{b.PreloadBars}` (14+3). Diferencia documentada, no igualada artificialmente.");
        sb.AppendLine("- Split temporal fijo: 60% develop / 20% validate / 20% OOS (por índice cronológico).");
        sb.AppendLine("- Eventos T≥3 **no** son operaciones reales.");
        sb.AppendLine();
        sb.AppendLine("## Métricas descriptivas (global)");
        sb.AppendLine();
        sb.AppendLine("| Variante | K | Swings | Latencia media conf. | Niveles | Activados | T=2 cand. | T≥3 | Invalid. | T3/1000 barras |");
        sb.AppendLine("|----------|---|--------|----------------------|---------|-----------|-----------|-----|----------|----------------|");
        foreach (var r in descriptive.Where(x => x.Segment == SegmentSplitter.Global))
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.VariantId} | {r.SwingK} | {r.SwingConfirmed} | {r.MeanConfirmationLatencyBars:F2} | " +
                $"{r.LevelsCreated} | {r.LevelsActivated} | {r.EntryCandidatesT2} | {r.TouchCountReached3} | " +
                $"{r.Invalidations} | {r.SignalsPer1000Bars:F2} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Por segmento");
        sb.AppendLine();
        sb.AppendLine("| Variante | Segmento | Swings | T=2 | T≥3 | Invalid. | T3/1000 |");
        sb.AppendLine("|----------|----------|--------|-----|-----|----------|---------|");
        foreach (var r in descriptive.Where(x => x.Segment != SegmentSplitter.Global)
                     .OrderBy(x => x.VariantId).ThenBy(x => x.Segment))
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.VariantId} | {r.Segment} | {r.SwingConfirmed} | {r.EntryCandidatesT2} | " +
                $"{r.TouchCountReached3} | {r.Invalidations} | {r.SignalsPer1000Bars:F2} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Vida útil de niveles invalidados");
        sb.AppendLine();
        sb.AppendLine("| Variante | Segmento | N | Media barras | Mediana |");
        sb.AppendLine("|----------|----------|---|--------------|---------|");
        foreach (var r in lifetimes.OrderBy(x => x.VariantId).ThenBy(x => x.Segment))
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.VariantId} | {r.Segment} | {r.LevelsWithLifetime} | {r.MeanLifetimeBars:F1} | {r.MedianLifetimeBars:F1} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Evaluación predictiva (T≥3, retrospectiva)");
        sb.AppendLine();
        sb.AppendLine("Metodología fijada a priori: tras el primer T≥3 de cada nivel, horizonte H∈{16,32}, umbral 0.5×ATR; Resistance favorece alza, Support favorece baja. No son trades.");
        sb.AppendLine();
        sb.AppendLine("| Variante | Segmento | H | N | HitRate | Media MFE·ATR | Media MAE·ATR | Invalid. en H |");
        sb.AppendLine("|----------|----------|---|---|---------|---------------|---------------|---------------|");
        foreach (var r in predictive)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.VariantId} | {r.Segment} | {r.HorizonBars} | {r.Evaluated} | {r.HitRate:P1} | " +
                $"{r.MeanMfeAtr:F2} | {r.MeanMaeAtr:F2} | {r.Invalidated} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Lectura cautelosa");
        sb.AppendLine();
        sb.AppendLine("- Más swings/señales **no** implica superioridad.");
        sb.AppendLine("- Comparar HitRate y MFE/MAE en **validate** y **oos**, no solo develop.");
        sb.AppendLine("- Invalidación OHLC por Close es un proxy (sin ticks Bid/Ask).");
        sb.AppendLine("- `DefaultSwingStrength = 3` en producción **no** se modifica por este informe.");
        return sb.ToString();
    }

    private static string BuildLimitations(VariantRunResult a, VariantRunResult b)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Limitaciones del harness A/B");
        sb.AppendLine();
        sb.AppendLine("1. Sin datos tick: invalidación y candidatos usan Close OHLC / estado del motor tras vela cerrada.");
        sb.AppendLine("2. `EntryCandidateSelector` estructural (T=2 Active) no exige lado Bid/Ask de mercado en este harness; se registra el nacimiento estructural del candidato.");
        sb.AppendLine("3. PreloadBars difiere con K (A=" + a.PreloadBars + ", B=" + b.PreloadBars + ") por fórmula del motor `ATR+K`.");
        sb.AppendLine("4. No se simulan órdenes, slippage ni spreads.");
        sb.AppendLine("5. Eventos T≥3 no son operaciones; la métrica predictiva es un proxy de reacción de precio.");
        sb.AppendLine("6. Huecos temporales no se rellenan; horizontes cuentan velas disponibles, no minutos de reloj.");
        sb.AppendLine("7. Niveles fusionados/absorbidos pueden alterar Ids y conteos relativos a un escenario sin merge.");
        sb.AppendLine("8. No se dispone de un ledger de invalidaciones tick-faithful en el motor offline.");
        return sb.ToString();
    }

    private static string Fmt(double? v) =>
        v.HasValue && !double.IsNaN(v.Value)
            ? v.Value.ToString("R", CultureInfo.InvariantCulture)
            : "";

    private static string Escape(string? s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace(',', ';');
}
