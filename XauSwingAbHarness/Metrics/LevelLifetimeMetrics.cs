using System.Globalization;
using System.Text;
using cAlgo.Robots.SupportResistance;
using XauSwingAbHarness.Experiment;

namespace XauSwingAbHarness.Metrics;

public sealed class LifetimeSummary
{
    public required string VariantId { get; init; }
    public required int SwingK { get; init; }
    public required string Segment { get; init; }
    public int LevelsWithLifetime { get; init; }
    public double MeanLifetimeBars { get; init; }
    public double MedianLifetimeBars { get; init; }
}

public static class LevelLifetimeMetrics
{
    /// <summary>
    /// Vida útil = barras desde LevelCreated hasta LevelInvalidated (si existe).
    /// Niveles aún activos al final se excluyen del promedio (lifetime censurado).
    /// </summary>
    public static IReadOnlyList<LifetimeSummary> Compute(VariantRunResult run, int totalBars, AbExperimentConfig config)
    {
        var created = run.Events
            .Where(e => e.Kind == EventKind.LevelCreated && e.LevelId.HasValue)
            .GroupBy(e => e.LevelId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var invalidated = run.Events
            .Where(e => e.Kind == EventKind.LevelInvalidated && e.LevelId.HasValue)
            .GroupBy(e => e.LevelId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(e => e.BarIndex));

        var lifetimes = new List<(string Segment, int Bars)>();
        foreach (var (id, createEv) in created)
        {
            if (!invalidated.TryGetValue(id, out var invBar))
                continue;
            var life = invBar - createEv.BarIndex;
            if (life < 0)
                continue;
            lifetimes.Add((createEv.Segment, life));
        }

        var segments = new[]
        {
            SegmentSplitter.Develop,
            SegmentSplitter.Validate,
            SegmentSplitter.Oos,
            SegmentSplitter.Global
        };

        var result = new List<LifetimeSummary>();
        foreach (var seg in segments)
        {
            var sample = seg == SegmentSplitter.Global
                ? lifetimes.Select(x => x.Bars).ToList()
                : lifetimes.Where(x => x.Segment == seg).Select(x => x.Bars).ToList();
            sample.Sort();
            result.Add(new LifetimeSummary
            {
                VariantId = run.VariantId,
                SwingK = run.SwingK,
                Segment = seg,
                LevelsWithLifetime = sample.Count,
                MeanLifetimeBars = sample.Count == 0 ? 0 : sample.Average(),
                MedianLifetimeBars = sample.Count == 0 ? 0 : Percentile(sample, 0.5)
            });
        }

        return result;
    }

    public static string ToCsv(IEnumerable<LifetimeSummary> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("VariantId,SwingK,Segment,LevelsWithLifetime,MeanLifetimeBars,MedianLifetimeBars");
        foreach (var r in rows)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.VariantId},{r.SwingK},{r.Segment},{r.LevelsWithLifetime}," +
                $"{r.MeanLifetimeBars:F2},{r.MedianLifetimeBars:F2}");
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static double Percentile(IReadOnlyList<int> sorted, double p)
    {
        if (sorted.Count == 0)
            return 0;
        var idx = (int)Math.Round(p * (sorted.Count - 1));
        idx = Math.Clamp(idx, 0, sorted.Count - 1);
        return sorted[idx];
    }
}
