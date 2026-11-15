using cAlgo.Robots.SupportResistance;
using Xunit;

namespace XauBreakoutBot.Tests;

public class EntryCandidateSelectorTests
{
    [Fact]
    public void Active_T2_OnCorrectSide_IsCandidate()
    {
        var levels = new List<SrLevel>
        {
            ActiveH(1, LevelRole.Resistance, 2010, touches: 2),
            ActiveH(2, LevelRole.Support, 1990, touches: 2)
        };

        var snap = EntryCandidateSelector.Select(levels, bid: 2000, ask: 2000.1, referenceBarIndex: 0);

        Assert.Single(snap.LongCandidates);
        Assert.Equal(1, snap.LongCandidates[0].Id);
        Assert.Single(snap.ShortCandidates);
        Assert.Equal(2, snap.ShortCandidates[0].Id);
        Assert.Equal(2, snap.Count);
    }

    [Fact]
    public void Active_T3_IsNotCandidate()
    {
        var levels = new List<SrLevel>
        {
            ActiveH(1, LevelRole.Resistance, 2010, touches: 3),
            ActiveH(2, LevelRole.Support, 1990, touches: 4)
        };

        var snap = EntryCandidateSelector.Select(levels, bid: 2000, ask: 2000, referenceBarIndex: 0);

        Assert.True(snap.IsEmpty);
        Assert.Empty(snap.LongCandidates);
        Assert.Empty(snap.ShortCandidates);
    }

    [Fact]
    public void Provisional_IsNotCandidate()
    {
        var levels = new List<SrLevel>
        {
            ProvisionalH(1, LevelRole.Resistance, 2010),
            ProvisionalH(2, LevelRole.Support, 1990)
        };

        var snap = EntryCandidateSelector.Select(levels, bid: 2000, ask: 2000, referenceBarIndex: 0);
        Assert.True(snap.IsEmpty);
    }

    [Fact]
    public void Invalidated_IsNotCandidate()
    {
        var levels = new List<SrLevel>
        {
            InvalidatedH(1, LevelRole.Resistance, 2010),
            InvalidatedH(2, LevelRole.Support, 1990)
        };

        var snap = EntryCandidateSelector.Select(levels, bid: 2000, ask: 2000, referenceBarIndex: 0);
        Assert.True(snap.IsEmpty);
    }

    [Fact]
    public void ResistanceBelowAsk_IsNotLongCandidate()
    {
        var levels = new List<SrLevel>
        {
            ActiveH(1, LevelRole.Resistance, 1990, touches: 2), // below ask
            ActiveH(2, LevelRole.Resistance, 2010, touches: 2)
        };

        var snap = EntryCandidateSelector.Select(levels, bid: 2000, ask: 2000, referenceBarIndex: 0);

        Assert.Single(snap.LongCandidates);
        Assert.Equal(2, snap.LongCandidates[0].Id);
        Assert.DoesNotContain(snap.LongCandidates, l => l.Id == 1);
    }

    [Fact]
    public void SupportAboveBid_IsNotShortCandidate()
    {
        var levels = new List<SrLevel>
        {
            ActiveH(1, LevelRole.Support, 2010, touches: 2), // above bid
            ActiveH(2, LevelRole.Support, 1990, touches: 2)
        };

        var snap = EntryCandidateSelector.Select(levels, bid: 2000, ask: 2000, referenceBarIndex: 0);

        Assert.Single(snap.ShortCandidates);
        Assert.Equal(2, snap.ShortCandidates[0].Id);
        Assert.DoesNotContain(snap.ShortCandidates, l => l.Id == 1);
    }

    [Fact]
    public void HorizontalAndDiagonal_WorkTheSame()
    {
        var levels = new List<SrLevel>
        {
            ActiveH(1, LevelRole.Resistance, 2010, touches: 2),
            ActiveD(2, LevelRole.Resistance, slope: 0, priceAtBar0: 2020, touches: 2),
            ActiveH(3, LevelRole.Support, 1990, touches: 2),
            ActiveD(4, LevelRole.Support, slope: 0, priceAtBar0: 1980, touches: 2),
            ActiveD(5, LevelRole.Resistance, slope: 0, priceAtBar0: 1995, touches: 2), // below ask
            ActiveD(6, LevelRole.Support, slope: 0, priceAtBar0: 2005, touches: 2), // above bid
            ActiveD(7, LevelRole.Resistance, slope: 0, priceAtBar0: 2030, touches: 3) // T=3
        };

        var snap = EntryCandidateSelector.Select(levels, bid: 2000, ask: 2000.1, referenceBarIndex: 0);

        Assert.Equal(2, snap.LongCandidates.Count);
        Assert.Equal(new long[] { 1, 2 }, snap.LongCandidates.Select(l => l.Id).ToArray());
        Assert.Equal(2, snap.ShortCandidates.Count);
        Assert.Equal(new long[] { 3, 4 }, snap.ShortCandidates.Select(l => l.Id).ToArray());
        Assert.DoesNotContain(snap.All(), l => l.Id is 5 or 6 or 7);
    }

    [Fact]
    public void Select_DoesNotMutateLevelsOrCounters()
    {
        var engine = new SupportResistanceEngine();
        var bars = BuildSyntheticXauLike(120, seed: 11);
        engine.ReplayClosedBars(bars, enforceLookbackWindow: true);
        engine.ProcessTick(bars[^1].OpenTime.AddMinutes(1), bid: bars[^1].Close - 0.2, ask: bars[^1].Close + 0.2);

        var beforeCount = engine.Levels.Count;
        var beforeStates = engine.Levels
            .Select(l => (l.Id, l.State, l.IndependentTouchCount, l.Center, l.HalfWidth, l.SlopePerBar, l.Invalidation))
            .ToList();
        var beforeTouches = engine.Levels.Sum(l => l.Touches.Count);
        var beforeActive = engine.Levels.Count(l => l.State == LevelState.Active);
        var beforeEngineCandidates = (
            engine.Candidates.NearestResistanceAboveAsk?.Id,
            engine.Candidates.NearestSupportBelowBid?.Id);

        var barIndex = engine.ClosedBarCount - 1;
        var snap = EntryCandidateSelector.Select(
            engine.Levels, bars[^1].Close - 0.2, bars[^1].Close + 0.2, barIndex);

        Assert.True(snap.Count >= 0);
        Assert.Equal(beforeCount, engine.Levels.Count);
        Assert.Equal(beforeActive, engine.Levels.Count(l => l.State == LevelState.Active));
        Assert.Equal(beforeTouches, engine.Levels.Sum(l => l.Touches.Count));
        Assert.Equal(
            beforeStates,
            engine.Levels
                .Select(l => (l.Id, l.State, l.IndependentTouchCount, l.Center, l.HalfWidth, l.SlopePerBar, l.Invalidation))
                .ToList());
        Assert.Equal(
            beforeEngineCandidates,
            (engine.Candidates.NearestResistanceAboveAsk?.Id, engine.Candidates.NearestSupportBelowBid?.Id));
    }

    [Fact]
    public void EmptySnapshot_ExposesInformationalMessageConstant()
    {
        Assert.False(string.IsNullOrWhiteSpace(EntryCandidateSelector.EmptyMessage));
        Assert.Contains("No entry candidates", EntryCandidateSelector.EmptyMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveWithInvalidationInfo_IsNotCandidate()
    {
        var level = ActiveH(1, LevelRole.Resistance, 2010, touches: 2);
        level.Invalidation = new InvalidationInfo
        {
            Time = DateTime.UtcNow,
            Price = 2015,
            RuleTag = "synthetic",
            BreakoutBuffer = 1
        };
        // Estado Active con Invalidation no debería ocurrir en el motor; la capa lo excluye.
        var snap = EntryCandidateSelector.Select(new[] { level }, bid: 2000, ask: 2000, referenceBarIndex: 0);
        Assert.True(snap.IsEmpty);
    }

    private static SrLevel ActiveH(long id, LevelRole role, double center, int touches) => new()
    {
        Id = id,
        Geometry = LevelGeometry.Horizontal,
        Role = role,
        State = LevelState.Active,
        Center = center,
        HalfWidth = 1,
        AtrRef = 10,
        IndependentTouchCount = touches
    };

    private static SrLevel ProvisionalH(long id, LevelRole role, double center) => new()
    {
        Id = id,
        Geometry = LevelGeometry.Horizontal,
        Role = role,
        State = LevelState.Provisional,
        Center = center,
        HalfWidth = 1,
        IndependentTouchCount = 1
    };

    private static SrLevel InvalidatedH(long id, LevelRole role, double center) => new()
    {
        Id = id,
        Geometry = LevelGeometry.Horizontal,
        Role = role,
        State = LevelState.Invalidated,
        Center = center,
        HalfWidth = 1,
        IndependentTouchCount = 2,
        Invalidation = new InvalidationInfo
        {
            Time = DateTime.UtcNow,
            Price = center,
            RuleTag = "test",
            BreakoutBuffer = 1
        }
    };

    private static SrLevel ActiveD(long id, LevelRole role, double slope, double priceAtBar0, int touches)
    {
        var level = new SrLevel
        {
            Id = id,
            Geometry = LevelGeometry.Diagonal,
            Role = role,
            State = LevelState.Active,
            Center = priceAtBar0,
            HalfWidth = 1,
            AtrRef = 10,
            SlopePerBar = slope,
            IndependentTouchCount = touches
        };
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        level.Anchors.Add(new AnchorPoint { BarIndex = 0, Time = t0, Price = priceAtBar0 });
        level.Anchors.Add(new AnchorPoint
        {
            BarIndex = 20,
            Time = t0.AddMinutes(15 * 20),
            Price = priceAtBar0 + slope * 20
        });
        return level;
    }

    private static List<OhlcBar> BuildSyntheticXauLike(int n, int seed)
    {
        var rng = new Random(seed);
        var bars = new List<OhlcBar>(n);
        var t0 = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        double price = 2350;
        for (var i = 0; i < n; i++)
        {
            var drift = Math.Sin(i / 37.0) * 0.15 + Math.Sin(i / 91.0) * 0.08;
            var shock = (rng.NextDouble() - 0.5) * 3.2;
            var open = price;
            var close = price + drift + shock;
            bars.Add(new OhlcBar(
                i,
                t0.AddMinutes(15 * i),
                open,
                Math.Max(open, close) + rng.NextDouble() * 2.5,
                Math.Min(open, close) - rng.NextDouble() * 2.5,
                close));
            price = close;
        }

        return bars;
    }
}
