using System.Globalization;
using System.Text;
using XauSwingAbHarness.Experiment;

namespace XauSwingAbHarness.Metrics;

public sealed class SegmentDescriptiveStats
{
    public required string VariantId { get; init; }
    public required int SwingK { get; init; }
    public required string Segment { get; init; }
    public int SwingConfirmed { get; init; }
    public double MeanConfirmationLatencyBars { get; init; }
    public int LevelsCreated { get; init; }
    public int LevelsActivated { get; init; }
    public int IndependentTouches { get; init; }
    public int EntryCandidatesT2 { get; init; }
    public int TouchCountReached3 { get; init; }
    public int Invalidations { get; init; }
    public double SignalsPer1000Bars { get; init; }
    public int SegmentBarCount { get; init; }
}

public static class DescriptiveMetrics
{
    public static IReadOnlyList<SegmentDescriptiveStats> Compute(
        VariantRunResult run,
        int totalBars,
        double developFrac,
        double validateFrac)
    {
        var (devEnd, valEnd) = SegmentSplitter.Bounds(totalBars, developFrac, validateFrac);
        var segments = new[]
        {
            (SegmentSplitter.Develop, 0, devEnd),
            (SegmentSplitter.Validate, devEnd, valEnd),
            (SegmentSplitter.Oos, valEnd, totalBars),
            (SegmentSplitter.Global, 0, totalBars)
        };

        var list = new List<SegmentDescriptiveStats>();
        foreach (var (name, start, end) in segments)
        {
            var barCount = Math.Max(0, end - start);
            var ev = run.Events.Where(e => e.BarIndex >= start && e.BarIndex < end).ToList();
            var swings = ev.Where(e => e.Kind == EventKind.SwingConfirmed).ToList();
            var latency = swings
                .Where(e => e.ConfirmationLatencyBars.HasValue)
                .Select(e => (double)e.ConfirmationLatencyBars!.Value)
                .ToList();

            var t3 = ev.Count(e => e.Kind == EventKind.TouchCountReached3);
            list.Add(new SegmentDescriptiveStats
            {
                VariantId = run.VariantId,
                SwingK = run.SwingK,
                Segment = name,
                SwingConfirmed = swings.Count,
                MeanConfirmationLatencyBars = latency.Count == 0 ? 0 : latency.Average(),
                LevelsCreated = ev.Count(e => e.Kind == EventKind.LevelCreated),
                LevelsActivated = ev.Count(e => e.Kind == EventKind.LevelActivated),
                IndependentTouches = ev.Count(e => e.Kind == EventKind.IndependentTouch),
                EntryCandidatesT2 = ev.Count(e => e.Kind == EventKind.EntryCandidateT2),
                TouchCountReached3 = t3,
                Invalidations = ev.Count(e => e.Kind == EventKind.LevelInvalidated),
                SignalsPer1000Bars = barCount == 0 ? 0 : t3 * 1000.0 / barCount,
                SegmentBarCount = barCount
            });
        }

        return list;
    }

    public static string ToCsv(IEnumerable<SegmentDescriptiveStats> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "VariantId,SwingK,Segment,SegmentBars,SwingConfirmed,MeanConfirmLatencyBars," +
            "LevelsCreated,LevelsActivated,IndependentTouches,EntryCandidatesT2," +
            "TouchCountReached3,Invalidations,T3Per1000Bars");
        foreach (var r in rows)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.VariantId},{r.SwingK},{r.Segment},{r.SegmentBarCount},{r.SwingConfirmed}," +
                $"{r.MeanConfirmationLatencyBars:F4},{r.LevelsCreated},{r.LevelsActivated}," +
                $"{r.IndependentTouches},{r.EntryCandidatesT2},{r.TouchCountReached3}," +
                $"{r.Invalidations},{r.SignalsPer1000Bars:F4}");
            sb.Append('\n');
        }

        return sb.ToString();
    }
}
