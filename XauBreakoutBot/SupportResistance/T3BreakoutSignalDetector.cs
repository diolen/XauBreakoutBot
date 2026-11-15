using System;
using System.Collections.Generic;

namespace cAlgo.Robots.SupportResistance;

/// <summary>
/// Detector determinista de señales T3 + breakout estructural (invalidación OHLC).
/// No envía órdenes. Una señal por LevelId. Ventana (T3, T3+MaxBarsAfterT3].
/// Merge: elegibilidad del LevelId absorbido caduca (no transferencia sin snapshot).
/// </summary>
public sealed class T3BreakoutSignalDetector
{
    public const int MaxBarsAfterT3 = 8;

    private readonly Dictionary<long, int> _t3BarByLevel = new();
    private readonly Dictionary<long, int> _prevTouchCount = new();
    private readonly HashSet<long> _signaledLevels = new();
    private readonly List<T3BreakoutSignal> _signals = new();

    public IReadOnlyList<T3BreakoutSignal> Signals => _signals;

    public void Reset()
    {
        _t3BarByLevel.Clear();
        _prevTouchCount.Clear();
        _signaledLevels.Clear();
        _signals.Clear();
    }

    /// <summary>Solo tests: registra T=3 elegible sin pasar por el motor.</summary>
    public void SeedT3EligibilityForTests(long levelId, int t3BarIndex)
    {
        if (_signaledLevels.Contains(levelId))
            return;
        _t3BarByLevel[levelId] = t3BarIndex;
        _prevTouchCount[levelId] = Math.Max(_prevTouchCount.GetValueOrDefault(levelId), 3);
    }

    /// <summary>
    /// Invocar inmediatamente después de <see cref="SupportResistanceEngine.ProcessClosedBar"/>.
    /// </summary>
    public IReadOnlyList<T3BreakoutSignal> ProcessAfterClosedBar(SupportResistanceEngine engine, OhlcBar bar)
    {
        var newly = new List<T3BreakoutSignal>();
        var presentIds = new HashSet<long>();

        foreach (var level in engine.Levels)
        {
            presentIds.Add(level.Id);

            var prevTouches = _prevTouchCount.GetValueOrDefault(level.Id, 0);
            if (level.IndependentTouchCount >= 3 && prevTouches < 3)
            {
                // Misma vela que invalidación: no hay elegibilidad posterior.
                if (level.State != LevelState.Invalidated)
                    _t3BarByLevel[level.Id] = bar.Index;
            }

            _prevTouchCount[level.Id] = level.IndependentTouchCount;
        }

        // Nivel absorbido por merge → caduca elegibilidad.
        var mergedAway = new List<long>();
        foreach (var id in _t3BarByLevel.Keys)
        {
            if (!presentIds.Contains(id))
                mergedAway.Add(id);
        }

        foreach (var id in mergedAway)
            _t3BarByLevel.Remove(id);

        foreach (var inv in engine.LastBarInvalidations)
        {
            var signal = TryCreateSignal(inv, _t3BarByLevel, _signaledLevels);
            if (signal == null)
                continue;

            _signaledLevels.Add(signal.LevelId);
            _t3BarByLevel.Remove(signal.LevelId);
            _signals.Add(signal);
            newly.Add(signal);
        }

        var toExpire = new List<long>();
        foreach (var kv in _t3BarByLevel)
        {
            if (bar.Index >= kv.Value + MaxBarsAfterT3)
                toExpire.Add(kv.Key);
        }

        foreach (var id in toExpire)
            _t3BarByLevel.Remove(id);

        return newly;
    }

    /// <summary>Reglas puras de emisión (testeables sin motor).</summary>
    public static T3BreakoutSignal? TryCreateSignal(
        StructuralInvalidationSnapshot inv,
        IReadOnlyDictionary<long, int> t3BarByLevel,
        ISet<long> alreadySignaled)
    {
        if (!inv.WasOhlcProxy)
            return null;
        if (alreadySignaled.Contains(inv.LevelId))
            return null;
        if (!t3BarByLevel.TryGetValue(inv.LevelId, out var t3Bar))
            return null;
        if (inv.BarIndex <= t3Bar)
            return null;
        if (inv.BarIndex > t3Bar + MaxBarsAfterT3)
            return null;
        if (inv.StateBefore is not (LevelState.Active or LevelState.Provisional))
            return null;

        return new T3BreakoutSignal
        {
            LevelId = inv.LevelId,
            LevelRole = inv.Role,
            Direction = inv.Role == LevelRole.Resistance ? SignalDirection.Buy : SignalDirection.Sell,
            T3BarIndex = t3Bar,
            BreakoutBarIndex = inv.BarIndex,
            BreakoutTime = inv.Time,
            ConfirmationClose = inv.Close,
            LevelMidAtBreakout = inv.Mid,
            UpperBoundAtBreakout = inv.UpperBound,
            LowerBoundAtBreakout = inv.LowerBound,
            HalfWidthAtBreakout = inv.HalfWidth,
            AtrAtBreakout = inv.AtrCurrent,
            AtrRefAtBreakout = inv.AtrRef,
            BreakoutReason = inv.ReasonTag,
            Geometry = inv.Geometry
        };
    }
}
