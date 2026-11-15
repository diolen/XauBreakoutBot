using cAlgo.Robots.SupportResistance;
using XauSwingAbHarness.Breakout;
using XauSwingAbHarness.Dataset;
using XauSwingAbHarness.Experiment;
using XauSwingAbHarness.Metrics;

namespace XauSwingAbHarness.Structural;

public sealed class DefinitionMetricsRow
{
    public required string VariantId { get; init; }
    public required int SwingK { get; init; }
    public required string Definition { get; init; } // A or C
    public required string Segment { get; init; }
    public required string Filter { get; init; }
    public required OhlcAmbiguityPolicy Policy { get; init; }
    public int T3Universe { get; init; }
    public int Confirmed { get; init; }
    public double ConfirmationRate { get; init; }
    public double MeanBarsToConfirm { get; init; }
    public int Evaluable { get; init; }
    public int Hits { get; init; }
    public int Misses { get; init; }
    public int AmbiguousSameBar { get; init; }
    public int Unresolved { get; init; }
    public double HitRateReported { get; init; }
    public double MeanMfeAtr { get; init; }
    public double MeanMaeAtr { get; init; }
}

public static class StructuralBreakoutEvaluator
{
    public static IReadOnlyList<DefinitionMetricsRow> EvaluateDefinitions(
        StructuralBreakoutRunResult run,
        IReadOnlyList<OhlcBar> bars,
        StructuralBreakoutConfig config,
        string filter,
        IReadOnlyList<ClassifiedT3Event> universe)
    {
        var rows = new List<DefinitionMetricsRow>();
        foreach (var def in new[] { "A", "C" })
        {
            var confirmed = universe
                .Where(e => def == "A" ? e.ConfirmBarA.HasValue : e.ConfirmBarC.HasValue)
                .ToList();

            foreach (var segGroup in GroupSegments(universe, confirmed))
            {
                var (segName, uniSeg, confSeg) = segGroup;
                var reactions = new List<(OhlcAmbiguityPolicy Policy, ReactionEvaluation Eval)>();
                var barsToConfirm = new List<int>();

                foreach (var e in confSeg)
                {
                    var confirmBar = def == "A" ? e.ConfirmBarA!.Value : e.ConfirmBarC!.Value;
                    var entry = def == "A" ? e.ConfirmCloseA!.Value : e.ConfirmCloseC!.Value;
                    var atr = def == "A" ? e.AtrAtConfirmA!.Value : e.AtrAtConfirmC!.Value;
                    barsToConfirm.Add(confirmBar - e.T3BarIndex);

                    var end = Math.Min(bars.Count - 1, confirmBar + config.ReactionHorizonBars);
                    var path = new List<(double, double)>();
                    for (var i = confirmBar + 1; i <= end; i++)
                        path.Add((bars[i].High, bars[i].Low));

                    var expectsUp = e.Role == LevelRole.Resistance;
                    foreach (var kv in OhlcPathEvaluator.EvaluateAllPolicies(
                                 path, entry, atr, config.ReactionThresholdAtr, expectsUp))
                        reactions.Add((kv.Key, kv.Value));
                }

                foreach (OhlcAmbiguityPolicy p in Enum.GetValues(typeof(OhlcAmbiguityPolicy)))
                {
                    var evals = reactions.Where(r => r.Policy == p).Select(r => r.Eval).ToList();
                    var agg = PolicyAggregateBuilder.Build(
                        run.VariantId, run.SwingK, segName, $"def_{def}", p, config.ReactionHorizonBars, evals);
                    rows.Add(new DefinitionMetricsRow
                    {
                        VariantId = run.VariantId,
                        SwingK = run.SwingK,
                        Definition = def,
                        Segment = segName,
                        Filter = filter,
                        Policy = p,
                        T3Universe = uniSeg.Count,
                        Confirmed = confSeg.Count,
                        ConfirmationRate = uniSeg.Count == 0 ? 0 : confSeg.Count / (double)uniSeg.Count,
                        MeanBarsToConfirm = barsToConfirm.Count == 0 ? 0 : barsToConfirm.Average(),
                        Evaluable = agg.Evaluable,
                        Hits = agg.Hits,
                        Misses = agg.Misses,
                        AmbiguousSameBar = agg.AmbiguousSameBar,
                        Unresolved = agg.Unresolved,
                        HitRateReported = agg.HitRateReported,
                        MeanMfeAtr = agg.MeanMfeAtr,
                        MeanMaeAtr = agg.MeanMaeAtr
                    });
                }
            }
        }

        return rows;
    }

    public static IReadOnlyList<ClassifiedT3Event> FilterByGap(
        IReadOnlyList<ClassifiedT3Event> events,
        IReadOnlyList<TemporalGap> gaps,
        IReadOnlyList<DateTime> openTimes,
        int horizonBars,
        bool restrictedOnly)
    {
        // Reutilizar EventFilters con un adaptador mínimo vía T3EventRecord-like checks.
        return events.Where(e =>
        {
            var fake = new T3EventRecord
            {
                VariantId = e.VariantId,
                SwingK = e.SwingK,
                LevelId = e.LevelId,
                T3BarIndex = e.T3BarIndex,
                T3OpenTimeUtc = openTimes[Math.Clamp(e.T3BarIndex, 0, openTimes.Count - 1)],
                Segment = e.Segment,
                Geometry = LevelGeometrySnapshot.FromLevel(new SrLevel
                {
                    Id = e.LevelId,
                    Role = e.Role,
                    Geometry = e.Geometry,
                    Center = 0,
                    State = LevelState.Active
                }),
                AtrAtT3 = e.AtrAtT3,
                CloseAtT3 = 0
            };
            return !EventFilters.IsExcludedByGap(fake, gaps, openTimes, horizonBars, restrictedOnly);
        }).ToList();
    }

    public static IReadOnlyList<ClassifiedT3Event> Decimate(
        IReadOnlyList<ClassifiedT3Event> events, int minGap)
    {
        var ordered = events.OrderBy(e => e.T3BarIndex).ThenBy(e => e.LevelId).ToList();
        var kept = new List<ClassifiedT3Event>();
        var next = int.MinValue;
        foreach (var e in ordered)
        {
            if (e.T3BarIndex < next)
                continue;
            kept.Add(e);
            next = e.T3BarIndex + minGap + 1;
        }

        return kept;
    }

    private static IEnumerable<(string Seg, List<ClassifiedT3Event> Uni, List<ClassifiedT3Event> Conf)> GroupSegments(
        IReadOnlyList<ClassifiedT3Event> universe,
        List<ClassifiedT3Event> confirmed)
    {
        yield return (SegmentSplitter.Global, universe.ToList(), confirmed);
        foreach (var seg in new[] { SegmentSplitter.Develop, SegmentSplitter.Validate, SegmentSplitter.Oos })
        {
            yield return (
                seg,
                universe.Where(e => e.Segment == seg).ToList(),
                confirmed.Where(e => e.Segment == seg).ToList());
        }
    }
}
