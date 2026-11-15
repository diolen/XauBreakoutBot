using cAlgo.Robots.SupportResistance;
using XauSwingAbHarness.Experiment;
using XauSwingAbHarness.Metrics;

namespace XauSwingAbHarness.Breakout;

public sealed class ReactionStudyRow
{
    public required T3EventRecord T3 { get; init; }
    public required string FilterTag { get; init; }
    public required bool HorizonTruncated { get; init; }
    public required IReadOnlyDictionary<OhlcAmbiguityPolicy, ReactionEvaluation> ByPolicy { get; init; }
}

/// <summary>
/// Reacción desde Close@T3 (experimento A/B corregido), no desde confirmación de breakout.
/// </summary>
public static class ReactionStudy
{
    public static ReactionStudyRow EvaluateFromT3(
        T3EventRecord t3,
        IReadOnlyList<OhlcBar> bars,
        BreakoutExperimentConfig config,
        string filterTag)
    {
        var i0 = t3.T3BarIndex;
        var end = Math.Min(bars.Count - 1, i0 + config.ReactionHorizonBars);
        var truncated = end < i0 + config.ReactionHorizonBars;
        var path = new List<(double, double)>();
        for (var i = i0 + 1; i <= end; i++)
            path.Add((bars[i].High, bars[i].Low));

        var expectsUp = t3.Geometry.Role == LevelRole.Resistance;
        var atr = t3.AtrAtT3;
        var byPolicy = path.Count == 0 || !IsFinite(atr) || atr <= 0
            ? UnresolvedAll()
            : OhlcPathEvaluator.EvaluateAllPolicies(
                path, t3.CloseAtT3, atr, config.ReactionThresholdAtr, expectsUp);

        return new ReactionStudyRow
        {
            T3 = t3,
            FilterTag = filterTag,
            HorizonTruncated = truncated,
            ByPolicy = byPolicy
        };
    }

    public static IReadOnlyList<PolicyAggregate> Aggregate(
        IEnumerable<ReactionStudyRow> rows,
        string study,
        int horizon)
    {
        var list = rows.ToList();
        var result = new List<PolicyAggregate>();
        foreach (var group in list.GroupBy(r => (r.T3.VariantId, r.T3.SwingK, r.T3.Segment)))
        {
            foreach (OhlcAmbiguityPolicy p in Enum.GetValues(typeof(OhlcAmbiguityPolicy)))
            {
                var evals = group.Select(r => r.ByPolicy[p]).ToList();
                result.Add(PolicyAggregateBuilder.Build(
                    group.Key.VariantId, group.Key.SwingK, group.Key.Segment, study, p, horizon, evals));
            }
        }

        foreach (var group in list.GroupBy(r => (r.T3.VariantId, r.T3.SwingK)))
        {
            foreach (OhlcAmbiguityPolicy p in Enum.GetValues(typeof(OhlcAmbiguityPolicy)))
            {
                var evals = group.Select(r => r.ByPolicy[p]).ToList();
                result.Add(PolicyAggregateBuilder.Build(
                    group.Key.VariantId, group.Key.SwingK, SegmentSplitter.Global, study, p, horizon, evals));
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<OhlcAmbiguityPolicy, ReactionEvaluation> UnresolvedAll()
    {
        var d = new Dictionary<OhlcAmbiguityPolicy, ReactionEvaluation>();
        foreach (OhlcAmbiguityPolicy p in Enum.GetValues(typeof(OhlcAmbiguityPolicy)))
        {
            d[p] = new ReactionEvaluation
            {
                Outcome = ReactionOutcome.Unresolved,
                Policy = p,
                MfeAtr = 0,
                MaeAtr = 0,
                ResolutionBarOffset = null,
                SameBarBothThresholds = false
            };
        }

        return d;
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
