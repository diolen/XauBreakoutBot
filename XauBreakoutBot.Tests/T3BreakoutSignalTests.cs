using cAlgo.Robots.SupportResistance;
using Xunit;

namespace XauBreakoutBot.Tests;

public class T3BreakoutSignalTests
{
    private static readonly DateTime T0 = new(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Buy_Valid_ResistanceBreakoutAfterT3()
    {
        var inv = Snap(
            LevelRole.Resistance, LevelState.Active, t3: 10, breakBar: 12,
            close: 103, mid: 100, upper: 101.5, lower: 98.5, atr: 10,
            "ohlc-close-proxy-resistance");
        var t3 = new Dictionary<long, int> { [1] = 10 };
        var signaled = new HashSet<long>();
        var s = T3BreakoutSignalDetector.TryCreateSignal(inv, t3, signaled);
        Assert.NotNull(s);
        Assert.Equal(SignalDirection.Buy, s!.Direction);
        Assert.Equal(1, s.LevelId);
        Assert.Equal(10, s.T3BarIndex);
        Assert.Equal(12, s.BreakoutBarIndex);
        Assert.True(s.ConfirmationClose > s.UpperBoundAtBreakout);
    }

    [Fact]
    public void Sell_Valid_SupportBreakoutAfterT3()
    {
        var inv = Snap(
            LevelRole.Support, LevelState.Active, t3: 5, breakBar: 8,
            close: 97, mid: 100, upper: 101.5, lower: 98.5, atr: 10,
            "ohlc-close-proxy-support");
        var s = T3BreakoutSignalDetector.TryCreateSignal(
            inv, new Dictionary<long, int> { [1] = 5 }, new HashSet<long>());
        Assert.NotNull(s);
        Assert.Equal(SignalDirection.Sell, s!.Direction);
        Assert.True(s.ConfirmationClose < s.LowerBoundAtBreakout);
    }

    [Fact]
    public void BreakoutWithoutPriorT3_NoSignal()
    {
        var inv = Snap(LevelRole.Resistance, LevelState.Active, 0, 3, 110, 100, 101, 99, 10,
            "ohlc-close-proxy-resistance");
        var s = T3BreakoutSignalDetector.TryCreateSignal(
            inv, new Dictionary<long, int>(), new HashSet<long>());
        Assert.Null(s);
    }

    [Fact]
    public void SameBarAsT3_NoSignal()
    {
        var inv = Snap(LevelRole.Resistance, LevelState.Active, 7, 7, 110, 100, 101, 99, 10,
            "ohlc-close-proxy-resistance");
        var s = T3BreakoutSignalDetector.TryCreateSignal(
            inv, new Dictionary<long, int> { [1] = 7 }, new HashSet<long>());
        Assert.Null(s);
    }

    [Fact]
    public void BreakoutAfterMoreThan8Bars_NoSignal()
    {
        var inv = Snap(LevelRole.Resistance, LevelState.Active, 1, 1 + 9, 110, 100, 101, 99, 10,
            "ohlc-close-proxy-resistance");
        var s = T3BreakoutSignalDetector.TryCreateSignal(
            inv, new Dictionary<long, int> { [1] = 1 }, new HashSet<long>());
        Assert.Null(s);
    }

    [Fact]
    public void InvalidationAndSignalSameBar_Allowed_WhenT3Earlier()
    {
        // T3 en barra 2; invalidación (=señal) en barra 3 — misma vela de ruptura e invalidación.
        var inv = Snap(LevelRole.Resistance, LevelState.Provisional, 2, 3, 110, 100, 101, 99, 10,
            "ohlc-close-proxy-resistance");
        var s = T3BreakoutSignalDetector.TryCreateSignal(
            inv, new Dictionary<long, int> { [1] = 2 }, new HashSet<long>());
        Assert.NotNull(s);
        Assert.Equal(3, s!.BreakoutBarIndex);
    }

    [Fact]
    public void DuplicateSignal_Blocked()
    {
        var inv = Snap(LevelRole.Support, LevelState.Active, 1, 4, 90, 100, 101, 99, 10,
            "ohlc-close-proxy-support");
        var t3 = new Dictionary<long, int> { [9] = 1 };
        var signaled = new HashSet<long> { 9 };
        Assert.Null(T3BreakoutSignalDetector.TryCreateSignal(inv, t3, signaled));
    }

    [Fact]
    public void Provisional_StateBefore_Allowed()
    {
        var inv = Snap(LevelRole.Support, LevelState.Provisional, 1, 2, 90, 100, 101, 99, 10,
            "ohlc-close-proxy-support");
        Assert.NotNull(T3BreakoutSignalDetector.TryCreateSignal(
            inv, new Dictionary<long, int> { [1] = 1 }, new HashSet<long>()));
    }

    [Fact]
    public void PreviouslyInvalidated_StateBefore_NoSignal()
    {
        var inv = Snap(LevelRole.Resistance, LevelState.Invalidated, 1, 3, 110, 100, 101, 99, 10,
            "ohlc-close-proxy-resistance");
        Assert.Null(T3BreakoutSignalDetector.TryCreateSignal(
            inv, new Dictionary<long, int> { [1] = 1 }, new HashSet<long>()));
    }

    [Fact]
    public void DiagonalGeometry_CarriedOnSignal()
    {
        var inv = Snap(LevelRole.Resistance, LevelState.Active, 1, 2, 110, 105, 106, 104, 10,
            "ohlc-close-proxy-resistance", LevelGeometry.Diagonal);
        var s = T3BreakoutSignalDetector.TryCreateSignal(
            inv, new Dictionary<long, int> { [1] = 1 }, new HashSet<long>());
        Assert.NotNull(s);
        Assert.Equal(LevelGeometry.Diagonal, s!.Geometry);
        Assert.Equal(105, s.LevelMidAtBreakout);
    }

    [Fact]
    public void Merge_AbsorbedLevel_EligibilityExpires()
    {
        var engine = new SupportResistanceEngine(2, 40, 5);
        var det = new T3BreakoutSignalDetector();
        det.SeedT3EligibilityForTests(levelId: 99, t3BarIndex: 0);
        // Procesar barras sin el level 99 presente → caduca.
        for (var i = 0; i < 5; i++)
        {
            var bar = new OhlcBar(i, T0.AddMinutes(15 * i), 100, 101, 99, 100);
            engine.ProcessClosedBar(bar);
            det.ProcessAfterClosedBar(engine, bar);
        }

        var inv = Snap(LevelRole.Resistance, LevelState.Active, 0, 3, 110, 100, 101, 99, 10,
            "ohlc-close-proxy-resistance");
        inv = new StructuralInvalidationSnapshot
        {
            LevelId = 99,
            Role = inv.Role,
            Geometry = inv.Geometry,
            StateBefore = inv.StateBefore,
            BarIndex = 3,
            Time = inv.Time,
            Close = inv.Close,
            Mid = inv.Mid,
            UpperBound = inv.UpperBound,
            LowerBound = inv.LowerBound,
            HalfWidth = inv.HalfWidth,
            AtrCurrent = inv.AtrCurrent,
            AtrRef = inv.AtrRef,
            ReasonTag = inv.ReasonTag,
            WasOhlcProxy = true,
            IndependentTouchCount = 3
        };
        // Tras ProcessAfterClosedBar sin level 99, el mapa T3 ya no lo tiene.
        Assert.Null(T3BreakoutSignalDetector.TryCreateSignal(
            inv, new Dictionary<long, int>(), new HashSet<long>()));
    }

    [Fact]
    public void VariableAtr_StoredOnSignal()
    {
        var inv = Snap(LevelRole.Resistance, LevelState.Active, 1, 2, 120, 100, 103, 97, 20,
            "ohlc-close-proxy-resistance");
        var s = T3BreakoutSignalDetector.TryCreateSignal(
            inv, new Dictionary<long, int> { [1] = 1 }, new HashSet<long>());
        Assert.Equal(20, s!.AtrAtBreakout);
    }

    [Fact]
    public void Engine_RecordsLastBarInvalidations_Causally()
    {
        var engine = new SupportResistanceEngine(2, 50, 5);
        for (var i = 0; i < 25; i++)
            engine.ProcessClosedBar(new OhlcBar(i, T0.AddMinutes(15 * i), 100, 101, 99, 100));
        Assert.Empty(engine.LastBarInvalidations);
        // API estable y limpia cada barra.
        engine.ProcessClosedBar(new OhlcBar(25, T0.AddMinutes(15 * 25), 100, 101, 99, 100));
        Assert.Empty(engine.LastBarInvalidations);
    }

    [Fact]
    public void ReplayDeterministic_TryCreateStable()
    {
        var inv = Snap(LevelRole.Support, LevelState.Active, 4, 6, 90, 100, 101, 98.5, 10,
            "ohlc-close-proxy-support");
        var a = T3BreakoutSignalDetector.TryCreateSignal(
            inv, new Dictionary<long, int> { [1] = 4 }, new HashSet<long>());
        var b = T3BreakoutSignalDetector.TryCreateSignal(
            inv, new Dictionary<long, int> { [1] = 4 }, new HashSet<long>());
        Assert.Equal(a!.BreakoutBarIndex, b!.BreakoutBarIndex);
        Assert.Equal(a.ConfirmationClose, b.ConfirmationClose);
    }

    [Fact]
    public void NoLookahead_BreakoutIndex_IsInvalidationBar()
    {
        var inv = Snap(LevelRole.Resistance, LevelState.Active, 1, 5, 110, 100, 101, 99, 10,
            "ohlc-close-proxy-resistance");
        var s = T3BreakoutSignalDetector.TryCreateSignal(
            inv, new Dictionary<long, int> { [1] = 1 }, new HashSet<long>());
        Assert.Equal(inv.BarIndex, s!.BreakoutBarIndex);
    }

    [Fact]
    public void TickInvalidation_NotUsedForSignals()
    {
        var inv = Snap(LevelRole.Resistance, LevelState.Active, 1, 2, 110, 100, 101, 99, 10,
            "tick-bid-resistance");
        // WasOhlcProxy false
        inv = new StructuralInvalidationSnapshot
        {
            LevelId = 1,
            Role = LevelRole.Resistance,
            Geometry = LevelGeometry.Horizontal,
            StateBefore = LevelState.Active,
            BarIndex = 2,
            Time = T0,
            Close = 110,
            Mid = 100,
            UpperBound = 101,
            LowerBound = 99,
            HalfWidth = 1,
            AtrCurrent = 10,
            ReasonTag = "tick-bid-resistance",
            WasOhlcProxy = false,
            IndependentTouchCount = 3
        };
        Assert.Null(T3BreakoutSignalDetector.TryCreateSignal(
            inv, new Dictionary<long, int> { [1] = 1 }, new HashSet<long>()));
    }

    [Fact]
    public void DefaultSwingStrength_Remains3()
    {
        Assert.Equal(3, SupportResistanceEngine.DefaultSwingStrength);
    }

    [Fact]
    public void Integration_SeededT3_ThenForcedInvalidationPath_Dedupes()
    {
        var engine = new SupportResistanceEngine(2, 40, 5);
        var det = new T3BreakoutSignalDetector();
        for (var i = 0; i < 20; i++)
        {
            var bar = new OhlcBar(i, T0.AddMinutes(15 * i), 100, 101, 99, 100);
            engine.ProcessClosedBar(bar);
            det.ProcessAfterClosedBar(engine, bar);
        }

        // Sembrar elegibilidad sobre un id inexistente no genera señal sin invalidación del motor.
        det.SeedT3EligibilityForTests(12345, 10);
        var bar21 = new OhlcBar(21, T0.AddMinutes(15 * 21), 100, 101, 99, 100);
        engine.ProcessClosedBar(bar21);
        var newly = det.ProcessAfterClosedBar(engine, bar21);
        Assert.Empty(newly);
    }

    private static StructuralInvalidationSnapshot Snap(
        LevelRole role,
        LevelState stateBefore,
        int t3,
        int breakBar,
        double close,
        double mid,
        double upper,
        double lower,
        double atr,
        string reason,
        LevelGeometry geometry = LevelGeometry.Horizontal) =>
        new()
        {
            LevelId = 1,
            Role = role,
            Geometry = geometry,
            StateBefore = stateBefore,
            BarIndex = breakBar,
            Time = T0.AddMinutes(15 * breakBar),
            Close = close,
            Mid = mid,
            UpperBound = upper,
            LowerBound = lower,
            HalfWidth = (upper - lower) / 2,
            AtrCurrent = atr,
            AtrRef = atr,
            ReasonTag = reason,
            WasOhlcProxy = true,
            IndependentTouchCount = 3
        };
}
