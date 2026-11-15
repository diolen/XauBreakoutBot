using cAlgo.Robots.SupportResistance;
using Xunit;

namespace XauBreakoutBot.Tests;

public class ChartVisibleLabelAnchorTests
{
    [Fact]
    public void ResolveLabelBarIndex_PlacesInsideVisibleRange_InsetFromRight()
    {
        // Viewport M15 típico: muchas barras; la etiqueta no debe ir al extremo absoluto.
        var idx = ChartVisibleLabelAnchor.ResolveLabelBarIndex(100, 200, insetFromRight: 0.12);
        Assert.InRange(idx, 100, 200);
        Assert.True(idx < 200, "debe quedar inset desde el borde derecho");
        Assert.True(idx > 100);
        Assert.Equal(200 - (int)Math.Round(100 * 0.12), idx);
    }

    [Fact]
    public void ResolveLabelBarIndex_WorksForHourChartViewport()
    {
        // En H1 el span visible es menor; igual debe quedar dentro.
        var idx = ChartVisibleLabelAnchor.ResolveLabelBarIndex(10, 40, insetFromRight: 0.12);
        Assert.InRange(idx, 10, 40);
        Assert.Equal(40 - (int)Math.Round(30 * 0.12), idx);
    }

    [Fact]
    public void ResolveLabelBarIndex_SingleBarViewport_UsesThatBar()
    {
        Assert.Equal(55, ChartVisibleLabelAnchor.ResolveLabelBarIndex(55, 55));
    }

    [Fact]
    public void ResolveLabelBarIndex_InvertedRange_ReturnsNonNegativeLast()
    {
        Assert.Equal(3, ChartVisibleLabelAnchor.ResolveLabelBarIndex(10, 3));
    }

    [Fact]
    public void ResolveLabelTime_UsesVisibleWindow_NotM15FallbackWhenAvailable()
    {
        var start = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(5);
        var fallback = end.AddMinutes(15); // antiguo anclaje M15 (borde / fuera)

        var label = ChartVisibleLabelAnchor.ResolveLabelTime(start, end, fallback, insetFromRight: 0.12);

        Assert.True(label >= start && label <= end);
        Assert.NotEqual(fallback, label);
        Assert.True(label < end);
    }

    [Fact]
    public void ResolveLabelTime_FallsBackWhenVisibleMissing()
    {
        var fallback = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(fallback, ChartVisibleLabelAnchor.ResolveLabelTime(null, null, fallback));
        Assert.Equal(fallback, ChartVisibleLabelAnchor.ResolveLabelTime(fallback, null, fallback));
    }

    [Fact]
    public void ResolveLineEnd_ExtendsToVisibleIfLater()
    {
        var dataEnd = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var visibleEnd = dataEnd.AddHours(1);
        Assert.Equal(visibleEnd, ChartVisibleLabelAnchor.ResolveLineEnd(dataEnd, visibleEnd));
        Assert.Equal(dataEnd, ChartVisibleLabelAnchor.ResolveLineEnd(dataEnd, dataEnd.AddHours(-1)));
        Assert.Equal(dataEnd, ChartVisibleLabelAnchor.ResolveLineEnd(dataEnd, null));
    }

    [Fact]
    public void IsUsableVisibleRange_RejectsCloudStyleZerosOnEmptyMeaning()
    {
        Assert.True(ChartVisibleLabelAnchor.IsUsableVisibleRange(0, 100, 200));
        Assert.True(ChartVisibleLabelAnchor.IsUsableVisibleRange(50, 50, 100));
        Assert.True(ChartVisibleLabelAnchor.IsUsableVisibleRange(0, 0, 1));
        Assert.False(ChartVisibleLabelAnchor.IsUsableVisibleRange(0, 0, 0));
        Assert.False(ChartVisibleLabelAnchor.IsUsableVisibleRange(0, 0, 100)); // Cloud stub
        Assert.False(ChartVisibleLabelAnchor.IsUsableVisibleRange(-1, 10, 100));
        Assert.False(ChartVisibleLabelAnchor.IsUsableVisibleRange(0, 100, 50));
    }

    [Fact]
    public void ChangingChartViewport_DoesNotAffectEntryCandidateSelection()
    {
        // Garantiza separación: anclaje gráfico ≠ reglas de candidatos M15.
        var levels = new List<SrLevel>
        {
            new()
            {
                Id = 1,
                Geometry = LevelGeometry.Horizontal,
                Role = LevelRole.Resistance,
                State = LevelState.Active,
                Center = 2010,
                HalfWidth = 1,
                IndependentTouchCount = 2
            },
            new()
            {
                Id = 2,
                Geometry = LevelGeometry.Horizontal,
                Role = LevelRole.Support,
                State = LevelState.Active,
                Center = 1990,
                HalfWidth = 1,
                IndependentTouchCount = 2
            }
        };

        var snapM15Viewport = EntryCandidateSelector.Select(levels, bid: 2000, ask: 2000.1, referenceBarIndex: 0);
        var snapH1Viewport = EntryCandidateSelector.Select(levels, bid: 2000, ask: 2000.1, referenceBarIndex: 0);

        Assert.Equal(
            EntryCandidateSelector.ToIdArray(snapM15Viewport),
            EntryCandidateSelector.ToIdArray(snapH1Viewport));

        // Distintos viewports → distintos índices de etiqueta; mismos candidatos.
        var labelM15 = ChartVisibleLabelAnchor.ResolveLabelBarIndex(0, 200);
        var labelH1 = ChartVisibleLabelAnchor.ResolveLabelBarIndex(0, 50);
        Assert.NotEqual(labelM15, labelH1);
        Assert.Equal(2, snapM15Viewport.Count);
    }
}
