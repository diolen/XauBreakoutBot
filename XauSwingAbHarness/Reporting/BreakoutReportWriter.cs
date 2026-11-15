using System.Globalization;
using System.Text;
using XauSwingAbHarness.Breakout;
using XauSwingAbHarness.Dataset;
using XauSwingAbHarness.Metrics;

namespace XauSwingAbHarness.Reporting;

public static class BreakoutReportWriter
{
    public static void WritePolicyCsv(string path, IEnumerable<PolicyAggregate> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "Study,VariantId,SwingK,Segment,Policy,Horizon,Evaluable,Hits,Misses,AmbiguousSameBar," +
            "Unresolved,HitRateReported,HitRateAmongResolved,MeanMfeAtr,MeanMaeAtr");
        foreach (var r in rows)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.Study},{r.VariantId},{r.SwingK},{r.Segment},{r.Policy},{r.HorizonBars}," +
                $"{r.Evaluable},{r.Hits},{r.Misses},{r.AmbiguousSameBar},{r.Unresolved}," +
                $"{r.HitRateReported:F4},{r.HitRateAmongResolved:F4},{r.MeanMfeAtr:F4},{r.MeanMaeAtr:F4}");
            sb.Append('\n');
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static void WriteConfirmCsv(string path, IEnumerable<BreakoutConfirmResult> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "VariantId,SwingK,Segment,LevelId,LevelType,Geometry,T3BarIndex,Status,ConfirmationBarIndex," +
            "BarsToConfirm,LevelPriceAtConfirm,ATRAtConfirmation,BreakoutDirection,ConfirmationClose,Detail");
        foreach (var r in rows)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.T3.VariantId},{r.T3.SwingK},{r.T3.Segment},{r.T3.LevelId},{r.T3.Geometry.Role}," +
                $"{r.T3.Geometry.Geometry},{r.T3.T3BarIndex},{r.Status}," +
                $"{r.ConfirmationBarIndex?.ToString(CultureInfo.InvariantCulture) ?? ""}," +
                $"{r.BarsToConfirm?.ToString(CultureInfo.InvariantCulture) ?? ""}," +
                $"{Fmt(r.LevelPriceAtConfirm)},{Fmt(r.AtrAtConfirmation)}," +
                $"{r.BreakoutDirection ?? ""},{Fmt(r.ConfirmationClose)},{Escape(r.Detail)}");
            sb.Append('\n');
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static void WriteMarkdown(
        string path,
        DatasetValidationResult dataset,
        BreakoutExperimentConfig config,
        IReadOnlyList<PolicyAggregate> reactionPolicies,
        IReadOnlyList<BreakoutConfirmResult> confirmsA,
        IReadOnlyList<BreakoutConfirmResult> confirmsB,
        IReadOnlyList<PolicyAggregate> postPolicies,
        string limitations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Breakout en T=3 — informe experimental");
        sb.AppendLine();
        sb.AppendLine("## Preservación");
        sb.AppendLine();
        sb.AppendLine("- Resultados originales K=2 vs K=3: `artifacts/swing-ab-original-k2k3/`");
        sb.AppendLine($"- Dataset SHA-256: `{dataset.Sha256Hex}`");
        sb.AppendLine($"- Barras: {dataset.BarCount} | Gaps: {dataset.Gaps.Count}");
        sb.AppendLine();
        sb.AppendLine("## Capacidades del motor (inspección)");
        sb.AppendLine();
        sb.AppendLine("- Rol: `LevelRole` Support/Resistance");
        sb.AppendLine("- Precio horizontal: `Center`; diagonal: `ProjectPriceAtBar(barIndex)` causal");
        sb.AppendLine("- Estado: Provisional / Active / Invalidated");
        sb.AppendLine("- T=3: primer `IndependentTouchCount >= 3` por nivel");
        sb.AppendLine("- Invalidación OHLC productiva: Close vs bounds + `BreakoutBufferAtr=0.10` (no modificada)");
        sb.AppendLine("- Confirmación experimental: Close vs LevelPrice ± **0.15×ATR** (hipótesis, ≠ productiva)");
        sb.AppendLine();
        sb.AppendLine("## HitRate corregido desde Close@T3 (H=16)");
        sb.AppendLine();
        sb.AppendLine("| Study | Var | Seg | Policy | N | Hits | Miss | Amb | Unres | HitRate |");
        sb.AppendLine("|-------|-----|-----|--------|---|------|------|-----|-------|---------|");
        foreach (var r in reactionPolicies
                     .Where(x => x.Segment is "global" or "oos")
                     .OrderBy(x => x.Study).ThenBy(x => x.VariantId).ThenBy(x => x.Segment).ThenBy(x => x.Policy))
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.Study} | {r.VariantId} | {r.Segment} | {r.Policy} | {r.Evaluable} | {r.Hits} | " +
                $"{r.Misses} | {r.AmbiguousSameBar} | {r.Unresolved} | {r.HitRateReported:P1} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Confirmación de breakout (máx. 8 velas post-T3)");
        sb.AppendLine();
        AppendConfirmTable(sb, "A_K2", confirmsA);
        AppendConfirmTable(sb, "B_K3", confirmsB);

        sb.AppendLine();
        sb.AppendLine("## Post-confirmación (H=16, umbral 0.5×ATR)");
        sb.AppendLine();
        sb.AppendLine("| Var | Seg | Policy | N | Hits | Miss | Amb | Unres | HitRate |");
        sb.AppendLine("|-----|-----|--------|---|------|------|-----|-------|---------|");
        foreach (var r in postPolicies
                     .Where(x => x.Study == "post_breakout" && x.Segment is "global" or "oos")
                     .OrderBy(x => x.VariantId).ThenBy(x => x.Segment).ThenBy(x => x.Policy))
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.VariantId} | {r.Segment} | {r.Policy} | {r.Evaluable} | {r.Hits} | {r.Misses} | " +
                $"{r.AmbiguousSameBar} | {r.Unresolved} | {r.HitRateReported:P1} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Limitaciones");
        sb.AppendLine();
        sb.AppendLine(limitations);
        sb.AppendLine();
        sb.AppendLine("No son operaciones reales. `DefaultSwingStrength=3` no se modifica.");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void AppendConfirmTable(StringBuilder sb, string label, IReadOnlyList<BreakoutConfirmResult> rows)
    {
        var n = rows.Count;
        var conf = rows.Count(r => r.Status == BreakoutConfirmStatus.Confirmed);
        var notc = rows.Count(r => r.Status == BreakoutConfirmStatus.NotConfirmedInWindow);
        var inv = rows.Count(r => r.Status == BreakoutConfirmStatus.InvalidatedBeforeConfirm);
        var excl = rows.Count(r => r.Status == BreakoutConfirmStatus.ExcludedNoGeometry);
        var rate = n == 0 ? 0 : conf / (double)n;
        var lat = rows.Where(r => r.BarsToConfirm.HasValue).Select(r => (double)r.BarsToConfirm!.Value).ToList();
        sb.AppendLine($"### {label}");
        sb.AppendLine();
        sb.AppendLine($"- T=3: **{n}**");
        sb.AppendLine($"- Confirmados: **{conf}** ({rate:P1})");
        sb.AppendLine($"- No confirmados en ventana: {notc}");
        sb.AppendLine($"- Invalidados antes/en confirmación: {inv}");
        sb.AppendLine($"- Excluidos sin geometría: {excl}");
        if (lat.Count > 0)
            sb.AppendLine(CultureInfo.InvariantCulture, $"- BarsToConfirm medio: {lat.Average():F2}");
        sb.AppendLine();
    }

    private static string Fmt(double? v) =>
        v.HasValue && !double.IsNaN(v.Value) ? v.Value.ToString("R", CultureInfo.InvariantCulture) : "";

    private static string Escape(string? s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace(',', ';');
}
