using cAlgo.Robots.SupportResistance;
using Xunit;
using Xunit.Abstractions;

namespace XauBreakoutBot.Tests;

/// <summary>
/// Auditoría determinista de toques independientes vs docs §7.
/// Solo pruebas/análisis; no modifica el motor.
/// </summary>
public class IndependentTouchAuditTests
{
    private readonly ITestOutputHelper _out;
    public IndependentTouchAuditTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void ConsecutiveBarsInZone_DoNotAddExtraIndependentTouches()
    {
        var (engine, levelId) = BuildResistanceInTouchEpisode(out _);
        var level = Req(engine, levelId);
        var before = level.IndependentTouchCount;
        Assert.True(before >= 1);
        Assert.True(level.InTouchEpisode);
        Assert.False(level.RetreatConfirmed);

        var center = level.Center;
        var hw = level.HalfWidth;
        var t = level.UpdatedAt;

        for (var i = 0; i < 6; i++)
        {
            var idx = engine.ClosedBarCount;
            // Sigue en zona; Close cerca del centro (sin retreat 0.5×ATR).
            engine.ProcessClosedBar(Bar(idx, t.AddMinutes(15 * (i + 1)), center, center + hw * 0.5, center - hw * 0.2, center));
        }

        level = Req(engine, levelId);
        Assert.Equal(before, level.IndependentTouchCount);
        AssertCountMatchesRecords(level);
        Assert.True(level.InTouchEpisode);
        Assert.False(level.RetreatConfirmed);
    }

    [Fact]
    public void InsufficientRetreat_DoesNotAllowNewIndependentTouch()
    {
        var (engine, levelId) = BuildResistanceInTouchEpisode(out var atr);
        var level = Req(engine, levelId);
        var before = level.IndependentTouchCount;
        var lower = level.Center - level.HalfWidth;
        var t = level.UpdatedAt;

        // Sale de la zona (High < lower) pero Close no alcanza lower − 0.5×ATR.
        var idx = engine.ClosedBarCount;
        var close = lower - 0.25 * atr;
        var high = lower - 0.01;
        Assert.True(close > lower - 0.5 * atr);
        engine.ProcessClosedBar(Bar(idx, t.AddMinutes(15), close, high, close - 1, close));

        level = Req(engine, levelId);
        Assert.False(level.RetreatConfirmed);
        Assert.False(level.InTouchEpisode);
        Assert.Equal(before, level.IndependentTouchCount);

        // Regreso a zona: no debe sumar toque.
        idx = engine.ClosedBarCount;
        var c = level.Center;
        var hw = level.HalfWidth;
        engine.ProcessClosedBar(Bar(idx, t.AddMinutes(30), c, c + hw * 0.4, c - hw, c));

        level = Req(engine, levelId);
        Assert.Equal(before, level.IndependentTouchCount);
        AssertCountMatchesRecords(level);
    }

    [Fact]
    public void ConfirmedRetreat_ThenReturn_AddsIndependentTouch_UsingClosedBarClose()
    {
        var (engine, levelId) = BuildResistanceInTouchEpisode(out var atr);
        var level = Req(engine, levelId);
        var before = level.IndependentTouchCount;
        var lower = level.Center - level.HalfWidth;
        var t = level.UpdatedAt;

        // Vela cerrada: Close confirma retreat (≥ 0.5×ATR bajo el borde inferior).
        // Margen amplio: HalfWidth Provisional puede moverse con ATR_current en la misma barra.
        var idx = engine.ClosedBarCount;
        var close = lower - Math.Max(atr, 1.0) * 2.0;
        engine.ProcessClosedBar(Bar(idx, t.AddMinutes(15), close + 1, lower - 0.01, close - 1, close));

        level = Req(engine, levelId);
        Assert.True(level.RetreatConfirmed, $"close={close:F4} lower0={lower:F4} atr={atr:F4} hw={level.HalfWidth:F4}");
        Assert.Equal(before, level.IndependentTouchCount);

        // Retorno posterior a la zona → nuevo toque independiente.
        idx = engine.ClosedBarCount;
        var c = level.Center;
        var hw = level.HalfWidth;
        var touchHigh = c + hw * 0.3;
        engine.ProcessClosedBar(Bar(idx, t.AddMinutes(30), c, touchHigh, c - hw, c));

        level = Req(engine, levelId);
        Assert.Equal(before + 1, level.IndependentTouchCount);
        AssertCountMatchesRecords(level);
        var last = level.Touches.Where(x => x.IsIndependent).OrderBy(x => x.BarIndex).Last();
        Assert.Equal(idx, last.BarIndex);
        Assert.Equal(touchHigh, last.Price);
        Assert.False(level.RetreatConfirmed);
    }

    [Fact]
    public void FourIndependentTouches_NearClusterWithoutRetreat_DoesNotInflate_ThenValidT3T4()
    {
        var (engine, levelId) = BuildResistanceInTouchEpisode(out var atr);
        var level = Req(engine, levelId);
        var t1 = level.IndependentTouchCount;

        // Cluster en zona sin retreat: no infla.
        StayInZone(engine, levelId, bars: 5);
        level = Req(engine, levelId);
        Assert.Equal(t1, level.IndependentTouchCount);

        // T+1 y T+2 con retreat válidos (equivalente a acumular hacia T3/T4 si se partía de T1/T2).
        RetreatAndReturn(engine, levelId, atr);
        RetreatAndReturn(engine, levelId, atr);
        level = Req(engine, levelId);
        Assert.Equal(t1 + 2, level.IndependentTouchCount);

        StayInZone(engine, levelId, bars: 4); // “toques cercanos” sin alejamiento
        level = Req(engine, levelId);
        Assert.Equal(t1 + 2, level.IndependentTouchCount);

        RetreatAndReturn(engine, levelId, atr);
        RetreatAndReturn(engine, levelId, atr);
        level = Req(engine, levelId);

        Assert.Equal(t1 + 4, level.IndependentTouchCount);
        Assert.Equal(LevelState.Active, level.State);
        AssertCountMatchesRecords(level);
        DumpLevel(level, atr, "near-T3T4-style");
    }

    [Fact]
    public void IndependentTouchCount_AlwaysMatchesIndependentTouchRecords()
    {
        var engine = new SupportResistanceEngine();
        engine.ReplayClosedBars(BuildSyntheticXauLike(497, 42), true);
        foreach (var level in engine.Levels)
            AssertCountMatchesRecords(level);
    }

    [Fact]
    public void DiagonalAnchors_AreTwoTouches_SepOk_ThirdNeedsRetreat()
    {
        var engine = new SupportResistanceEngine();
        engine.ReplayClosedBars(BuildSyntheticXauLike(497, 42), true);

        var diagonal = engine.Levels
            .Where(l => l.Geometry == LevelGeometry.Diagonal && l.State != LevelState.Invalidated)
            .OrderByDescending(l => l.IndependentTouchCount)
            .FirstOrDefault();

        Assert.NotNull(diagonal);
        Assert.Equal(2, diagonal!.Anchors.Count);
        Assert.True(diagonal.Anchors[1].BarIndex - diagonal.Anchors[0].BarIndex >=
                    SupportResistanceEngine.MinDiagonalAnchorSeparation);

        var ordered = diagonal.Touches.Where(t => t.IsIndependent).OrderBy(t => t.BarIndex).ToList();
        Assert.True(ordered.Count >= 2);
        Assert.Equal(diagonal.Anchors[0].BarIndex, ordered[0].BarIndex);
        Assert.Equal(diagonal.Anchors[0].Price, ordered[0].Price);
        Assert.Equal(diagonal.Anchors[1].BarIndex, ordered[1].BarIndex);
        Assert.Equal(diagonal.Anchors[1].Price, ordered[1].Price);
        AssertCountMatchesRecords(diagonal);

        // Si T>2, entre ancla2 y T3 debe haber al menos 1 barra de separación.
        if (ordered.Count >= 3)
            Assert.True(ordered[2].BarIndex - ordered[1].BarIndex >= 1);

        DumpLevel(diagonal, engine.AtrCurrent, "diagonal");
    }

    [Fact]
    public void ActiveWithThreePlusTouches_RemainsActive_NotAnEntrySignal()
    {
        var (engine, levelId) = BuildResistanceInTouchEpisode(out var atr);
        RetreatAndReturn(engine, levelId, atr);
        RetreatAndReturn(engine, levelId, atr);
        RetreatAndReturn(engine, levelId, atr);

        var level = Req(engine, levelId);
        Assert.True(level.IndependentTouchCount >= 3);
        Assert.Equal(LevelState.Active, level.State);
        Assert.Null(level.Invalidation);
        AssertCountMatchesRecords(level);
        // Active + T≥3 describe historial de toques; no implica elegibilidad/entrada (no implementada).
    }

    [Fact]
    public void SameBar_RetreatCloseAndWickIntoZone_DoesNotRegisterNewTouch()
    {
        var (engine, levelId) = BuildResistanceInTouchEpisode(out var atr);
        var level = Req(engine, levelId);
        var before = level.IndependentTouchCount;
        var lower = level.Center - level.HalfWidth;
        var upper = level.Center + level.HalfWidth;
        var t = level.UpdatedAt;

        var idx = engine.ClosedBarCount;
        var weakClose = lower - 0.1 * atr;
        engine.ProcessClosedBar(Bar(idx, t.AddMinutes(15), weakClose, lower - 0.01, weakClose - 1, weakClose));
        level = Req(engine, levelId);
        Assert.False(level.RetreatConfirmed);
        Assert.False(level.InTouchEpisode);

        // Misma vela: Close confirma retreat + High re-entra en zona → NO nuevo toque.
        idx = engine.ClosedBarCount;
        var retreatBar = idx;
        var retreatClose = lower - Math.Max(atr, 1.0) * 2.0;
        var wickHigh = level.Center;
        Assert.True(wickHigh >= lower && wickHigh <= upper);
        engine.ProcessClosedBar(Bar(idx, t.AddMinutes(30), retreatClose, wickHigh, retreatClose - 1, retreatClose));

        level = Req(engine, levelId);
        Assert.Equal(before, level.IndependentTouchCount);
        Assert.True(level.RetreatConfirmed);
        Assert.Equal(retreatBar, level.RetreatConfirmedAtBar);
        Assert.False(level.InTouchEpisode);
        AssertCountMatchesRecords(level);
    }

    [Fact]
    public void RetreatConfirmed_ThenLaterBarContact_RegistersIndependentTouch()
    {
        var (engine, levelId) = BuildResistanceInTouchEpisode(out var atr);
        var level = Req(engine, levelId);
        var before = level.IndependentTouchCount;
        var lower = level.Center - level.HalfWidth;
        var t = level.UpdatedAt;

        // Vela de retreat (con mecha en zona): confirma alejamiento, no cuenta toque.
        var idx = engine.ClosedBarCount;
        var retreatBar = idx;
        var retreatClose = lower - Math.Max(atr, 1.0) * 2.0;
        engine.ProcessClosedBar(Bar(idx, t.AddMinutes(15), retreatClose, level.Center, retreatClose - 1, retreatClose));
        level = Req(engine, levelId);
        Assert.True(level.RetreatConfirmed);
        Assert.Equal(retreatBar, level.RetreatConfirmedAtBar);
        Assert.Equal(before, level.IndependentTouchCount);

        // Vela posterior en zona → sí cuenta.
        idx = engine.ClosedBarCount;
        Assert.True(idx > retreatBar);
        var c = level.Center;
        var hw = level.HalfWidth;
        var touchHigh = c + hw * 0.25;
        engine.ProcessClosedBar(Bar(idx, t.AddMinutes(30), c, touchHigh, c - hw, c));

        level = Req(engine, levelId);
        Assert.Equal(before + 1, level.IndependentTouchCount);
        Assert.False(level.RetreatConfirmed);
        Assert.Null(level.RetreatConfirmedAtBar);
        AssertCountMatchesRecords(level);
        var last = level.Touches.Where(x => x.IsIndependent).OrderBy(x => x.BarIndex).Last();
        Assert.Equal(idx, last.BarIndex);
        Assert.True(last.BarIndex > retreatBar);
    }

    [Fact]
    public void DiagnosticReport_SyntheticActiveHighTouchLevels()
    {
        var engine = new SupportResistanceEngine();
        engine.ReplayClosedBars(BuildSyntheticXauLike(497, 42), true);
        var atr = engine.AtrCurrent;
        var actives = engine.Levels
            .Where(l => l.State == LevelState.Active && l.IndependentTouchCount >= 3)
            .OrderByDescending(l => l.IndependentTouchCount)
            .ThenBy(l => l.Id)
            .Take(10)
            .ToList();

        _out.WriteLine($"Nota: IDs de pantalla (#70/#74/#63) no son estables tras reinicio; informe sintético seed=42.");
        _out.WriteLine($"ATR={atr:F4} Active T>=3: {actives.Count}");

        foreach (var level in actives)
        {
            DumpLevel(level, atr, "diag");
            AssertCountMatchesRecords(level);
            var touches = level.Touches.Where(t => t.IsIndependent).OrderBy(t => t.BarIndex).ToList();
            var start = level.Geometry == LevelGeometry.Diagonal ? 1 : 0;
            for (var i = start; i < touches.Count - 1; i++)
            {
                var sep = touches[i + 1].BarIndex - touches[i].BarIndex;
                _out.WriteLine($"  gap T{i + 1}->T{i + 2}: {sep} bars");
                Assert.True(sep >= 1);
            }
        }
    }

    // ----------------- helpers -----------------

    private void DumpLevel(SrLevel level, double atr, string tag)
    {
        _out.WriteLine(
            $"[{tag}] #{level.Id} {level.Geometry} {level.Role} {level.State} T={level.IndependentTouchCount} " +
            $"Center/proj~={level.Center:F2} HW={level.HalfWidth:F4}");
        foreach (var t in level.Touches.Where(x => x.IsIndependent).OrderBy(x => x.BarIndex))
            _out.WriteLine($"  T@{t.BarIndex} {t.Time:u} px={t.Price:F2}");
    }

    private static void AssertCountMatchesRecords(SrLevel level)
    {
        var n = level.Touches.Count(t => t.IsIndependent);
        Assert.Equal(n, level.IndependentTouchCount);
        Assert.Equal(n, level.Touches.Where(t => t.IsIndependent).Select(t => t.BarIndex).Distinct().Count());
    }

    private static SrLevel Req(SupportResistanceEngine engine, long id) =>
        engine.Levels.Single(l => l.Id == id);

    /// <summary>
    /// Deja un Resistance horizontal con ≥1 toque, InTouchEpisode=true, RetreatConfirmed=false.
    /// </summary>
    private static (SupportResistanceEngine engine, long levelId) BuildResistanceInTouchEpisode(out double atr)
    {
        var engine = new SupportResistanceEngine(2, 120, 5);
        var t0 = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        const double mid = 2000.0;

        for (var i = 0; i < 30; i++)
            engine.ProcessClosedBar(Bar(i, t0.AddMinutes(15 * i), mid, mid + 3, mid - 3, mid));

        // Pico en 32; confirmación K=2 en barra 34.
        double[] highs = { mid + 2, mid + 4, mid + 25, mid + 4, mid + 2 };
        for (var i = 0; i < highs.Length; i++)
        {
            var idx = 30 + i;
            engine.ProcessClosedBar(Bar(idx, t0.AddMinutes(15 * idx), mid, highs[i], mid - 2, mid));
        }

        atr = engine.AtrCurrent;
        var level = engine.Levels
            .Where(l => l.Geometry == LevelGeometry.Horizontal && l.Role == LevelRole.Resistance)
            .OrderByDescending(l => l.Center)
            .First();

        // Tras confirmación, el Close en mid suele dejar RetreatConfirmed=true.
        // Forzar reentrada en zona para obtener un toque con episodio abierto y retreat limpio en false.
        if (level.RetreatConfirmed || !level.InTouchEpisode)
        {
            // Si ya hay retreat, reentrar (suma toque) y permanecer en zona.
            var idx = engine.ClosedBarCount;
            var c = level.Center;
            var hw = level.HalfWidth;
            engine.ProcessClosedBar(Bar(idx, t0.AddMinutes(15 * idx), c, c + hw * 0.4, c - hw * 0.5, c));
            level = Req(engine, level.Id);
        }

        // Asegurar que seguimos en episodio: otra vela en zona (no debe sumar).
        {
            var idx = engine.ClosedBarCount;
            var c = level.Center;
            var hw = level.HalfWidth;
            var before = level.IndependentTouchCount;
            engine.ProcessClosedBar(Bar(idx, t0.AddMinutes(15 * idx), c, c + hw * 0.3, c - hw * 0.4, c));
            level = Req(engine, level.Id);
            Assert.Equal(before, level.IndependentTouchCount);
            Assert.True(level.InTouchEpisode);
            Assert.False(level.RetreatConfirmed);
        }

        return (engine, level.Id);
    }

    private static void StayInZone(SupportResistanceEngine engine, long levelId, int bars)
    {
        var level = Req(engine, levelId);
        var c = level.Center;
        var hw = level.HalfWidth;
        var t = level.UpdatedAt;
        for (var i = 0; i < bars; i++)
        {
            var idx = engine.ClosedBarCount;
            engine.ProcessClosedBar(Bar(idx, t.AddMinutes(15 * (i + 1)), c, c + hw * 0.4, c - hw * 0.3, c));
        }
    }

    private static void RetreatAndReturn(SupportResistanceEngine engine, long levelId, double atrHint)
    {
        var level = Req(engine, levelId);
        var atr = engine.AtrCurrent > 0 ? engine.AtrCurrent : atrHint;
        var lower = level.Center - level.HalfWidth;
        var before = level.IndependentTouchCount;
        var t = level.UpdatedAt;

        var idx = engine.ClosedBarCount;
        var close = lower - Math.Max(atr, 1.0) * 2.0;
        engine.ProcessClosedBar(Bar(idx, t.AddMinutes(15), close + 1, lower - 0.01, close - 1, close));
        level = Req(engine, levelId);
        Assert.False(level.State == LevelState.Invalidated, "no debería invalidar resistencia con close por debajo");
        Assert.True(level.RetreatConfirmed);

        idx = engine.ClosedBarCount;
        var c = level.Center;
        var hw = level.HalfWidth;
        engine.ProcessClosedBar(Bar(idx, t.AddMinutes(30), c, c + hw * 0.35, c - hw, c));
        level = Req(engine, levelId);
        Assert.Equal(before + 1, level.IndependentTouchCount);
    }

    private static OhlcBar Bar(int i, DateTime t, double o, double h, double l, double c) =>
        new(i, t, o, h, l, c);

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
                i, t0.AddMinutes(15 * i), open,
                Math.Max(open, close) + rng.NextDouble() * 2.5,
                Math.Min(open, close) - rng.NextDouble() * 2.5,
                close));
            price = close;
        }

        return bars;
    }
}
