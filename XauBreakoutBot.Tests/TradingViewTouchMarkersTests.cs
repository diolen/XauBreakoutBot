using cAlgo.Robots.SupportResistance;
using Xunit;

namespace XauBreakoutBot.Tests;

public class TradingViewTouchMarkersTests
{
    [Fact]
    public void Build_ShowTouchesFalse_ReturnsEmpty()
    {
        var level = LevelWithTouches(70, LevelRole.Resistance, new[]
        {
            (10, 2010.0),
            (40, 2011.0),
            (80, 2009.5)
        });

        var markers = TradingViewTouchMarkers.Build(new[] { level }, showTouches: false);
        Assert.Empty(markers);
    }

    [Fact]
    public void Build_ShowTouchesTrue_EmitsOneMarkerPerIndependentTouch_WithHistoricalTimePrice()
    {
        var t0 = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var level = new SrLevel
        {
            Id = 70,
            Geometry = LevelGeometry.Horizontal,
            Role = LevelRole.Resistance,
            State = LevelState.Active,
            Center = 2010,
            HalfWidth = 1,
            IndependentTouchCount = 3
        };
        level.Touches.Add(new TouchRecord { BarIndex = 10, Time = t0.AddMinutes(15 * 10), Price = 2010.0, IsIndependent = true });
        level.Touches.Add(new TouchRecord { BarIndex = 40, Time = t0.AddMinutes(15 * 40), Price = 2011.0, IsIndependent = true });
        level.Touches.Add(new TouchRecord { BarIndex = 11, Time = t0.AddMinutes(15 * 11), Price = 2010.2, IsIndependent = false });
        level.Touches.Add(new TouchRecord { BarIndex = 80, Time = t0.AddMinutes(15 * 80), Price = 2009.5, IsIndependent = true });

        var markers = TradingViewTouchMarkers.Build(new[] { level }, showTouches: true);

        Assert.Equal(3, markers.Count);
        Assert.Equal(new[] { 1, 2, 3 }, markers.Select(m => m.TouchOrdinal).ToArray());
        Assert.Equal(new[] { 10, 40, 80 }, markers.Select(m => m.BarIndex).ToArray());
        Assert.Equal(new[] { 2010.0, 2011.0, 2009.5 }, markers.Select(m => m.Price).ToArray());
        Assert.Equal(new[] { "#70 T1", "#70 T2", "#70 T3" }, markers.Select(m => m.Label).ToArray());
        Assert.All(markers, m => Assert.Equal(70, m.LevelId));
        Assert.Equal(level.Touches[0].Time, markers[0].Time);
        Assert.Equal(level.Touches[1].Time, markers[1].Time);
        Assert.Equal(level.Touches[3].Time, markers[2].Time);
    }

    [Fact]
    public void Build_OnlySelectedLevels_IgnoresNonSelected()
    {
        var selected = LevelWithTouches(72, LevelRole.Resistance, new[] { (5, 2020.0), (25, 2021.0) });
        var other = LevelWithTouches(99, LevelRole.Support, new[] { (6, 1980.0), (26, 1979.0), (50, 1978.0) });

        // Solo se pasan niveles seleccionados (como hace RenderTradingView).
        var markers = TradingViewTouchMarkers.Build(new[] { selected }, showTouches: true);

        Assert.Equal(2, markers.Count);
        Assert.All(markers, m => Assert.Equal(72, m.LevelId));
        Assert.DoesNotContain(markers, m => m.LevelId == 99);
        // other sigue intacto (sin mutación).
        Assert.Equal(3, other.Touches.Count(t => t.IsIndependent));
        Assert.Equal(3, other.IndependentTouchCount);
    }

    [Fact]
    public void Build_DoesNotMutateTouchRecordsOrCounts()
    {
        var level = LevelWithTouches(74, LevelRole.Resistance, new[]
        {
            (1, 2050.0),
            (20, 2051.0),
            (40, 2049.0),
            (60, 2050.5)
        });
        var beforeCount = level.IndependentTouchCount;
        var beforeTouches = level.Touches.Select(t => (t.BarIndex, t.Time, t.Price, t.IsIndependent)).ToList();

        _ = TradingViewTouchMarkers.Build(new[] { level }, showTouches: true);
        _ = TradingViewTouchMarkers.Build(new[] { level }, showTouches: false);

        Assert.Equal(beforeCount, level.IndependentTouchCount);
        Assert.Equal(
            beforeTouches,
            level.Touches.Select(t => (t.BarIndex, t.Time, t.Price, t.IsIndependent)).ToList());
    }

    [Fact]
    public void Build_ObjectNamesAreUniqueAndPrefixed()
    {
        var a = LevelWithTouches(1, LevelRole.Resistance, new[] { (10, 2010.0) });
        var b = LevelWithTouches(2, LevelRole.Support, new[] { (10, 1990.0) });
        var markers = TradingViewTouchMarkers.Build(new[] { a, b }, showTouches: true);
        Assert.Equal(2, markers.Select(m => m.ObjectName).Distinct().Count());
        Assert.All(markers, m => Assert.StartsWith(TradingViewTouchMarkers.ObjectPrefix, m.ObjectName));
    }

    private static SrLevel LevelWithTouches(long id, LevelRole role, (int Bar, double Price)[] touches)
    {
        var t0 = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var level = new SrLevel
        {
            Id = id,
            Geometry = LevelGeometry.Horizontal,
            Role = role,
            State = LevelState.Active,
            Center = touches[0].Price,
            HalfWidth = 1,
            IndependentTouchCount = touches.Length
        };
        foreach (var (bar, price) in touches)
        {
            level.Touches.Add(new TouchRecord
            {
                BarIndex = bar,
                Time = t0.AddMinutes(15 * bar),
                Price = price,
                IsIndependent = true
            });
        }

        return level;
    }
}
