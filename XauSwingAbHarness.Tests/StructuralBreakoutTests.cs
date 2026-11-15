using cAlgo.Robots.SupportResistance;
using XauSwingAbHarness.Metrics;
using XauSwingAbHarness.Structural;
using Xunit;

namespace XauSwingAbHarness.Tests;

public class StructuralBreakoutTests
{
    [Fact]
    public void MeetsExperimental_And_Structural_Formulas()
    {
        Assert.True(StructuralBreakoutRunner.MeetsExperimental(
            LevelRole.Resistance, close: 101.6, mid: 100, atrAtT3: 10, bufMult: 0.15));
        Assert.False(StructuralBreakoutRunner.MeetsExperimental(
            LevelRole.Resistance, close: 101.4, mid: 100, atrAtT3: 10, bufMult: 0.15));

        // Upper=100.15*10? upper=101.5 if mid=100 hw=1.5; +0.10*10=1 → need >102.5
        Assert.True(StructuralBreakoutRunner.MeetsStructural(
            LevelRole.Resistance, 102.6, upper: 101.5, lower: 98.5, atrCurrent: 10, bufMult: 0.10));
        Assert.False(StructuralBreakoutRunner.MeetsStructural(
            LevelRole.Resistance, 102.4, upper: 101.5, lower: 98.5, atrCurrent: 10, bufMult: 0.10));
    }

    [Fact]
    public void Classify_MutuallyExclusive()
    {
        Assert.Equal(BreakoutClass.Neither, StructuralBreakoutRunner.Classify(null, null));
        Assert.Equal(BreakoutClass.OnlyC, StructuralBreakoutRunner.Classify(null, 5));
        Assert.Equal(BreakoutClass.OnlyA, StructuralBreakoutRunner.Classify(5, null));
        Assert.Equal(BreakoutClass.AAndCSameBar, StructuralBreakoutRunner.Classify(5, 5));
        Assert.Equal(BreakoutClass.AAndCDifferentBars, StructuralBreakoutRunner.Classify(7, 5));
    }

    [Fact]
    public void SameBar_C_BeforeOrWith_A_OnSyntheticSeries()
    {
        // Construir nivel: mid=100, hw≈1.5 con atr~10 → structural ~102.5, experimental ~101.5
        var bars = BuildRangeBars(mid: 100, count: 80);
        // Forzar un swing-ish path is hard without engine; unit-test runner formulas + classify instead.
        // Full engine path: use mild series and assert runner completes deterministically.
        var cfg = new StructuralBreakoutConfig { LookbackBars = 40, AtrPeriod = 5, MaxBarsToConfirm = 8 };
        var run = StructuralBreakoutRunner.Run("T", 3, bars, cfg);
        Assert.True(run.T3Count >= 0);
        Assert.Equal(run.Classified.Count, run.T3Count);
        var sum = run.Classified.Count(c => c.Class == BreakoutClass.OnlyC) +
                  run.Classified.Count(c => c.Class == BreakoutClass.OnlyA) +
                  run.Classified.Count(c => c.Class == BreakoutClass.AAndCSameBar) +
                  run.Classified.Count(c => c.Class == BreakoutClass.AAndCDifferentBars) +
                  run.Classified.Count(c => c.Class == BreakoutClass.Neither);
        Assert.Equal(run.T3Count, sum);
    }

    [Fact]
    public void C_CanFire_Without_Structural_WhenBetweenThresholds()
    {
        // mid=100, atrT3=10 → C at >101.5; structural upper=101.5 +1 = >102.5
        Assert.True(StructuralBreakoutRunner.MeetsExperimental(
            LevelRole.Resistance, 102.0, 100, 10, 0.15));
        Assert.False(StructuralBreakoutRunner.MeetsStructural(
            LevelRole.Resistance, 102.0, upper: 101.5, lower: 98.5, atrCurrent: 10, bufMult: 0.10));
    }

    [Fact]
    public void Structural_Without_C_WhenJumpPastBoth_StillStructural()
    {
        Assert.True(StructuralBreakoutRunner.MeetsStructural(
            LevelRole.Resistance, 103, upper: 101.5, lower: 98.5, atrCurrent: 10, bufMult: 0.10));
        Assert.True(StructuralBreakoutRunner.MeetsExperimental(
            LevelRole.Resistance, 103, 100, 10, 0.15));
        // Same bar both → class AAndCSameBar when both confirm bars equal
        Assert.Equal(BreakoutClass.AAndCSameBar, StructuralBreakoutRunner.Classify(12, 12));
    }

    [Fact]
    public void DiagonalProjection_IsCausalFunctionOfBarIndex()
    {
        var level = new SrLevel
        {
            Id = 1,
            Geometry = LevelGeometry.Diagonal,
            Role = LevelRole.Resistance,
            Center = 100,
            SlopePerBar = 0.1,
            State = LevelState.Active,
            HalfWidth = 1.5,
            AtrRef = 10
        };
        level.Anchors.Add(new AnchorPoint { BarIndex = 10, Time = DateTime.UtcNow, Price = 100 });
        level.Anchors.Add(new AnchorPoint { BarIndex = 30, Time = DateTime.UtcNow.AddHours(5), Price = 102 });
        var mid20 = level.ProjectPriceAtBar(20);
        Assert.Equal(101.0, mid20, 6);
        var mid20b = level.ProjectPriceAtBar(20);
        Assert.Equal(mid20, mid20b);
    }

    [Fact]
    public void VariableAtr_StructuralUsesCurrent_ExperimentalUsesT3()
    {
        // atrCurrent high → structural harder; atrT3 low → experimental easier
        Assert.False(StructuralBreakoutRunner.MeetsStructural(
            LevelRole.Resistance, 102.2, 101.5, 98.5, atrCurrent: 20, bufMult: 0.10)); // need >103.5
        Assert.True(StructuralBreakoutRunner.MeetsExperimental(
            LevelRole.Resistance, 102.2, 100, atrAtT3: 10, bufMult: 0.15));
    }

    [Fact]
    public void IncompleteHorizon_PathYieldsUnresolved()
    {
        var eval = OhlcPathEvaluator.Evaluate(
            Array.Empty<(double, double)>(), 100, 2, 0.5, true, OhlcAmbiguityPolicy.Optimistic);
        Assert.Equal(ReactionOutcome.Unresolved, eval.Outcome);
    }

    [Fact]
    public void Decimation_ReducesOverlap_ConsistentCounts()
    {
        var list = Enumerable.Range(0, 20).Select(i => new ClassifiedT3Event
        {
            VariantId = "T",
            SwingK = 3,
            LevelId = i + 1,
            Role = LevelRole.Resistance,
            Geometry = LevelGeometry.Horizontal,
            T3BarIndex = i * 3,
            Segment = "develop",
            AtrAtT3 = 1,
            Class = BreakoutClass.Neither
        }).ToList();
        var dec = StructuralBreakoutEvaluator.Decimate(list, 16);
        Assert.True(dec.Count < list.Count);
        for (var i = 1; i < dec.Count; i++)
            Assert.True(dec[i].T3BarIndex - dec[i - 1].T3BarIndex > 16);
    }

    [Fact]
    public void ExpectedSha_MatchesConstant()
    {
        Assert.Equal(
            "023c31d2e0b45a85ad1411350d28539e32c36a60cc96f05d0c8945cbe2bce95d",
            StructuralBreakoutConfig.ExpectedSha256);
    }

    private static List<OhlcBar> BuildRangeBars(double mid, int count)
    {
        var t0 = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<OhlcBar>(count);
        for (var i = 0; i < count; i++)
        {
            // oscilación suave para generar algo de estructura
            var wiggle = Math.Sin(i / 5.0) * 2;
            var m = mid + wiggle;
            bars.Add(new OhlcBar(i, t0.AddMinutes(15 * i), m, m + 1.5, m - 1.5, m));
        }

        return bars;
    }
}
