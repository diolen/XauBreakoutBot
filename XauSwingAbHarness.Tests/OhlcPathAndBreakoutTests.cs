using cAlgo.Robots.SupportResistance;
using XauSwingAbHarness.Breakout;
using XauSwingAbHarness.Dataset;
using XauSwingAbHarness.Experiment;
using XauSwingAbHarness.Metrics;
using Xunit;

namespace XauSwingAbHarness.Tests;

public class OhlcPathAndBreakoutTests
{
    private static readonly BreakoutExperimentConfig Cfg = new()
    {
        ReactionHorizonBars = 16,
        ReactionThresholdAtr = 0.5,
        ConfirmationBufferAtr = 0.15,
        MaxBarsToConfirm = 8
    };

    [Fact]
    public void SameBarBothThresholds_PoliciesDiverge()
    {
        // entry=100, atr=2 → thr=1; High=102 (+2 fav), Low=98 (−2 adv) same bar
        var path = new List<(double, double)> { (102, 98) };
        var opt = OhlcPathEvaluator.Evaluate(path, 100, 2, 0.5, resistanceExpectsUp: true, OhlcAmbiguityPolicy.Optimistic);
        var cons = OhlcPathEvaluator.Evaluate(path, 100, 2, 0.5, resistanceExpectsUp: true, OhlcAmbiguityPolicy.Conservative);
        var excl = OhlcPathEvaluator.Evaluate(path, 100, 2, 0.5, resistanceExpectsUp: true, OhlcAmbiguityPolicy.ExcludeAmbiguous);

        Assert.Equal(ReactionOutcome.Hit, opt.Outcome);
        Assert.Equal(ReactionOutcome.Miss, cons.Outcome);
        Assert.Equal(ReactionOutcome.AmbiguousSameBar, excl.Outcome);
        Assert.True(opt.SameBarBothThresholds);
    }

    [Fact]
    public void FavorableBeforeAdverse_IsHit()
    {
        var path = new List<(double, double)>
        {
            (100.5, 99.8), // neither
            (102, 99.5),   // fav first (+2 >= 1)
            (103, 90)      // later adverse unused
        };
        var r = OhlcPathEvaluator.Evaluate(path, 100, 2, 0.5, true, OhlcAmbiguityPolicy.Optimistic);
        Assert.Equal(ReactionOutcome.Hit, r.Outcome);
        Assert.Equal(1, r.ResolutionBarOffset);
    }

    [Fact]
    public void AdverseBeforeFavorable_IsMiss()
    {
        var path = new List<(double, double)>
        {
            (100.2, 98), // adv −2 >= 1
            (110, 97)
        };
        var r = OhlcPathEvaluator.Evaluate(path, 100, 2, 0.5, true, OhlcAmbiguityPolicy.Optimistic);
        Assert.Equal(ReactionOutcome.Miss, r.Outcome);
    }

    [Fact]
    public void NoThreshold_IsUnresolved_NotMiss()
    {
        var path = new List<(double, double)> { (100.2, 99.8), (100.3, 99.7) };
        var r = OhlcPathEvaluator.Evaluate(path, 100, 2, 0.5, true, OhlcAmbiguityPolicy.Optimistic);
        Assert.Equal(ReactionOutcome.Unresolved, r.Outcome);
    }

    [Fact]
    public void TruncatedHorizon_StillEvaluatesAvailableBars()
    {
        var path = new List<(double, double)> { (100.2, 99.9) }; // short path
        var r = OhlcPathEvaluator.Evaluate(path, 100, 2, 0.5, true, OhlcAmbiguityPolicy.ExcludeAmbiguous);
        Assert.Equal(ReactionOutcome.Unresolved, r.Outcome);
    }

    [Fact]
    public void ResistanceConfirmation_RequiresCloseAboveLevelPlusBuffer()
    {
        var t0 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<OhlcBar>
        {
            new(0, t0, 100, 101, 99, 100), // T3
            new(1, t0.AddMinutes(15), 100, 101, 99, 100.1), // not enough
            new(2, t0.AddMinutes(30), 100, 102, 99, 101.5) // close > 100 + 0.15*10=101.5? need >
        };
        // atr=10, buffer=1.5, level=100 → need close > 101.5
        bars[2] = new OhlcBar(2, t0.AddMinutes(30), 100, 103, 99, 101.6);

        var t3 = MakeT3(0, LevelRole.Resistance, LevelGeometry.Horizontal, 100, 10, bars[0].Close);
        var conf = BreakoutConfirmation.TryConfirm(t3, bars, Cfg);
        Assert.Equal(BreakoutConfirmStatus.Confirmed, conf.Status);
        Assert.Equal(2, conf.ConfirmationBarIndex);
        Assert.Equal(2, conf.BarsToConfirm);
        Assert.Equal("Up", conf.BreakoutDirection);
    }

    [Fact]
    public void SupportConfirmation_RequiresCloseBelowLevelMinusBuffer()
    {
        var t0 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<OhlcBar>
        {
            new(0, t0, 100, 101, 99, 100),
            new(1, t0.AddMinutes(15), 100, 101, 98, 98.4) // close < 100 - 1.5 = 98.5
        };
        var t3 = MakeT3(0, LevelRole.Support, LevelGeometry.Horizontal, 100, 10, 100);
        var conf = BreakoutConfirmation.TryConfirm(t3, bars, Cfg);
        Assert.Equal(BreakoutConfirmStatus.Confirmed, conf.Status);
        Assert.Equal("Down", conf.BreakoutDirection);
    }

    [Fact]
    public void WickBeyondLevel_WithoutClose_DoesNotConfirm()
    {
        var t0 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<OhlcBar>
        {
            new(0, t0, 100, 101, 99, 100),
            new(1, t0.AddMinutes(15), 100, 105, 99, 100.5) // high breaks, close does not
        };
        var t3 = MakeT3(0, LevelRole.Resistance, LevelGeometry.Horizontal, 100, 10, 100);
        var conf = BreakoutConfirmation.TryConfirm(t3, bars, Cfg);
        Assert.Equal(BreakoutConfirmStatus.NotConfirmedInWindow, conf.Status);
    }

    [Fact]
    public void ConfirmationOutside8Bars_NotConfirmed()
    {
        var t0 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<OhlcBar> { new(0, t0, 100, 101, 99, 100) };
        for (var i = 1; i <= 10; i++)
        {
            var close = i == 9 ? 102.0 : 100.0; // confirm would be at bar 9 (>8)
            bars.Add(new OhlcBar(i, t0.AddMinutes(15 * i), 100, Math.Max(101, close), 99, close));
        }

        var t3 = MakeT3(0, LevelRole.Resistance, LevelGeometry.Horizontal, 100, 10, 100);
        var conf = BreakoutConfirmation.TryConfirm(t3, bars, Cfg);
        Assert.Equal(BreakoutConfirmStatus.NotConfirmedInWindow, conf.Status);
    }

    [Fact]
    public void AtrSnapshot_DoesNotUseFutureBars()
    {
        // Path evaluator ATR is caller-supplied; confirmation buffer uses AtrAtT3 only.
        var t0 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<OhlcBar>
        {
            new(0, t0, 100, 101, 99, 100),
            new(1, t0.AddMinutes(15), 100, 120, 80, 101.6)
        };
        var atrByBar = new[] { 10.0, 999.0 }; // future inflated ATR must not change buffer (uses T3 atr=10)
        var t3 = MakeT3(0, LevelRole.Resistance, LevelGeometry.Horizontal, 100, 10, 100);
        var conf = BreakoutConfirmation.TryConfirm(t3, bars, Cfg, atrByBar);
        Assert.Equal(BreakoutConfirmStatus.Confirmed, conf.Status);
        // Buffer was 1.5 from atr=10; if atr=999 were used, threshold would differ — close 101.6 still works for 10.
        Assert.Equal(999.0, conf.AtrAtConfirmation); // snapshot at confirm bar for post-eval ATR
    }

    [Fact]
    public void GapExclusion_S1_DetectsResumeWindow()
    {
        var times = Enumerable.Range(0, 40)
            .Select(i => new DateTime(2025, 1, 6, 0, 0, 0, DateTimeKind.Utc).AddMinutes(15 * i))
            .ToList();
        // artificial gap before index 10
        var gaps = new List<TemporalGap>
        {
            new(times[9], times[10], TimeSpan.FromHours(2), GapClass.ShortGapUnknown)
        };
        var rec = new T3EventRecord
        {
            VariantId = "T",
            SwingK = 3,
            LevelId = 1,
            T3BarIndex = 12,
            T3OpenTimeUtc = times[12],
            Segment = SegmentSplitter.Develop,
            Geometry = LevelGeometrySnapshot.FromLevel(new SrLevel
            {
                Id = 1,
                Geometry = LevelGeometry.Horizontal,
                Role = LevelRole.Resistance,
                Center = 100,
                State = LevelState.Active
            }),
            AtrAtT3 = 1,
            CloseAtT3 = 100
        };
        Assert.True(EventFilters.IsExcludedByGap(rec, gaps, times, 16, restrictedClassesOnly: false));
        Assert.False(EventFilters.IsExcludedByGap(rec, gaps, times, 16, restrictedClassesOnly: true));
    }

    [Fact]
    public void Decimation_EnforcesMinGap()
    {
        var list = new List<T3EventRecord>();
        for (var i = 0; i < 5; i++)
        {
            list.Add(new T3EventRecord
            {
                VariantId = "T",
                SwingK = 3,
                LevelId = i + 1,
                T3BarIndex = i * 5,
                T3OpenTimeUtc = DateTime.UtcNow,
                Segment = SegmentSplitter.Develop,
                Geometry = LevelGeometrySnapshot.FromLevel(new SrLevel
                {
                    Id = i + 1,
                    Geometry = LevelGeometry.Horizontal,
                    Role = LevelRole.Resistance,
                    Center = 100,
                    State = LevelState.Active
                }),
                AtrAtT3 = 1,
                CloseAtT3 = 100
            });
        }

        var kept = EventFilters.DecimateByMinGap(list, 16);
        Assert.True(kept.Count < list.Count);
        for (var i = 1; i < kept.Count; i++)
            Assert.True(kept[i].T3BarIndex - kept[i - 1].T3BarIndex > 16);
    }

    [Fact]
    public void AggregateCounts_AreConsistent()
    {
        var pathAmb = new List<(double, double)> { (102, 98) };
        var pathHit = new List<(double, double)> { (102, 99.5) };
        var pathMiss = new List<(double, double)> { (100.2, 98) };
        var pathUnres = new List<(double, double)> { (100.1, 99.9) };

        var rows = new[]
        {
            OhlcPathEvaluator.Evaluate(pathAmb, 100, 2, 0.5, true, OhlcAmbiguityPolicy.ExcludeAmbiguous),
            OhlcPathEvaluator.Evaluate(pathHit, 100, 2, 0.5, true, OhlcAmbiguityPolicy.ExcludeAmbiguous),
            OhlcPathEvaluator.Evaluate(pathMiss, 100, 2, 0.5, true, OhlcAmbiguityPolicy.ExcludeAmbiguous),
            OhlcPathEvaluator.Evaluate(pathUnres, 100, 2, 0.5, true, OhlcAmbiguityPolicy.ExcludeAmbiguous)
        };
        var agg = PolicyAggregateBuilder.Build("T", 3, "global", "test", OhlcAmbiguityPolicy.ExcludeAmbiguous, 16, rows);
        Assert.Equal(1, agg.Hits);
        Assert.Equal(1, agg.Misses);
        Assert.Equal(1, agg.AmbiguousSameBar);
        Assert.Equal(1, agg.Unresolved);
        Assert.Equal(3, agg.Evaluable); // amb excluded
        Assert.Equal(1 / 3.0, agg.HitRateReported, 6);
    }

    private static T3EventRecord MakeT3(
        int barIndex, LevelRole role, LevelGeometry geom, double center, double atr, double close) =>
        new()
        {
            VariantId = "T",
            SwingK = 3,
            LevelId = 1,
            T3BarIndex = barIndex,
            T3OpenTimeUtc = DateTime.UtcNow,
            Segment = SegmentSplitter.Develop,
            Geometry = LevelGeometrySnapshot.FromLevel(new SrLevel
            {
                Id = 1,
                Geometry = geom,
                Role = role,
                Center = center,
                State = LevelState.Active,
                SlopePerBar = 0
            }),
            AtrAtT3 = atr,
            CloseAtT3 = close
        };
}
