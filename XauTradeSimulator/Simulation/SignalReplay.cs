using cAlgo.Robots.SupportResistance;

namespace XauTradeSimulator.Simulation;

/// <summary>
/// Replay causal completo: ProcessClosedBar → ProcessAfterClosedBar.
/// Sin lookahead; sin truncar lookback (serie completa).
/// </summary>
public static class SignalReplay
{
    public sealed class ReplayResult
    {
        public required IReadOnlyList<T3BreakoutSignal> Signals { get; init; }
        public required int BarCount { get; init; }
    }

    public static ReplayResult Run(
        IReadOnlyList<OhlcBar> bars,
        int swingStrength = SupportResistanceEngine.DefaultSwingStrength,
        int lookbackBars = SupportResistanceEngine.DefaultLookbackBars,
        int atrPeriod = SupportResistanceEngine.DefaultAtrPeriod)
    {
        var engine = new SupportResistanceEngine(swingStrength, lookbackBars, atrPeriod);
        var detector = new T3BreakoutSignalDetector();

        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            engine.ProcessClosedBar(bar);
            detector.ProcessAfterClosedBar(engine, bar);
        }

        return new ReplayResult
        {
            Signals = detector.Signals.ToList(),
            BarCount = bars.Count
        };
    }
}
