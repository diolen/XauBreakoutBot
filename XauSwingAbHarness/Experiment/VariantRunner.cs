using cAlgo.Robots.SupportResistance;

namespace XauSwingAbHarness.Experiment;

public sealed class VariantRunResult
{
    public required string VariantId { get; init; }
    public required int SwingK { get; init; }
    public required int PreloadBars { get; init; }
    public required int MinimumPreferredBars { get; init; }
    public required bool HasInsufficientHistory { get; init; }
    public required string? HistoryWarning { get; init; }
    public required IReadOnlyList<ExperimentEvent> Events { get; init; }
    public required int FinalSwingCount { get; init; }
    public required int FinalLevelCount { get; init; }
    public required int FinalActiveCount { get; init; }
    public required int FinalInvalidatedCount { get; init; }
}

/// <summary>
/// Ejecuta una variante sobre el historial completo con ProcessClosedBar secuencial.
/// No usa ReplayClosedBars(enforceLookbackWindow:true) para no truncar el dataset.
/// </summary>
public static class VariantRunner
{
    public static VariantRunResult Run(
        string variantId,
        int swingK,
        IReadOnlyList<OhlcBar> bars,
        AbExperimentConfig config)
    {
        var engine = new SupportResistanceEngine(
            swingStrength: swingK,
            lookbackBars: config.LookbackBars,
            atrPeriod: config.AtrPeriod);

        var events = new List<ExperimentEvent>(capacity: 8192);
        var prevTouch = new Dictionary<long, int>();
        var prevState = new Dictionary<long, LevelState>();
        var knownLevelIds = new HashSet<long>();
        var seenT3 = new HashSet<long>();
        var seenCandidateEmit = new HashSet<long>();
        var prevSwingCount = 0;

        for (var i = 0; i < bars.Count; i++)
        {
            var src = bars[i];
            var bar = new OhlcBar(i, src.OpenTime, src.Open, src.High, src.Low, src.Close);
            engine.ProcessClosedBar(bar);

            var segment = SegmentSplitter.SegmentOf(
                i, bars.Count, config.DevelopFraction, config.ValidateFraction);
            var atr = engine.AtrReady ? engine.AtrCurrent : double.NaN;

            // Swings nuevos (confirmados en esta barra).
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

            // Niveles: creación, activación, toques, T3, invalidación.
            foreach (var level in engine.Levels)
            {
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

                    // Diagonales nacen Active con T=2.
                    if (level.State == LevelState.Active && level.IndependentTouchCount >= 2)
                    {
                        events.Add(MakeActivation(variantId, swingK, i, bar, segment, level, atr));
                        TryEmitCandidate(events, seenCandidateEmit, variantId, swingK, i, bar, segment, level, atr);
                    }

                    if (level.IndependentTouchCount >= 3 && seenT3.Add(level.Id))
                        events.Add(MakeT3(variantId, swingK, i, bar, segment, level, atr));

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
                        events.Add(MakeT3(variantId, swingK, i, bar, segment, level, atr));
                }

                if (oldState != LevelState.Active && level.State == LevelState.Active)
                {
                    events.Add(MakeActivation(variantId, swingK, i, bar, segment, level, atr));
                    TryEmitCandidate(events, seenCandidateEmit, variantId, swingK, i, bar, segment, level, atr);
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
                }

                // Candidato T=2: Active con exactamente 2 toques (selector estructural).
                if (EntryCandidateSelector.IsStructuralCandidate(level))
                    TryEmitCandidate(events, seenCandidateEmit, variantId, swingK, i, bar, segment, level, atr);

                prevTouch[level.Id] = level.IndependentTouchCount;
                prevState[level.Id] = level.State;
            }
        }

        return new VariantRunResult
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
    }

    private static void TryEmitCandidate(
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

    private static ExperimentEvent MakeActivation(
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

    private static ExperimentEvent MakeT3(
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
}
