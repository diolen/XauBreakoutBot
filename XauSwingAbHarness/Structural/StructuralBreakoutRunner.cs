using cAlgo.Robots.SupportResistance;
using XauSwingAbHarness.Experiment;

namespace XauSwingAbHarness.Structural;

public sealed class StructuralBreakoutRunResult
{
    public required string VariantId { get; init; }
    public required int SwingK { get; init; }
    public required IReadOnlyList<PostT3BarSnapshot> Snapshots { get; init; }
    public required IReadOnlyList<ClassifiedT3Event> Classified { get; init; }
    public required int T3Count { get; init; }
}

/// <summary>
/// Replay causal con snapshots post-T=3 y clasificación A (estructural) vs C (experimental).
/// </summary>
public static class StructuralBreakoutRunner
{
    private sealed class TrackedLevel
    {
        public required long LevelId { get; init; }
        public required LevelRole Role { get; init; }
        public required LevelGeometry Geometry { get; init; }
        public required int T3BarIndex { get; init; }
        public required string Segment { get; init; }
        public required double AtrAtT3 { get; init; }
        public double? LastMid { get; set; }
        public double? LastHalfWidth { get; set; }
        public double? LastSlope { get; set; }
        public bool Closed { get; set; }
        public int? ConfirmA { get; set; }
        public int? ConfirmC { get; set; }
        public double? CloseA { get; set; }
        public double? CloseC { get; set; }
        public double? AtrA { get; set; }
        public double? AtrC { get; set; }
        public bool SawPriorInvalidation { get; set; }
    }

    public static StructuralBreakoutRunResult Run(
        string variantId,
        int swingK,
        IReadOnlyList<OhlcBar> bars,
        StructuralBreakoutConfig config)
    {
        var engine = new SupportResistanceEngine(swingK, config.LookbackBars, config.AtrPeriod);
        var snapshots = new List<PostT3BarSnapshot>(capacity: 16384);
        var tracked = new Dictionary<long, TrackedLevel>();
        var prevTouch = new Dictionary<long, int>();
        var prevState = new Dictionary<long, LevelState>();
        var seenT3 = new HashSet<long>();
        var (devEnd, valEnd) = SegmentSplitter.Bounds(bars.Count, config.DevelopFraction, config.ValidateFraction);

        for (var i = 0; i < bars.Count; i++)
        {
            var src = bars[i];
            var bar = new OhlcBar(i, src.OpenTime, src.Open, src.High, src.Low, src.Close);

            // StateBefore para niveles trackeados aún abiertos.
            var stateBefore = new Dictionary<long, LevelState>();
            foreach (var kv in tracked)
            {
                if (kv.Value.Closed)
                    continue;
                var live = FindLevel(engine, kv.Key);
                stateBefore[kv.Key] = live?.State ?? LevelState.Invalidated;
            }

            engine.ProcessClosedBar(bar);

            var segment = SegmentSplitter.SegmentOf(i, bars.Count, config.DevelopFraction, config.ValidateFraction);
            var atrCurrent = engine.AtrReady ? engine.AtrCurrent : double.NaN;

            // Detectar T=3 nuevos (toque cruza >=3 en esta barra).
            foreach (var level in engine.Levels)
            {
                var oldTouches = prevTouch.GetValueOrDefault(level.Id, 0);
                var oldState = prevState.GetValueOrDefault(level.Id, LevelState.Provisional);

                if (level.IndependentTouchCount >= 3 && oldTouches < 3 && seenT3.Add(level.Id))
                {
                    // No usar invalidación simultánea/anterior a T=3 como confirmación posterior.
                    if (level.State == LevelState.Invalidated || oldState == LevelState.Invalidated)
                    {
                        // T=3 en misma barra que invalidación: trackear pero marcar prior invalidation.
                        tracked[level.Id] = new TrackedLevel
                        {
                            LevelId = level.Id,
                            Role = level.Role,
                            Geometry = level.Geometry,
                            T3BarIndex = i,
                            Segment = segment,
                            AtrAtT3 = atrCurrent,
                            SawPriorInvalidation = true,
                            Closed = true
                        };
                    }
                    else
                    {
                        tracked[level.Id] = new TrackedLevel
                        {
                            LevelId = level.Id,
                            Role = level.Role,
                            Geometry = level.Geometry,
                            T3BarIndex = i,
                            Segment = segment,
                            AtrAtT3 = IsFinite(atrCurrent) ? atrCurrent : double.NaN,
                            LastMid = Mid(level, i),
                            LastHalfWidth = level.HalfWidth,
                            LastSlope = level.SlopePerBar
                        };
                    }
                }

                prevTouch[level.Id] = level.IndependentTouchCount;
                prevState[level.Id] = level.State;
            }

            // Snapshots en barras estrictamente posteriores a T=3, ventana 8.
            foreach (var kv in tracked.ToList())
            {
                var t = kv.Value;
                if (t.Closed || t.SawPriorInvalidation)
                    continue;
                if (i <= t.T3BarIndex)
                    continue;
                if (i > t.T3BarIndex + config.MaxBarsToConfirm)
                {
                    t.Closed = true;
                    continue;
                }

                var level = FindLevel(engine, t.LevelId);
                if (level == null)
                {
                    t.Closed = true;
                    continue;
                }

                var before = stateBefore.GetValueOrDefault(t.LevelId, LevelState.Active);
                var after = level.State;
                var mid = Mid(level, i);
                var hw = level.HalfWidth;
                var upper = level.UpperBoundAtBar(i);
                var lower = level.LowerBoundAtBar(i);
                var atrRef = level.AtrRef;

                var geomChanged = t.LastMid.HasValue && (
                    Math.Abs(t.LastMid.Value - mid) > 1e-9 ||
                    Math.Abs((t.LastHalfWidth ?? hw) - hw) > 1e-9 ||
                    Math.Abs((t.LastSlope ?? level.SlopePerBar) - level.SlopePerBar) > 1e-12);
                var geomDetail = geomChanged
                    ? $"mid:{t.LastMid}->{mid};hw:{t.LastHalfWidth}->{hw};slope:{t.LastSlope}->{level.SlopePerBar}"
                    : null;

                var meetsC = IsFinite(t.AtrAtT3) && t.AtrAtT3 > 0 && MeetsExperimental(
                    level.Role, bar.Close, mid, t.AtrAtT3, config.ExperimentalBufferAtr);
                var meetsA = IsFinite(atrCurrent) && atrCurrent > 0 && MeetsStructural(
                    level.Role, bar.Close, upper, lower, atrCurrent, config.StructuralBufferAtr);

                // Coherencia: invalidación del motor en esta barra.
                var invalidatedNow = before != LevelState.Invalidated && after == LevelState.Invalidated;
                if (invalidatedNow)
                    meetsA = true; // la regla productiva disparó (o equivalente)

                var reason = invalidatedNow
                    ? (level.Invalidation?.RuleTag ?? "invalidated")
                    : (meetsA ? "close-beyond-bound-buffer" : null);

                snapshots.Add(new PostT3BarSnapshot
                {
                    VariantId = variantId,
                    SwingK = swingK,
                    LevelId = t.LevelId,
                    Role = t.Role,
                    Geometry = t.Geometry,
                    T3BarIndex = t.T3BarIndex,
                    BarIndex = i,
                    Segment = t.Segment,
                    StateBefore = before,
                    StateAfter = after,
                    Close = bar.Close,
                    MidAtBar = mid,
                    UpperBoundAtBar = upper,
                    LowerBoundAtBar = lower,
                    HalfWidth = hw,
                    AtrRef = atrRef,
                    AtrCurrent = atrCurrent,
                    AtrAtT3 = t.AtrAtT3,
                    MeetsExperimentalThreshold = meetsC,
                    MeetsStructuralInvalidation = meetsA,
                    InvalidationReason = reason,
                    GeometryChanged = geomChanged,
                    GeometryChangeDetail = geomDetail
                });

                t.LastMid = mid;
                t.LastHalfWidth = hw;
                t.LastSlope = level.SlopePerBar;

                // Confirmación A: invalidación productiva con nivel válido justo antes (regla estructural).
                if (t.ConfirmA == null &&
                    before is LevelState.Active or LevelState.Provisional &&
                    after == LevelState.Invalidated)
                {
                    t.ConfirmA = i;
                    t.CloseA = bar.Close;
                    t.AtrA = atrCurrent;
                }

                // Confirmación C: primera vez mid±0.15; permitir misma vela que invalidación;
                // no si ya había invalidación en barra previa.
                if (t.ConfirmC == null && meetsC && before != LevelState.Invalidated)
                {
                    t.ConfirmC = i;
                    t.CloseC = bar.Close;
                    t.AtrC = atrCurrent;
                }

                if (before != LevelState.Invalidated && after == LevelState.Invalidated)
                    t.SawPriorInvalidation = true; // bloquea C retrospectivo en barras siguientes

                if (t.ConfirmA != null && t.ConfirmC != null)
                    t.Closed = true;
                else if (after == LevelState.Invalidated && t.ConfirmC != null)
                    t.Closed = true; // C ya capturado; A puede haberse capturado misma vela
                else if (after == LevelState.Invalidated && t.ConfirmA != null)
                    t.Closed = true;
                else if (after == LevelState.Invalidated)
                    t.Closed = true; // sin C previo: no más confirmaciones
            }
        }

        var classified = new List<ClassifiedT3Event>();
        foreach (var t in tracked.Values.OrderBy(x => x.T3BarIndex))
        {
            var cls = Classify(t.ConfirmA, t.ConfirmC);
            var windowEnd = Math.Min(bars.Count - 1, t.T3BarIndex + config.MaxBarsToConfirm);
            var censored = t.ConfirmA == null && t.ConfirmC == null && windowEnd >= bars.Count - 1 &&
                           t.T3BarIndex + config.MaxBarsToConfirm > bars.Count - 1;

            var evalEndA = t.ConfirmA.HasValue
                ? Math.Min(bars.Count - 1, t.ConfirmA.Value + config.ReactionHorizonBars)
                : -1;
            var evalEndC = t.ConfirmC.HasValue
                ? Math.Min(bars.Count - 1, t.ConfirmC.Value + config.ReactionHorizonBars)
                : -1;
            var crosses = CrossesSegment(t.Segment, Math.Max(evalEndA, evalEndC), devEnd, valEnd);

            string? detail = null;
            if (t.SawPriorInvalidation && t.ConfirmA == null && t.ConfirmC == null &&
                !snapshots.Any(s => s.LevelId == t.LevelId))
                detail = "t3-with-invalidation-same-or-prior-bar";

            classified.Add(new ClassifiedT3Event
            {
                VariantId = variantId,
                SwingK = swingK,
                LevelId = t.LevelId,
                Role = t.Role,
                Geometry = t.Geometry,
                T3BarIndex = t.T3BarIndex,
                Segment = t.Segment,
                AtrAtT3 = t.AtrAtT3,
                Class = cls,
                ConfirmBarA = t.ConfirmA,
                ConfirmBarC = t.ConfirmC,
                ConfirmCloseA = t.CloseA,
                ConfirmCloseC = t.CloseC,
                AtrAtConfirmA = t.AtrA,
                AtrAtConfirmC = t.AtrC,
                CensoredEndOfData = censored,
                OutcomeWindowCrossesSegment = crosses,
                Detail = detail
            });
        }

        return new StructuralBreakoutRunResult
        {
            VariantId = variantId,
            SwingK = swingK,
            Snapshots = snapshots,
            Classified = classified,
            T3Count = seenT3.Count
        };
    }

    public static BreakoutClass Classify(int? a, int? c)
    {
        if (a == null && c == null)
            return BreakoutClass.Neither;
        if (a == null)
            return BreakoutClass.OnlyC;
        if (c == null)
            return BreakoutClass.OnlyA;
        if (a == c)
            return BreakoutClass.AAndCSameBar;
        return BreakoutClass.AAndCDifferentBars;
    }

    public static bool MeetsExperimental(LevelRole role, double close, double mid, double atrAtT3, double bufMult)
    {
        var buf = bufMult * atrAtT3;
        return role == LevelRole.Resistance ? close > mid + buf : close < mid - buf;
    }

    public static bool MeetsStructural(
        LevelRole role, double close, double upper, double lower, double atrCurrent, double bufMult)
    {
        var buf = bufMult * atrCurrent;
        return role == LevelRole.Resistance ? close > upper + buf : close < lower - buf;
    }

    private static double Mid(SrLevel level, int barIndex) =>
        level.Geometry == LevelGeometry.Horizontal
            ? level.Center
            : level.ProjectPriceAtBar(barIndex);

    private static SrLevel? FindLevel(SupportResistanceEngine engine, long id)
    {
        foreach (var l in engine.Levels)
            if (l.Id == id)
                return l;
        return null;
    }

    private static bool CrossesSegment(string segment, int evalEnd, int devEnd, int valEnd)
    {
        if (evalEnd < 0)
            return false;
        return segment switch
        {
            SegmentSplitter.Develop => evalEnd >= devEnd,
            SegmentSplitter.Validate => evalEnd >= valEnd,
            _ => false
        };
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
