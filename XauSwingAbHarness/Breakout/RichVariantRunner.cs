using cAlgo.Robots.SupportResistance;
using XauSwingAbHarness.Experiment;

namespace XauSwingAbHarness.Breakout;

/// <summary>
/// Misma semántica que VariantRunner + snapshots geométricos en T=3 + serie ATR por barra.
/// </summary>
public static class RichVariantRunner
{
    public static RichVariantResult Run(
        string variantId,
        int swingK,
        IReadOnlyList<OhlcBar> bars,
        BreakoutExperimentConfig config)
    {
        var abConfig = new AbExperimentConfig
        {
            LookbackBars = config.LookbackBars,
            AtrPeriod = config.AtrPeriod,
            DevelopFraction = config.DevelopFraction,
            ValidateFraction = config.ValidateFraction,
            PredictiveHorizonsBars = new[] { config.ReactionHorizonBars },
            ReactionThresholdAtr = config.ReactionThresholdAtr
        };

        var engine = new SupportResistanceEngine(swingK, config.LookbackBars, config.AtrPeriod);
        var events = new List<ExperimentEvent>(8192);
        var t3Records = new List<T3EventRecord>();
        var atrByBar = new double[bars.Count];
        var prevTouch = new Dictionary<long, int>();
        var prevState = new Dictionary<long, LevelState>();
        var knownLevelIds = new HashSet<long>();
        var seenT3 = new HashSet<long>();
        var seenCandidateEmit = new HashSet<long>();
        var prevSwingCount = 0;
        var levelGeom = new Dictionary<long, LevelGeometrySnapshot>();

        for (var i = 0; i < bars.Count; i++)
        {
            var src = bars[i];
            var bar = new OhlcBar(i, src.OpenTime, src.Open, src.High, src.Low, src.Close);
            engine.ProcessClosedBar(bar);

            var segment = SegmentSplitter.SegmentOf(i, bars.Count, config.DevelopFraction, config.ValidateFraction);
            var atr = engine.AtrReady ? engine.AtrCurrent : double.NaN;
            atrByBar[i] = atr;

            var swings = engine.Swings;
            for (var s = prevSwingCount; s < swings.Count; s++)
            {
                var sw = swings[s];
                events.Add(new ExperimentEvent
                {
                    VariantId = variantId,
                    SwingK = swingK,
                    Kind = EventKind.SwingConfirmed,
                    Observability = Observability.RealtimeObservable,
                    BarIndex = i,
                    OpenTimeUtc = bar.OpenTime,
                    Segment = segment,
                    SwingId = sw.Id,
                    Role = sw.Kind == SwingKind.High ? LevelRole.Resistance : LevelRole.Support,
                    ConfirmationLatencyBars = sw.ConfirmedAtBar - sw.BarIndex,
                    Price = sw.Price,
                    AtrAtEvent = atr,
                    Detail = sw.Kind.ToString()
                });
            }

            prevSwingCount = swings.Count;

            foreach (var level in engine.Levels)
            {
                levelGeom[level.Id] = LevelGeometrySnapshot.FromLevel(level);

                if (knownLevelIds.Add(level.Id))
                {
                    events.Add(new ExperimentEvent
                    {
                        VariantId = variantId,
                        SwingK = swingK,
                        Kind = EventKind.LevelCreated,
                        Observability = Observability.RealtimeObservable,
                        BarIndex = i,
                        OpenTimeUtc = bar.OpenTime,
                        Segment = segment,
                        LevelId = level.Id,
                        Role = level.Role,
                        Geometry = level.Geometry,
                        TouchCount = level.IndependentTouchCount,
                        Price = level.Center,
                        AtrAtEvent = atr,
                        Detail = level.State.ToString()
                    });
                    prevTouch[level.Id] = level.IndependentTouchCount;
                    prevState[level.Id] = level.State;

                    if (level.State == LevelState.Active && level.IndependentTouchCount >= 2)
                    {
                        events.Add(Act(variantId, swingK, i, bar, segment, level, atr));
                        TryCand(events, seenCandidateEmit, variantId, swingK, i, bar, segment, level, atr);
                    }

                    if (level.IndependentTouchCount >= 3 && seenT3.Add(level.Id))
                    {
                        events.Add(T3(variantId, swingK, i, bar, segment, level, atr));
                        t3Records.Add(MakeT3(variantId, swingK, i, bar, segment, level, atr));
                    }

                    continue;
                }

                var oldTouches = prevTouch.GetValueOrDefault(level.Id, 0);
                var oldState = prevState.GetValueOrDefault(level.Id, LevelState.Provisional);

                if (level.IndependentTouchCount > oldTouches)
                {
                    events.Add(new ExperimentEvent
                    {
                        VariantId = variantId,
                        SwingK = swingK,
                        Kind = EventKind.IndependentTouch,
                        Observability = Observability.RealtimeObservable,
                        BarIndex = i,
                        OpenTimeUtc = bar.OpenTime,
                        Segment = segment,
                        LevelId = level.Id,
                        Role = level.Role,
                        Geometry = level.Geometry,
                        TouchCount = level.IndependentTouchCount,
                        Price = level.Center,
                        AtrAtEvent = atr
                    });

                    if (level.IndependentTouchCount >= 3 && seenT3.Add(level.Id))
                    {
                        events.Add(T3(variantId, swingK, i, bar, segment, level, atr));
                        t3Records.Add(MakeT3(variantId, swingK, i, bar, segment, level, atr));
                    }
                }

                if (oldState != LevelState.Active && level.State == LevelState.Active)
                {
                    events.Add(Act(variantId, swingK, i, bar, segment, level, atr));
                    TryCand(events, seenCandidateEmit, variantId, swingK, i, bar, segment, level, atr);
                }

                if (oldState != LevelState.Invalidated && level.State == LevelState.Invalidated)
                {
                    events.Add(new ExperimentEvent
                    {
                        VariantId = variantId,
                        SwingK = swingK,
                        Kind = EventKind.LevelInvalidated,
                        Observability = Observability.RealtimeObservable,
                        BarIndex = i,
                        OpenTimeUtc = bar.OpenTime,
                        Segment = segment,
                        LevelId = level.Id,
                        Role = level.Role,
                        Geometry = level.Geometry,
                        TouchCount = level.IndependentTouchCount,
                        Price = level.Invalidation?.Price ?? level.Center,
                        AtrAtEvent = atr,
                        Detail = level.Invalidation?.RuleTag
                    });

                    foreach (var rec in t3Records)
                    {
                        if (rec.LevelId == level.Id && rec.InvalidationBarIndex == null)
                            rec.InvalidationBarIndex = i;
                    }
                }

                if (EntryCandidateSelector.IsStructuralCandidate(level))
                    TryCand(events, seenCandidateEmit, variantId, swingK, i, bar, segment, level, atr);

                prevTouch[level.Id] = level.IndependentTouchCount;
                prevState[level.Id] = level.State;
            }
        }

        var bas = new VariantRunResult
        {
            VariantId = variantId,
            SwingK = swingK,
            PreloadBars = engine.PreloadBars,
            MinimumPreferredBars = engine.MinimumPreferredBars,
            HasInsufficientHistory = engine.HasInsufficientHistory,
            HistoryWarning = engine.HistoryWarning,
            Events = events,
            FinalSwingCount = engine.Swings.Count,
            FinalLevelCount = engine.Levels.Count,
            FinalActiveCount = engine.Levels.Count(l => l.State == LevelState.Active),
            FinalInvalidatedCount = engine.Levels.Count(l => l.State == LevelState.Invalidated)
        };

        return new RichVariantResult
        {
            Base = bas,
            T3Events = t3Records,
            AtrByBar = atrByBar
        };
    }

    private static T3EventRecord MakeT3(
        string variantId, int swingK, int i, OhlcBar bar, string segment, SrLevel level, double atr) =>
        new()
        {
            VariantId = variantId,
            SwingK = swingK,
            LevelId = level.Id,
            T3BarIndex = i,
            T3OpenTimeUtc = bar.OpenTime,
            Segment = segment,
            Geometry = LevelGeometrySnapshot.FromLevel(level),
            AtrAtT3 = atr,
            CloseAtT3 = bar.Close
        };

    private static ExperimentEvent Act(
        string variantId, int swingK, int i, OhlcBar bar, string segment, SrLevel level, double atr) =>
        new()
        {
            VariantId = variantId,
            SwingK = swingK,
            Kind = EventKind.LevelActivated,
            Observability = Observability.RealtimeObservable,
            BarIndex = i,
            OpenTimeUtc = bar.OpenTime,
            Segment = segment,
            LevelId = level.Id,
            Role = level.Role,
            Geometry = level.Geometry,
            TouchCount = level.IndependentTouchCount,
            Price = level.Center,
            AtrAtEvent = atr
        };

    private static ExperimentEvent T3(
        string variantId, int swingK, int i, OhlcBar bar, string segment, SrLevel level, double atr) =>
        new()
        {
            VariantId = variantId,
            SwingK = swingK,
            Kind = EventKind.TouchCountReached3,
            Observability = Observability.RealtimeObservable,
            BarIndex = i,
            OpenTimeUtc = bar.OpenTime,
            Segment = segment,
            LevelId = level.Id,
            Role = level.Role,
            Geometry = level.Geometry,
            TouchCount = level.IndependentTouchCount,
            Price = level.Center,
            AtrAtEvent = atr,
            Detail = "T>=3-not-a-trade"
        };

    private static void TryCand(
        List<ExperimentEvent> events,
        HashSet<long> seen,
        string variantId,
        int swingK,
        int i,
        OhlcBar bar,
        string segment,
        SrLevel level,
        double atr)
    {
        if (!EntryCandidateSelector.IsStructuralCandidate(level))
            return;
        if (!seen.Add(level.Id))
            return;
        events.Add(new ExperimentEvent
        {
            VariantId = variantId,
            SwingK = swingK,
            Kind = EventKind.EntryCandidateT2,
            Observability = Observability.RealtimeObservable,
            BarIndex = i,
            OpenTimeUtc = bar.OpenTime,
            Segment = segment,
            LevelId = level.Id,
            Role = level.Role,
            Geometry = level.Geometry,
            TouchCount = level.IndependentTouchCount,
            Price = level.Center,
            AtrAtEvent = atr,
            Detail = "structural-T2-not-a-trade"
        });
    }
}
