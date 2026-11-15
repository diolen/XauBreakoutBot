using cAlgo.Robots.SupportResistance;
using XauSwingAbHarness.Experiment;
using XauSwingAbHarness.Metrics;

namespace XauSwingAbHarness.Breakout;

public enum BreakoutConfirmStatus
{
    Confirmed = 0,
    NotConfirmedInWindow = 1,
    InvalidatedBeforeConfirm = 2,
    ExcludedNoGeometry = 3,
    IncompleteHorizon = 4
}

public sealed class BreakoutConfirmResult
{
    public required T3EventRecord T3 { get; init; }
    public required BreakoutConfirmStatus Status { get; init; }
    public int? ConfirmationBarIndex { get; init; }
    public int? BarsToConfirm { get; init; }
    public double? LevelPriceAtConfirm { get; init; }
    public double? AtrAtConfirmation { get; init; }
    public double? ConfirmationClose { get; init; }
    public string? BreakoutDirection { get; init; }
    public string? Detail { get; init; }
}

public sealed class PostBreakoutResult
{
    public required BreakoutConfirmResult Confirm { get; init; }
    public required IReadOnlyDictionary<OhlcAmbiguityPolicy, ReactionEvaluation> Reactions { get; init; }
    public required bool HorizonTruncated { get; init; }
    public required bool OutcomeCrossesNextSegment { get; init; }
}

public static class BreakoutConfirmation
{
    /// <summary>
    /// Confirmación experimental (hipótesis): Close cruza LevelPrice ± buffer en velas
    /// estrictamente posteriores a T3, máx. MaxBarsToConfirm. Sin usar la vela T3.
    /// Si hay invalidación productiva en (T3, confirm], no se trata como señal válida.
    /// </summary>
    public static BreakoutConfirmResult TryConfirm(
        T3EventRecord t3,
        IReadOnlyList<OhlcBar> bars,
        BreakoutExperimentConfig config,
        IReadOnlyList<double>? atrByBar = null)
    {
        if (!t3.Geometry.CanEvaluateBreakout)
        {
            return new BreakoutConfirmResult
            {
                T3 = t3,
                Status = BreakoutConfirmStatus.ExcludedNoGeometry,
                Detail = "cannot-project-level-price"
            };
        }

        var i0 = t3.T3BarIndex;
        var last = Math.Min(bars.Count - 1, i0 + config.MaxBarsToConfirm);
        if (last <= i0)
        {
            return new BreakoutConfirmResult
            {
                T3 = t3,
                Status = BreakoutConfirmStatus.IncompleteHorizon,
                Detail = "no-bars-after-t3"
            };
        }

        if (!IsFinite(t3.AtrAtT3) || t3.AtrAtT3 <= 0)
        {
            return new BreakoutConfirmResult
            {
                T3 = t3,
                Status = BreakoutConfirmStatus.NotConfirmedInWindow,
                Detail = "atr-not-ready-at-t3"
            };
        }

        for (var i = i0 + 1; i <= last; i++)
        {
            if (t3.InvalidationBarIndex is int inv && inv > i0 && inv <= i)
            {
                return new BreakoutConfirmResult
                {
                    T3 = t3,
                    Status = BreakoutConfirmStatus.InvalidatedBeforeConfirm,
                    Detail = $"invalidated-at-{inv}-before-or-on-candidate"
                };
            }

            var levelPx = t3.Geometry.PriceAtBar(i);
            var buffer = config.ConfirmationBufferAtr * t3.AtrAtT3;
            var close = bars[i].Close;
            var isRes = t3.Geometry.Role == LevelRole.Resistance;
            var confirmed = isRes ? close > levelPx + buffer : close < levelPx - buffer;
            if (!confirmed)
                continue;

            var atrConfirm = atrByBar != null && i < atrByBar.Count && IsFinite(atrByBar[i]) && atrByBar[i] > 0
                ? atrByBar[i]
                : t3.AtrAtT3;

            return new BreakoutConfirmResult
            {
                T3 = t3,
                Status = BreakoutConfirmStatus.Confirmed,
                ConfirmationBarIndex = i,
                BarsToConfirm = i - i0,
                LevelPriceAtConfirm = levelPx,
                AtrAtConfirmation = atrConfirm,
                ConfirmationClose = close,
                BreakoutDirection = isRes ? "Up" : "Down",
                Detail = "experimental-close-beyond-level"
            };
        }

        return new BreakoutConfirmResult
        {
            T3 = t3,
            Status = BreakoutConfirmStatus.NotConfirmedInWindow,
            Detail = "no-close-beyond-buffer-in-window"
        };
    }

    public static PostBreakoutResult EvaluatePostBreakout(
        BreakoutConfirmResult confirm,
        IReadOnlyList<OhlcBar> bars,
        BreakoutExperimentConfig config,
        int totalBars)
    {
        if (confirm.Status != BreakoutConfirmStatus.Confirmed ||
            !confirm.ConfirmationBarIndex.HasValue ||
            !confirm.ConfirmationClose.HasValue ||
            !confirm.AtrAtConfirmation.HasValue)
        {
            return new PostBreakoutResult
            {
                Confirm = confirm,
                Reactions = new Dictionary<OhlcAmbiguityPolicy, ReactionEvaluation>(),
                HorizonTruncated = false,
                OutcomeCrossesNextSegment = false
            };
        }

        var c = confirm.ConfirmationBarIndex.Value;
        var end = Math.Min(bars.Count - 1, c + config.ReactionHorizonBars);
        var truncated = end < c + config.ReactionHorizonBars;
        var path = new List<(double, double)>(Math.Max(0, end - c));
        for (var i = c + 1; i <= end; i++)
            path.Add((bars[i].High, bars[i].Low));

        var expectsUp = confirm.T3.Geometry.Role == LevelRole.Resistance;
        var reactions = path.Count == 0
            ? EmptyUnresolved(config)
            : OhlcPathEvaluator.EvaluateAllPolicies(
                path,
                confirm.ConfirmationClose.Value,
                confirm.AtrAtConfirmation.Value,
                config.ReactionThresholdAtr,
                expectsUp);

        var (devEnd, valEnd) = SegmentSplitter.Bounds(totalBars, config.DevelopFraction, config.ValidateFraction);
        var crosses = confirm.T3.Segment switch
        {
            SegmentSplitter.Develop => end >= devEnd,
            SegmentSplitter.Validate => end >= valEnd,
            _ => false
        };

        return new PostBreakoutResult
        {
            Confirm = confirm,
            Reactions = reactions,
            HorizonTruncated = truncated,
            OutcomeCrossesNextSegment = crosses
        };
    }

    private static IReadOnlyDictionary<OhlcAmbiguityPolicy, ReactionEvaluation> EmptyUnresolved(
        BreakoutExperimentConfig config)
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
