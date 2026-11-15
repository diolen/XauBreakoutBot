using System;
using System.Collections.Generic;
using System.Linq;

namespace cAlgo.Robots.SupportResistance;

/// <summary>
/// Motor autónomo de detección S/R (sin órdenes ni señales de entrada).
/// Velas cerradas → swings/toques/fusiones; ticks → invalidación Bid/Ask y candidatos.
/// </summary>
public sealed class SupportResistanceEngine
{
    public const int DefaultSwingStrength = 3;
    public const int DefaultLookbackBars = 480;
    public const int DefaultAtrPeriod = 14;
    public const int MinDiagonalAnchorSeparation = 12;
    public const double ZoneToleranceAtr = 0.15;
    public const double BreakoutBufferAtr = 0.10;
    public const double RetreatAtr = 0.5;
    public const double SlopeNormMin = 0.5;
    public const double SlopeNormMax = 3.0;
    public const double DiagonalSlopeRelTolerance = 0.15;

    private readonly int _swingK;
    private readonly int _lookback;
    private readonly int _atrPeriod;
    private readonly AtrCalculator _atr;
    private readonly SwingDetector _swings;

    private readonly List<OhlcBar> _bars = new();
    private readonly List<SrLevel> _levels = new();
    private readonly HashSet<string> _diagonalKeys = new();
    private long _nextLevelId = 1;

    private CandidateSnapshot _candidates = new();
    private bool _insufficientHistoryReported;

    public SupportResistanceEngine(
        int swingStrength = DefaultSwingStrength,
        int lookbackBars = DefaultLookbackBars,
        int atrPeriod = DefaultAtrPeriod)
    {
        _swingK = swingStrength;
        _lookback = lookbackBars;
        _atrPeriod = atrPeriod;
        _atr = new AtrCalculator(atrPeriod);
        _swings = new SwingDetector(swingStrength);
    }

    public int PreloadBars => _atrPeriod + _swingK;
    public int MinimumPreferredBars => _lookback + PreloadBars;
    public bool HasInsufficientHistory { get; private set; }
    public double AtrCurrent => _atr.Current;
    public bool AtrReady => _atr.IsReady;
    public IReadOnlyList<SrLevel> Levels => _levels;
    public IReadOnlyList<SwingPoint> Swings => _swings.Swings;
    public CandidateSnapshot Candidates => _candidates;
    public int ClosedBarCount => _bars.Count;
    public string? HistoryWarning { get; private set; }

    public void Reset()
    {
        _bars.Clear();
        _levels.Clear();
        _diagonalKeys.Clear();
        _swings.Reset();
        _atr.Reset();
        _nextLevelId = 1;
        _candidates = new CandidateSnapshot();
        HasInsufficientHistory = false;
        HistoryWarning = null;
        _insufficientHistoryReported = false;
    }

    /// <summary>
    /// Replay determinista de velas cerradas. No usa ticks; invalidación OHLC = proxy por Close.
    /// </summary>
    public void ReplayClosedBars(IReadOnlyList<OhlcBar> closedBars, bool enforceLookbackWindow = true)
    {
        Reset();

        if (closedBars.Count < MinimumPreferredBars)
        {
            HasInsufficientHistory = true;
            HistoryWarning =
                $"Historial insuficiente: {closedBars.Count} velas cerradas; preferible ≥ {MinimumPreferredBars} " +
                $"(lookback {_lookback} + precarga {PreloadBars}). " +
                "No se reconstruyen niveles de antigüedad ilimitada. " +
                "El proxy OHLC de invalidación no reproduce eventos tick intradía.";
        }

        IReadOnlyList<OhlcBar> window = closedBars;
        if (enforceLookbackWindow && closedBars.Count > MinimumPreferredBars)
        {
            var start = closedBars.Count - MinimumPreferredBars;
            var slice = new List<OhlcBar>(MinimumPreferredBars);
            for (var i = start; i < closedBars.Count; i++)
            {
                var b = closedBars[i];
                slice.Add(new OhlcBar(i - start, b.OpenTime, b.Open, b.High, b.Low, b.Close));
            }

            window = slice;
        }
        else if (enforceLookbackWindow && closedBars.Count > 0)
        {
            // Reindexar desde 0 conservando orden.
            var slice = new List<OhlcBar>(closedBars.Count);
            for (var i = 0; i < closedBars.Count; i++)
            {
                var b = closedBars[i];
                slice.Add(new OhlcBar(i, b.OpenTime, b.Open, b.High, b.Low, b.Close));
            }

            window = slice;
        }

        for (var i = 0; i < window.Count; i++)
            ProcessClosedBar(window[i]);
    }

    public void ProcessClosedBar(OhlcBar bar)
    {
        if (_bars.Count != bar.Index)
        {
            // Exigir secuencia 0..n-1 sin huecos tras Reset/Replay.
            if (_bars.Count == 0 && bar.Index != 0)
                bar = new OhlcBar(0, bar.OpenTime, bar.Open, bar.High, bar.Low, bar.Close);
            else if (_bars.Count > 0 && bar.Index != _bars.Count)
                bar = new OhlcBar(_bars.Count, bar.OpenTime, bar.Open, bar.High, bar.Low, bar.Close);
        }

        _bars.Add(bar);
        var closedIndex = bar.Index;
        _atr.Update(bar.High, bar.Low, bar.Close);

        var newSwings = _swings.ProcessClosedBar(_bars, closedIndex);
        foreach (var swing in newSwings)
            OnNewSwing(swing, closedIndex);

        if (_atr.IsReady)
        {
            UpdateTouchesForBar(closedIndex);
            MergeHorizontalLevels(closedIndex);
            MergeDiagonalLevels(closedIndex);
            RebuildConfluence(closedIndex);
            InvalidateByOhlcProxy(closedIndex);
        }
    }

    /// <summary>Invalidación Bid/Ask y selección de candidatos en cada tick.</summary>
    public void ProcessTick(DateTime time, double bid, double ask)
    {
        if (!_atr.IsReady || double.IsNaN(_atr.Current) || _atr.Current <= 0)
        {
            _candidates = new CandidateSnapshot();
            return;
        }

        var atrCurrent = _atr.Current;
        var buffer = BreakoutBufferAtr * atrCurrent;

        foreach (var level in _levels)
        {
            if (level.State is not (LevelState.Active or LevelState.Provisional))
                continue;

            // Solo Active se invalida para candidatos; Provisional también puede invalidarse
            // si el precio ya lo rompió de forma clara (misma regla geométrica).
            var upper = level.UpperBoundAtTime(time);
            var lower = level.LowerBoundAtTime(time);

            if (level.Role == LevelRole.Resistance && bid > upper + buffer)
            {
                Invalidate(level, time, bid, buffer, "tick-bid-resistance", ohlcProxy: false);
            }
            else if (level.Role == LevelRole.Support && ask < lower - buffer)
            {
                Invalidate(level, time, ask, buffer, "tick-ask-support", ohlcProxy: false);
            }
        }

        _candidates = SelectCandidates(bid, ask);
    }

    public SrLevel? GetNearestResistanceAbove(double ask) =>
        SelectNearest(LevelRole.Resistance, above: true, referencePrice: ask);

    public SrLevel? GetNearestSupportBelow(double bid) =>
        SelectNearest(LevelRole.Support, above: false, referencePrice: bid);

    private void OnNewSwing(SwingPoint swing, int closedIndex)
    {
        if (!_atr.IsReady)
            return;

        var atr = _atr.Current;
        if (double.IsNaN(atr) || atr <= 0)
            return;

        var role = swing.Kind == SwingKind.High ? LevelRole.Resistance : LevelRole.Support;
        CreateOrAttachHorizontal(swing, role, atr, closedIndex);
        TryCreateDiagonals(swing, role, atr, closedIndex);
    }

    private void CreateOrAttachHorizontal(SwingPoint swing, LevelRole role, double atrCurrent, int closedIndex)
    {
        var half = ZoneToleranceAtr * atrCurrent;
        SrLevel? attach = null;

        foreach (var level in _levels)
        {
            if (level.Geometry != LevelGeometry.Horizontal || level.Role != role)
                continue;
            if (level.State == LevelState.Invalidated)
                continue;

            var hw = level.State == LevelState.Active && level.AtrRef.HasValue
                ? level.HalfWidth
                : half;
            var low = level.Center - hw;
            var high = level.Center + hw;
            // Inclusivo: el extremo del swing toca o entra en la zona.
            if (swing.Price >= low && swing.Price <= high)
            {
                attach = level;
                break;
            }
        }

        if (attach == null)
        {
            var level = new SrLevel
            {
                Id = _nextLevelId++,
                Geometry = LevelGeometry.Horizontal,
                Role = role,
                State = LevelState.Provisional,
                Center = swing.Price,
                HalfWidth = half,
                CreatedAt = swing.Time,
                UpdatedAt = swing.Time
            };
            level.AssociatedExtrema.Add(swing.Price);
            RegisterIndependentTouch(level, swing.BarIndex, swing.Time, swing.Price, atrCurrent, closedIndex, forceFirst: true);
            _levels.Add(level);
            return;
        }

        attach.AssociatedExtrema.Add(swing.Price);
        if (attach.State != LevelState.Active)
        {
            attach.Center = Median(attach.AssociatedExtrema);
            attach.HalfWidth = half;
        }
        else
        {
            attach.Center = Median(attach.AssociatedExtrema);
            // HalfWidth y ATR_ref permanecen congelados.
        }

        RegisterIndependentTouch(attach, swing.BarIndex, swing.Time, swing.Price, atrCurrent, closedIndex, forceFirst: false);
        attach.UpdatedAt = swing.Time;
    }

    /// <summary>
    /// Construye como máximo una diagonal por swing nuevo: pivote previo del mismo tipo
    /// (consecutivo), si cumple separación y pendiente. Evita el emparejamiento O(N²).
    /// </summary>
    private void TryCreateDiagonals(SwingPoint swing, LevelRole role, double atrCurrent, int closedIndex)
    {
        var older = FindPreviousSwingOfKind(swing);
        if (older == null)
            return;

        var sep = swing.BarIndex - older.BarIndex;
        if (sep < MinDiagonalAnchorSeparation)
            return;

        var key = older.BarIndex + ":" + swing.BarIndex + ":" + (int)role;
        if (_diagonalKeys.Contains(key))
            return;

        var slopePerBar = (swing.Price - older.Price) / sep;
        var slopeNorm = Math.Abs(slopePerBar) * 100.0 / atrCurrent;
        if (slopeNorm < SlopeNormMin || slopeNorm > SlopeNormMax)
            return;

        _diagonalKeys.Add(key);

        var level = new SrLevel
        {
            Id = _nextLevelId++,
            Geometry = LevelGeometry.Diagonal,
            Role = role,
            State = LevelState.Active,
            Center = swing.Price,
            HalfWidth = ZoneToleranceAtr * atrCurrent,
            AtrRef = atrCurrent,
            SlopePerBar = slopePerBar,
            IndependentTouchCount = 2,
            RetreatConfirmed = false,
            InTouchEpisode = true,
            CreatedAt = swing.Time,
            UpdatedAt = swing.Time
        };
        level.Anchors.Add(new AnchorPoint { BarIndex = older.BarIndex, Time = older.Time, Price = older.Price });
        level.Anchors.Add(new AnchorPoint { BarIndex = swing.BarIndex, Time = swing.Time, Price = swing.Price });
        level.AssociatedExtrema.Add(older.Price);
        level.AssociatedExtrema.Add(swing.Price);
        level.Touches.Add(new TouchRecord
        {
            BarIndex = older.BarIndex,
            Time = older.Time,
            Price = older.Price,
            IsIndependent = true
        });
        level.Touches.Add(new TouchRecord
        {
            BarIndex = swing.BarIndex,
            Time = swing.Time,
            Price = swing.Price,
            IsIndependent = true
        });

        _levels.Add(level);
    }

    /// <summary>Último swing del mismo tipo con BarIndex estrictamente anterior.</summary>
    private SwingPoint? FindPreviousSwingOfKind(SwingPoint swing)
    {
        var swings = _swings.Swings;
        for (var i = swings.Count - 1; i >= 0; i--)
        {
            var s = swings[i];
            if (s.Id == swing.Id || s.Kind != swing.Kind)
                continue;
            if (s.BarIndex >= swing.BarIndex)
                continue;
            return s;
        }

        return null;
    }

    private void UpdateTouchesForBar(int closedIndex)
    {
        var bar = _bars[closedIndex];
        var atr = _atr.Current;
        if (double.IsNaN(atr) || atr <= 0)
            return;

        foreach (var level in _levels)
        {
            if (level.State == LevelState.Invalidated)
                continue;

            EnsureOperationalHalfWidth(level, atr);

            var lower = level.LowerBoundAtBar(closedIndex);
            var upper = level.UpperBoundAtBar(closedIndex);
            var touchPrice = level.Role == LevelRole.Resistance ? bar.High : bar.Low;
            // Mecha alcanza la zona (intersección High-Low con [lower,upper], inclusiva).
            var intersects = bar.Low <= upper && bar.High >= lower;

            if (!level.RetreatConfirmed && level.IndependentTouchCount > 0)
            {
                if (IsRetreatConfirmed(level, bar.Close, lower, upper, atr))
                {
                    level.RetreatConfirmed = true;
                    level.RetreatConfirmedAtBar = closedIndex;
                    // El alejamiento cierra el episodio de toque anterior.
                    level.InTouchEpisode = false;
                }
            }

            if (intersects)
            {
                if (!level.InTouchEpisode)
                {
                    if (level.IndependentTouchCount == 0 || level.RetreatConfirmed)
                    {
                        var registered = RegisterIndependentTouch(
                            level, closedIndex, bar.OpenTime, touchPrice, atr, closedIndex, forceFirst: false);
                        if (registered)
                        {
                            // InTouchEpisode ya queda true dentro de RegisterIndependentTouch.
                        }
                        else if (IsSameBarAsRetreatConfirmation(level, closedIndex))
                        {
                            // Retreat confirmado en esta vela: no abrir episodio sin conteo;
                            // el toque solo podrá registrarse en una vela posterior.
                        }
                        else
                        {
                            // Rechazo por duplicado de barIndex u otro: evitar reintentos cada barra.
                            level.InTouchEpisode = true;
                        }
                    }
                    else
                    {
                        // En zona sin retreat previo: episodio sin conteo adicional.
                        level.InTouchEpisode = true;
                    }
                }
            }
            else
            {
                level.InTouchEpisode = false;
            }

            level.UpdatedAt = bar.OpenTime;
        }
    }

    private static bool IsSameBarAsRetreatConfirmation(SrLevel level, int barIndex) =>
        level.RetreatConfirmed &&
        level.RetreatConfirmedAtBar.HasValue &&
        level.RetreatConfirmedAtBar.Value == barIndex;

    private static bool IsRetreatConfirmed(SrLevel level, double close, double lower, double upper, double atr)
    {
        var dist = RetreatAtr * atr;
        if (level.Role == LevelRole.Resistance)
            return close <= lower - dist;
        return close >= upper + dist;
    }

    /// <returns>true si se registró un toque independiente nuevo.</returns>
    private bool RegisterIndependentTouch(
        SrLevel level,
        int barIndex,
        DateTime time,
        double price,
        double atrCurrent,
        int closedIndex,
        bool forceFirst)
    {
        // Evitar doble conteo del mismo barIndex.
        for (var i = 0; i < level.Touches.Count; i++)
        {
            if (level.Touches[i].IsIndependent && level.Touches[i].BarIndex == barIndex)
                return false;
        }

        if (!forceFirst && level.IndependentTouchCount > 0)
        {
            if (!level.RetreatConfirmed)
                return false;

            // El toque debe ser en una vela estrictamente posterior a la del retreat.
            if (level.RetreatConfirmedAtBar.HasValue && barIndex <= level.RetreatConfirmedAtBar.Value)
                return false;
        }

        level.Touches.Add(new TouchRecord
        {
            BarIndex = barIndex,
            Time = time,
            Price = price,
            IsIndependent = true
        });
        level.IndependentTouchCount++;
        level.RetreatConfirmed = false;
        level.RetreatConfirmedAtBar = null;
        level.InTouchEpisode = true;
        level.UpdatedAt = time;

        if (level.State == LevelState.Provisional && level.IndependentTouchCount >= 2)
            Activate(level, atrCurrent, time);

        return true;
    }

    private static void Activate(SrLevel level, double atrCurrent, DateTime time)
    {
        level.State = LevelState.Active;
        level.AtrRef = atrCurrent;
        level.HalfWidth = ZoneToleranceAtr * atrCurrent;
        if (level.Geometry == LevelGeometry.Horizontal)
            level.Center = Median(level.AssociatedExtrema);
        level.UpdatedAt = time;
    }

    private void EnsureOperationalHalfWidth(SrLevel level, double atrCurrent)
    {
        if (level.State == LevelState.Active && level.AtrRef.HasValue)
        {
            level.HalfWidth = ZoneToleranceAtr * level.AtrRef.Value;
            return;
        }

        level.HalfWidth = ZoneToleranceAtr * atrCurrent;
    }

    private void MergeHorizontalLevels(int closedIndex)
    {
        bool merged;
        do
        {
            merged = false;
            for (var i = 0; i < _levels.Count; i++)
            {
                var a = _levels[i];
                if (a.Geometry != LevelGeometry.Horizontal || a.State == LevelState.Invalidated)
                    continue;

                for (var j = i + 1; j < _levels.Count; j++)
                {
                    var b = _levels[j];
                    if (b.Geometry != LevelGeometry.Horizontal || b.Role != a.Role || b.State == LevelState.Invalidated)
                        continue;

                    EnsureOperationalHalfWidth(a, _atr.Current);
                    EnsureOperationalHalfWidth(b, _atr.Current);

                    var aLow = a.Center - a.HalfWidth;
                    var aHigh = a.Center + a.HalfWidth;
                    var bLow = b.Center - b.HalfWidth;
                    var bHigh = b.Center + b.HalfWidth;

                    // Decisión 12: solape inclusivo (contacto exacto incluido).
                    if (!(aLow <= bHigh && bLow <= aHigh))
                        continue;

                    var survivor = PreferSurvivor(a, b);
                    var absorbed = ReferenceEquals(survivor, a) ? b : a;
                    AbsorbHorizontal(survivor, absorbed);
                    merged = true;
                    break;
                }

                if (merged)
                    break;
            }
        } while (merged);
    }

    private static SrLevel PreferSurvivor(SrLevel a, SrLevel b)
    {
        if (a.State == LevelState.Active && b.State != LevelState.Active)
            return a;
        if (b.State == LevelState.Active && a.State != LevelState.Active)
            return b;
        return a.Id <= b.Id ? a : b;
    }

    private void AbsorbHorizontal(SrLevel survivor, SrLevel absorbed)
    {
        if (ReferenceEquals(survivor, absorbed))
            return;

        foreach (var e in absorbed.AssociatedExtrema)
            survivor.AssociatedExtrema.Add(e);

        MergeTouches(survivor, absorbed);

        survivor.Center = Median(survivor.AssociatedExtrema);

        if (survivor.State == LevelState.Active)
        {
            // HalfWidth / ATR_ref congelados.
            if (!survivor.AtrRef.HasValue && absorbed.AtrRef.HasValue)
            {
                survivor.AtrRef = absorbed.AtrRef;
                survivor.HalfWidth = ZoneToleranceAtr * survivor.AtrRef.Value;
            }
        }
        else if (absorbed.State == LevelState.Active)
        {
            survivor.State = LevelState.Active;
            survivor.AtrRef = absorbed.AtrRef;
            survivor.HalfWidth = absorbed.HalfWidth;
        }
        else
        {
            survivor.HalfWidth = ZoneToleranceAtr * _atr.Current;
        }

        if (survivor.IndependentTouchCount >= 2 && survivor.State == LevelState.Provisional)
            Activate(survivor, _atr.Current, survivor.UpdatedAt);

        survivor.UpdatedAt = _bars[^1].OpenTime;
        _levels.Remove(absorbed);
    }

    private void MergeDiagonalLevels(int closedIndex)
    {
        bool merged;
        do
        {
            merged = false;
            for (var i = 0; i < _levels.Count; i++)
            {
                var a = _levels[i];
                if (a.Geometry != LevelGeometry.Diagonal || a.State == LevelState.Invalidated)
                    continue;

                for (var j = i + 1; j < _levels.Count; j++)
                {
                    var b = _levels[j];
                    if (b.Geometry != LevelGeometry.Diagonal || b.Role != a.Role || b.State == LevelState.Invalidated)
                        continue;

                    if (Math.Sign(a.SlopePerBar) != Math.Sign(b.SlopePerBar) &&
                        !(Math.Abs(a.SlopePerBar) < 1e-12 && Math.Abs(b.SlopePerBar) < 1e-12))
                        continue;

                    var maxAbs = Math.Max(Math.Abs(a.SlopePerBar), Math.Abs(b.SlopePerBar));
                    if (maxAbs > 1e-12)
                    {
                        var rel = Math.Abs(a.SlopePerBar - b.SlopePerBar) / maxAbs;
                        if (rel > DiagonalSlopeRelTolerance)
                            continue;
                    }

                    var pa = a.ProjectPriceAtBar(closedIndex);
                    var pb = b.ProjectPriceAtBar(closedIndex);
                    if (Math.Abs(pa - pb) > ZoneToleranceAtr * _atr.Current)
                        continue;

                    var survivor = PreferSurvivor(a, b);
                    var absorbed = ReferenceEquals(survivor, a) ? b : a;

                    // Nunca modificar anclajes/pendiente de un Active.
                    if (survivor.State == LevelState.Active)
                    {
                        MergeTouches(survivor, absorbed);
                        survivor.UpdatedAt = _bars[^1].OpenTime;
                        _levels.Remove(absorbed);
                    }
                    else if (absorbed.State == LevelState.Active)
                    {
                        MergeTouches(absorbed, survivor);
                        absorbed.UpdatedAt = _bars[^1].OpenTime;
                        _levels.Remove(survivor);
                    }
                    else
                    {
                        // Ambos Provisional: conservar el de Id más antiguo; geometría del survivor.
                        MergeTouches(survivor, absorbed);
                        survivor.UpdatedAt = _bars[^1].OpenTime;
                        _levels.Remove(absorbed);
                    }

                    merged = true;
                    break;
                }

                if (merged)
                    break;
            }
        } while (merged);
    }

    private static void MergeTouches(SrLevel survivor, SrLevel absorbed)
    {
        var seen = new HashSet<int>();
        foreach (var t in survivor.Touches)
        {
            if (t.IsIndependent)
                seen.Add(t.BarIndex);
        }

        foreach (var t in absorbed.Touches)
        {
            if (!t.IsIndependent)
                continue;
            if (seen.Contains(t.BarIndex))
                continue;
            survivor.Touches.Add(t);
            seen.Add(t.BarIndex);
            survivor.IndependentTouchCount++;
        }

        // Recalcular conteo desde touches independientes únicos.
        var unique = new HashSet<int>();
        var count = 0;
        foreach (var t in survivor.Touches)
        {
            if (!t.IsIndependent || !unique.Add(t.BarIndex))
                continue;
            count++;
        }

        survivor.IndependentTouchCount = count;
        if (absorbed.RetreatConfirmed)
        {
            if (!survivor.RetreatConfirmed)
            {
                survivor.RetreatConfirmed = true;
                survivor.RetreatConfirmedAtBar = absorbed.RetreatConfirmedAtBar;
            }
            else if (absorbed.RetreatConfirmedAtBar.HasValue)
            {
                // Conservar la confirmación más reciente (más restrictiva para el siguiente toque).
                if (!survivor.RetreatConfirmedAtBar.HasValue ||
                    absorbed.RetreatConfirmedAtBar.Value > survivor.RetreatConfirmedAtBar.Value)
                {
                    survivor.RetreatConfirmedAtBar = absorbed.RetreatConfirmedAtBar;
                }
            }
        }
    }

    private void RebuildConfluence(int closedIndex)
    {
        foreach (var level in _levels)
            level.ConfluenceIds.Clear();

        for (var i = 0; i < _levels.Count; i++)
        {
            var a = _levels[i];
            if (a.State != LevelState.Active)
                continue;

            for (var j = i + 1; j < _levels.Count; j++)
            {
                var b = _levels[j];
                if (b.State != LevelState.Active)
                    continue;

                // Solo confluencia H ↔ D.
                if (a.Geometry == b.Geometry)
                    continue;
                if (a.Role != b.Role)
                    continue;

                EnsureOperationalHalfWidth(a, _atr.Current);
                EnsureOperationalHalfWidth(b, _atr.Current);

                var aLow = a.LowerBoundAtBar(closedIndex);
                var aHigh = a.UpperBoundAtBar(closedIndex);
                var bLow = b.LowerBoundAtBar(closedIndex);
                var bHigh = b.UpperBoundAtBar(closedIndex);

                if (aLow <= bHigh && bLow <= aHigh)
                {
                    if (!a.ConfluenceIds.Contains(b.Id))
                        a.ConfluenceIds.Add(b.Id);
                    if (!b.ConfluenceIds.Contains(a.Id))
                        b.ConfluenceIds.Add(a.Id);
                }
            }
        }
    }

    private void InvalidateByOhlcProxy(int closedIndex)
    {
        var bar = _bars[closedIndex];
        var atr = _atr.Current;
        if (double.IsNaN(atr) || atr <= 0)
            return;

        var buffer = BreakoutBufferAtr * atr;

        foreach (var level in _levels)
        {
            if (level.State is not (LevelState.Active or LevelState.Provisional))
                continue;

            var upper = level.UpperBoundAtBar(closedIndex);
            var lower = level.LowerBoundAtBar(closedIndex);

            // Proxy determinista por Close (no Bid/Ask). Limitación documentada.
            if (level.Role == LevelRole.Resistance && bar.Close > upper + buffer)
            {
                Invalidate(level, bar.OpenTime, bar.Close, buffer, "ohlc-close-proxy-resistance", ohlcProxy: true);
            }
            else if (level.Role == LevelRole.Support && bar.Close < lower - buffer)
            {
                Invalidate(level, bar.OpenTime, bar.Close, buffer, "ohlc-close-proxy-support", ohlcProxy: true);
            }
        }
    }

    private static void Invalidate(SrLevel level, DateTime time, double price, double buffer, string tag, bool ohlcProxy)
    {
        level.State = LevelState.Invalidated;
        level.Invalidation = new InvalidationInfo
        {
            Time = time,
            Price = price,
            RuleTag = tag,
            BreakoutBuffer = buffer,
            WasOhlcProxy = ohlcProxy
        };
        level.UpdatedAt = time;
        level.ConfluenceIds.Clear();
    }

    private CandidateSnapshot SelectCandidates(double bid, double ask)
    {
        return new CandidateSnapshot
        {
            NearestResistanceAboveAsk = SelectNearest(LevelRole.Resistance, above: true, referencePrice: ask),
            NearestSupportBelowBid = SelectNearest(LevelRole.Support, above: false, referencePrice: bid)
        };
    }

    private SrLevel? SelectNearest(LevelRole role, bool above, double referencePrice)
    {
        SrLevel? best = null;
        var bestDist = double.MaxValue;
        var nowBar = _bars.Count > 0 ? _bars[^1].Index : 0;

        foreach (var level in _levels)
        {
            if (level.State != LevelState.Active || level.Role != role)
                continue;

            var px = level.Geometry == LevelGeometry.Horizontal
                ? level.Center
                : level.ProjectPriceAtBar(nowBar);

            if (above)
            {
                if (px <= referencePrice)
                    continue;
            }
            else
            {
                if (px >= referencePrice)
                    continue;
            }

            var dist = Math.Abs(px - referencePrice);
            if (dist < bestDist - 1e-12 ||
                (Math.Abs(dist - bestDist) <= 1e-12 && (best == null || level.Id < best.Id)))
            {
                bestDist = dist;
                best = level;
            }
        }

        return best;
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
            return double.NaN;
        var sorted = values.ToArray();
        Array.Sort(sorted);
        var mid = sorted.Length / 2;
        if (sorted.Length % 2 == 1)
            return sorted[mid];
        return 0.5 * (sorted[mid - 1] + sorted[mid]);
    }

    public string ConsumeHistoryWarningOnce()
    {
        if (_insufficientHistoryReported || string.IsNullOrEmpty(HistoryWarning))
            return "";
        _insufficientHistoryReported = true;
        return HistoryWarning;
    }
}
