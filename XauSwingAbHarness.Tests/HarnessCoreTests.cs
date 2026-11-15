using cAlgo.Robots.SupportResistance;
using XauSwingAbHarness.Dataset;
using XauSwingAbHarness.Experiment;
using XauSwingAbHarness.Metrics;
using Xunit;

namespace XauSwingAbHarness.Tests;

public class HarnessCoreTests
{
    [Fact]
    public void GapClassifier_DetectsWeekendAndWeekdayBreak()
    {
        var fri = new DateTime(2025, 11, 14, 21, 45, 0, DateTimeKind.Utc);
        var sun = new DateTime(2025, 11, 16, 23, 0, 0, DateTimeKind.Utc);
        Assert.Equal(GapClass.WeekendClose, GapClassifier.Classify(fri, sun));

        var mon = new DateTime(2025, 11, 17, 21, 45, 0, DateTimeKind.Utc);
        var monResume = new DateTime(2025, 11, 17, 23, 0, 0, DateTimeKind.Utc);
        Assert.Equal(GapClass.WeekdayDailyBreak, GapClassifier.Classify(mon, monResume));
    }

    [Fact]
    public void SegmentSplitter_UsesChronologicalFractions()
    {
        Assert.Equal(SegmentSplitter.Develop, SegmentSplitter.SegmentOf(0, 100, 0.6, 0.2));
        Assert.Equal(SegmentSplitter.Validate, SegmentSplitter.SegmentOf(60, 100, 0.6, 0.2));
        Assert.Equal(SegmentSplitter.Oos, SegmentSplitter.SegmentOf(80, 100, 0.6, 0.2));
    }

    [Fact]
    public void VariantRunner_K2ConfirmsEarlierThanK3_OnSyntheticPivot()
    {
        // Construir un swing high claro en índice 5; K=2 confirma en barra 7, K=3 en barra 8.
        var bars = new List<OhlcBar>();
        var t0 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i <= 40; i++)
        {
            double high = i == 5 ? 110 : 100 + (i < 5 ? i : Math.Max(0, 10 - i));
            if (i > 5)
                high = 100 + Math.Max(0, 8 - (i - 5));
            bars.Add(new OhlcBar(i, t0.AddMinutes(15 * i), 100, high, 99, 100));
        }

        // Añadir más barras con rango para ATR y toques.
        for (var i = 41; i < 120; i++)
        {
            var mid = 100.0;
            bars.Add(new OhlcBar(i, t0.AddMinutes(15 * i), mid, mid + 1, mid - 1, mid));
        }

        var cfg = new AbExperimentConfig { LookbackBars = 50, AtrPeriod = 5 };
        var a = VariantRunner.Run("A", 2, bars, cfg);
        var b = VariantRunner.Run("B", 3, bars, cfg);

        var swingA = a.Events.FirstOrDefault(e => e.Kind == EventKind.SwingConfirmed && e.Detail == "High");
        var swingB = b.Events.FirstOrDefault(e => e.Kind == EventKind.SwingConfirmed && e.Detail == "High");
        Assert.NotNull(swingA);
        Assert.NotNull(swingB);
        Assert.Equal(2, swingA!.ConfirmationLatencyBars);
        Assert.Equal(3, swingB!.ConfirmationLatencyBars);
        Assert.True(swingA.BarIndex < swingB.BarIndex);
    }

    [Fact]
    public void PredictiveEvaluator_NoLookaheadBeyondHorizon()
    {
        var t0 = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<OhlcBar>();
        for (var i = 0; i < 40; i++)
        {
            // Tras evento en 10, highs suben fuerte.
            var high = i <= 10 ? 100 : 100 + (i - 10) * 2;
            bars.Add(new OhlcBar(i, t0.AddMinutes(15 * i), 100, high, 99, 100));
        }

        var ev = new ExperimentEvent
        {
            VariantId = "T",
            SwingK = 3,
            Kind = EventKind.TouchCountReached3,
            Observability = Observability.RealtimeObservable,
            BarIndex = 10,
            OpenTimeUtc = bars[10].OpenTime,
            Segment = SegmentSplitter.Develop,
            LevelId = 1,
            Role = LevelRole.Resistance,
            AtrAtEvent = 2.0,
            Price = 100
        };

        var run = new VariantRunResult
        {
            VariantId = "T",
            SwingK = 3,
            PreloadBars = 17,
            MinimumPreferredBars = 100,
            HasInsufficientHistory = false,
            HistoryWarning = null,
            Events = new[] { ev },
            FinalSwingCount = 0,
            FinalLevelCount = 1,
            FinalActiveCount = 1,
            FinalInvalidatedCount = 0
        };

        var cfg = new AbExperimentConfig
        {
            PredictiveHorizonsBars = new[] { 4 },
            ReactionThresholdAtr = 0.5
        };
        var rows = PredictiveEvaluator.Evaluate(run, bars, cfg);
        Assert.Single(rows);
        Assert.True(rows[0].HitBeforeAdverse);
        Assert.True(rows[0].MfeAtr > 0);
    }

    [Fact]
    public void CsvRoundTrip_HeaderAndInvariant()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xau-ab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "mini.csv");
        File.WriteAllText(path,
            "OpenTime,Open,High,Low,Close\n" +
            "2025-11-14T00:00:00Z,100,101,99,100.5\n" +
            "2025-11-14T00:15:00Z,100.5,102,100,101\n");

        var bars = CsvHistoryLoader.Load(path);
        var val = DatasetValidator.Validate(path, bars);
        Assert.True(val.IsStructurallyValid);
        Assert.Equal(2, val.BarCount);
        Directory.Delete(dir, recursive: true);
    }
}
