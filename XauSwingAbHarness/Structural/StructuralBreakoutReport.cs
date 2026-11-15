using System.Globalization;
using System.Text;
using XauSwingAbHarness.Dataset;
using XauSwingAbHarness.Metrics;

namespace XauSwingAbHarness.Structural;

public static class StructuralBreakoutReport
{
    public static void WriteAll(
        string outputDir,
        DatasetValidationResult dataset,
        StructuralBreakoutConfig config,
        StructuralBreakoutRunResult a,
        StructuralBreakoutRunResult b,
        IReadOnlyList<DefinitionMetricsRow> metrics)
    {
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "dataset_validation.txt"), DatasetValidator.FormatSummary(dataset));
        WriteSnapshots(Path.Combine(outputDir, "snapshots_A_K2.csv"), a.Snapshots);
        WriteSnapshots(Path.Combine(outputDir, "snapshots_B_K3.csv"), b.Snapshots);
        WriteClassified(Path.Combine(outputDir, "classified_A_K2.csv"), a.Classified);
        WriteClassified(Path.Combine(outputDir, "classified_B_K3.csv"), b.Classified);
        WriteMetrics(Path.Combine(outputDir, "metrics_definitions.csv"), metrics);
        WriteClassSummary(Path.Combine(outputDir, "class_summary.txt"), a, b);
        File.WriteAllText(Path.Combine(outputDir, "limitations.md"), Limitations(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(outputDir, "report_structural_vs_experimental.md"),
            BuildReport(dataset, config, a, b, metrics), Encoding.UTF8);
    }

    private static void WriteSnapshots(string path, IReadOnlyList<PostT3BarSnapshot> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "VariantId,SwingK,LevelId,Role,Geometry,T3BarIndex,BarIndex,Segment,StateBefore,StateAfter,Close," +
            "MidAtBar,UpperBoundAtBar,LowerBoundAtBar,HalfWidth,AtrRef,AtrCurrent,AtrAtT3," +
            "MeetsExperimentalThreshold,MeetsStructuralInvalidation,InvalidationReason,GeometryChanged,GeometryChangeDetail");
        foreach (var r in rows)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.VariantId},{r.SwingK},{r.LevelId},{r.Role},{r.Geometry},{r.T3BarIndex},{r.BarIndex},{r.Segment}," +
                $"{r.StateBefore},{r.StateAfter},{Fmt(r.Close)},{Fmt(r.MidAtBar)},{Fmt(r.UpperBoundAtBar)},{Fmt(r.LowerBoundAtBar)}," +
                $"{Fmt(r.HalfWidth)},{Fmt(r.AtrRef)},{Fmt(r.AtrCurrent)},{Fmt(r.AtrAtT3)}," +
                $"{r.MeetsExperimentalThreshold},{r.MeetsStructuralInvalidation},{Escape(r.InvalidationReason)}," +
                $"{r.GeometryChanged},{Escape(r.GeometryChangeDetail)}");
            sb.Append('\n');
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void WriteClassified(string path, IReadOnlyList<ClassifiedT3Event> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "VariantId,SwingK,LevelId,Role,Geometry,T3BarIndex,Segment,AtrAtT3,Class,ConfirmBarA,ConfirmBarC," +
            "ConfirmCloseA,ConfirmCloseC,AtrAtConfirmA,AtrAtConfirmC,CensoredEndOfData,OutcomeWindowCrossesSegment,Detail");
        foreach (var r in rows)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.VariantId},{r.SwingK},{r.LevelId},{r.Role},{r.Geometry},{r.T3BarIndex},{r.Segment},{Fmt(r.AtrAtT3)}," +
                $"{r.Class},{r.ConfirmBarA?.ToString(CultureInfo.InvariantCulture) ?? ""}," +
                $"{r.ConfirmBarC?.ToString(CultureInfo.InvariantCulture) ?? ""}," +
                $"{Fmt(r.ConfirmCloseA)},{Fmt(r.ConfirmCloseC)},{Fmt(r.AtrAtConfirmA)},{Fmt(r.AtrAtConfirmC)}," +
                $"{r.CensoredEndOfData},{r.OutcomeWindowCrossesSegment},{Escape(r.Detail)}");
            sb.Append('\n');
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void WriteMetrics(string path, IReadOnlyList<DefinitionMetricsRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "VariantId,SwingK,Definition,Segment,Filter,Policy,T3Universe,Confirmed,ConfirmationRate,MeanBarsToConfirm," +
            "Evaluable,Hits,Misses,AmbiguousSameBar,Unresolved,HitRateReported,MeanMfeAtr,MeanMaeAtr");
        foreach (var r in rows)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.VariantId},{r.SwingK},{r.Definition},{r.Segment},{r.Filter},{r.Policy},{r.T3Universe},{r.Confirmed}," +
                $"{r.ConfirmationRate:F4},{r.MeanBarsToConfirm:F3},{r.Evaluable},{r.Hits},{r.Misses},{r.AmbiguousSameBar}," +
                $"{r.Unresolved},{r.HitRateReported:F4},{r.MeanMfeAtr:F4},{r.MeanMaeAtr:F4}");
            sb.Append('\n');
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void WriteClassSummary(string path, StructuralBreakoutRunResult a, StructuralBreakoutRunResult b)
    {
        var sb = new StringBuilder();
        foreach (var run in new[] { a, b })
        {
            sb.AppendLine($"=== {run.VariantId} T3={run.T3Count} snapshots={run.Snapshots.Count} ===");
            foreach (BreakoutClass c in Enum.GetValues(typeof(BreakoutClass)))
                sb.AppendLine($"  {c}={run.Classified.Count(x => x.Class == c)}");
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static string BuildReport(
        DatasetValidationResult dataset,
        StructuralBreakoutConfig config,
        StructuralBreakoutRunResult a,
        StructuralBreakoutRunResult b,
        IReadOnlyList<DefinitionMetricsRow> metrics)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Structural (A) vs Experimental (C) Breakout tras T=3");
        sb.AppendLine();
        sb.AppendLine($"SHA-256: `{dataset.Sha256Hex}` | Barras: {dataset.BarCount}");
        sb.AppendLine("Originales preservados en `artifacts/swing-ab-original-k2k3/` y `artifacts/swing-ab-breakout-t3/`.");
        sb.AppendLine();
        sb.AppendLine("## Definiciones");
        sb.AppendLine();
        sb.AppendLine("- **A Structural:** Close más allá de Upper/Lower ± 0.10×AtrCurrent + invalidación; nivel válido justo antes.");
        sb.AppendLine("- **C Experimental:** primer Close más allá de Mid ± 0.15×AtrAtT3; permite misma vela que invalidación.");
        sb.AppendLine();
        sb.AppendLine("## Clasificación mutuamente excluyente");
        sb.AppendLine();
        sb.AppendLine("| Variante | OnlyC | A∩C misma vela | A∩C distinta | OnlyA | Neither |");
        sb.AppendLine("|----------|-------|----------------|--------------|-------|---------|");
        foreach (var run in new[] { a, b })
        {
            int N(BreakoutClass c) => run.Classified.Count(x => x.Class == c);
            sb.AppendLine($"| {run.VariantId} | {N(BreakoutClass.OnlyC)} | {N(BreakoutClass.AAndCSameBar)} | " +
                          $"{N(BreakoutClass.AAndCDifferentBars)} | {N(BreakoutClass.OnlyA)} | {N(BreakoutClass.Neither)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## ConfirmationRate y HitRate (ExcludeAmbiguous, baseline)");
        sb.AppendLine();
        sb.AppendLine("| Var | Def | Seg | ConfirmRate | N_conf | HitRate | Amb | Unres | BarsToConfirm |");
        sb.AppendLine("|-----|-----|-----|-------------|--------|---------|-----|-------|---------------|");
        foreach (var r in metrics.Where(m =>
                     m.Filter == "baseline" &&
                     m.Policy == OhlcAmbiguityPolicy.ExcludeAmbiguous &&
                     m.Segment is "global" or "oos"))
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.VariantId} | {r.Definition} | {r.Segment} | {r.ConfirmationRate:P1} | {r.Confirmed} | " +
                $"{r.HitRateReported:P1} | {r.AmbiguousSameBar} | {r.Unresolved} | {r.MeanBarsToConfirm:F2} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Sensibilidades (ExcludeAmbiguous, OOS)");
        sb.AppendLine();
        sb.AppendLine("| Var | Def | Filter | ConfirmRate | HitRate | N_conf |");
        sb.AppendLine("|-----|-----|--------|-------------|---------|--------|");
        foreach (var r in metrics.Where(m =>
                     m.Policy == OhlcAmbiguityPolicy.ExcludeAmbiguous &&
                     m.Segment == "oos" &&
                     m.Filter != "baseline"))
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.VariantId} | {r.Definition} | {r.Filter} | {r.ConfirmationRate:P1} | {r.HitRateReported:P1} | {r.Confirmed} |");
        }

        sb.AppendLine();
        sb.AppendLine("No son operaciones. DefaultSwingStrength=3 intacto.");
        return sb.ToString();
    }

    private static string Limitations() =>
        """
        # Limitaciones — Structural vs Experimental Breakout

        1. Replay OHLC proxy; live usaría Bid/Ask tick.
        2. Snapshots tras ProcessClosedBar; StateBefore capturado antes del process.
        3. Mid/bounds leídos del SrLevel live (incluyen merges); AtrAtT3 fijado en el evento T=3.
        4. Definición A exige coherencia con invalidación del motor en la misma lógica de bounds.
        5. Decimación reduce solape, no implica independencia i.i.d.
        6. HitRate ≠ rentabilidad.
        7. No se implementó T3BreakoutSignal productivo.
        """;

    private static string Fmt(double? v) =>
        v.HasValue && !double.IsNaN(v.Value) ? v.Value.ToString("R", CultureInfo.InvariantCulture) : "";

    private static string Escape(string? s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace(',', ';').Replace('\n', ' ');
}
