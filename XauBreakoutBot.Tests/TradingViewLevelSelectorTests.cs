using cAlgo.Robots.SupportResistance;
using Xunit;

namespace XauBreakoutBot.Tests;

public class TradingViewLevelSelectorTests
{
    [Fact]
    public void Select_TakesUpToThreeNearestActivePerSide_MixingHorizontalAndDiagonal()
    {
        var levels = new List<SrLevel>
        {
            ActiveH(1, LevelRole.Resistance, 2010),
            ActiveH(2, LevelRole.Resistance, 2020),
            ActiveD(3, LevelRole.Resistance, slope: 0, priceAtBar0: 2030),
            ActiveH(4, LevelRole.Resistance, 2040), // 4º → excluido
            ActiveH(5, LevelRole.Support, 1990),
            ActiveD(6, LevelRole.Support, slope: 0, priceAtBar0: 1980),
            ActiveH(7, LevelRole.Support, 1970),
            ActiveH(8, LevelRole.Support, 1960), // 4º → excluido
            ProvisionalH(9, LevelRole.Resistance, 2005),
            InvalidatedH(10, LevelRole.Support, 1995)
        };

        var selected = TradingViewLevelSelector.Select(levels, bid: 2000, ask: 2000.1, referenceBarIndex: 0);

        Assert.Equal(6, selected.Count);
        Assert.Equal(3, selected.Count(l => l.Role == LevelRole.Resistance));
        Assert.Equal(3, selected.Count(l => l.Role == LevelRole.Support));
        Assert.All(selected, l => Assert.Equal(LevelState.Active, l.State));
        Assert.DoesNotContain(selected, l => l.Id is 4 or 8 or 9 or 10);

        var resIds = selected.Where(l => l.Role == LevelRole.Resistance).Select(l => l.Id).ToArray();
        Assert.Equal(new long[] { 1, 2, 3 }, resIds);
        var supIds = selected.Where(l => l.Role == LevelRole.Support).Select(l => l.Id).ToArray();
        Assert.Equal(new long[] { 5, 6, 7 }, supIds);
    }

    [Fact]
    public void Select_TieBreak_UsesOldestId()
    {
        var levels = new List<SrLevel>
        {
            ActiveH(20, LevelRole.Resistance, 2010),
            ActiveH(5, LevelRole.Resistance, 2010),
            ActiveH(8, LevelRole.Resistance, 2010)
        };

        var selected = TradingViewLevelSelector.Select(levels, bid: 2000, ask: 2000, referenceBarIndex: 0);
        Assert.Equal(3, selected.Count);
        Assert.Equal(new long[] { 5, 8, 20 }, selected.Select(l => l.Id).ToArray());
    }

    [Fact]
    public void Select_DoesNotMutateEngineLevels()
    {
        var engine = new SupportResistanceEngine();
        var bars = BuildSyntheticXauLike(120, seed: 7);
        engine.ReplayClosedBars(bars, enforceLookbackWindow: true);
        engine.ProcessTick(bars[^1].OpenTime.AddMinutes(1), bid: bars[^1].Close - 0.2, ask: bars[^1].Close + 0.2);

        var beforeCount = engine.Levels.Count;
        var beforeStates = engine.Levels.Select(l => (l.Id, l.State, l.IndependentTouchCount, l.Center, l.HalfWidth, l.SlopePerBar)).ToList();
        var beforeTouches = engine.Levels.Sum(l => l.Touches.Count);
        var beforeActive = engine.Levels.Count(l => l.State == LevelState.Active);

        var barIndex = engine.ClosedBarCount - 1;
        var selected = TradingViewLevelSelector.Select(
            engine.Levels, bars[^1].Close - 0.2, bars[^1].Close + 0.2, barIndex);

        Assert.True(selected.Count <= TradingViewLevelSelector.MaxPerSide * 2);
        Assert.Equal(beforeCount, engine.Levels.Count);
        Assert.Equal(beforeActive, engine.Levels.Count(l => l.State == LevelState.Active));
        Assert.Equal(beforeTouches, engine.Levels.Sum(l => l.Touches.Count));
        Assert.Equal(
            beforeStates,
            engine.Levels.Select(l => (l.Id, l.State, l.IndependentTouchCount, l.Center, l.HalfWidth, l.SlopePerBar)).ToList());
    }

    [Fact]
    public void Select_IgnoresResistanceBelowAsk_AndSupportAboveBid()
    {
        var levels = new List<SrLevel>
        {
            ActiveH(1, LevelRole.Resistance, 1990), // below ask → ignore
            ActiveH(2, LevelRole.Resistance, 2010),
            ActiveH(3, LevelRole.Support, 2010), // above bid → ignore
            ActiveH(4, LevelRole.Support, 1990)
        };

        var selected = TradingViewLevelSelector.Select(levels, bid: 2000, ask: 2000, referenceBarIndex: 0);
        Assert.Equal(2, selected.Count);
        Assert.Contains(selected, l => l.Id == 2);
        Assert.Contains(selected, l => l.Id == 4);
    }

    [Fact]
    public void SameIdSet_DetectsSelectionChanges()
    {
        var a = new long[] { 1, 2, 3 };
        var b = new long[] { 1, 2, 3 };
        var c = new long[] { 1, 2, 4 };
        Assert.True(TradingViewLevelSelector.SameIdSet(a, b));
        Assert.False(TradingViewLevelSelector.SameIdSet(a, c));
        Assert.False(TradingViewLevelSelector.SameIdSet(a, null));
    }

    private static SrLevel ActiveH(long id, LevelRole role, double center) => new()
    {
        Id = id,
        Geometry = LevelGeometry.Horizontal,
        Role = role,
        State = LevelState.Active,
        Center = center,
        HalfWidth = 1,
        AtrRef = 10,
        IndependentTouchCount = 2
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
        IndependentTouchCount = 2
    };

    private static SrLevel ActiveD(long id, LevelRole role, double slope, double priceAtBar0)
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
            IndependentTouchCount = 2
        };
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        level.Anchors.Add(new AnchorPoint { BarIndex = 0, Time = t0, Price = priceAtBar0 });
        level.Anchors.Add(new AnchorPoint { BarIndex = 20, Time = t0.AddMinutes(15 * 20), Price = priceAtBar0 + slope * 20 });
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
