using System;
using System.Collections.Generic;

namespace cAlgo.Robots.SupportResistance;

/// <summary>
/// Detección determinista de swings sin lookahead.
/// Un swing en j se confirma solo cuando la barra j+K está cerrada.
/// Comparación estricta; empates → prevalece la ocurrencia más temprana.
/// </summary>
public sealed class SwingDetector
{
    private readonly int _k;
    private long _nextId = 1;
    private readonly List<SwingPoint> _swings = new();
    private int _lastProcessedClosedBar = -1;

    public SwingDetector(int strength = 3)
    {
        if (strength < 1)
            throw new ArgumentOutOfRangeException(nameof(strength));
        _k = strength;
    }

    public int Strength => _k;
    public IReadOnlyList<SwingPoint> Swings => _swings;

    public void Reset()
    {
        _swings.Clear();
        _nextId = 1;
        _lastProcessedClosedBar = -1;
    }

    /// <summary>
    /// Procesa la vela cerrada más reciente. <paramref name="bars"/> debe contener
    /// índices 0..closedBarIndex inclusive (solo cerradas).
    /// Devuelve los swings recién confirmados en esta barra.
    /// </summary>
    public List<SwingPoint> ProcessClosedBar(IReadOnlyList<OhlcBar> bars, int closedBarIndex)
    {
        var newly = new List<SwingPoint>();
        if (closedBarIndex < 0 || closedBarIndex >= bars.Count)
            return newly;

        // Solo avanzamos en orden cronológico.
        if (closedBarIndex <= _lastProcessedClosedBar)
            return newly;

        for (var i = _lastProcessedClosedBar + 1; i <= closedBarIndex; i++)
        {
            var candidate = i - _k;
            if (candidate < _k)
                continue;

            // Ventana completa candidate-K ... candidate+K debe existir y estar cerrada (candidate+K <= i).
            if (candidate + _k > i)
                continue;

            if (IsSwingHigh(bars, candidate))
            {
                var sp = Create(bars[candidate], SwingKind.High, confirmedAt: candidate + _k);
                _swings.Add(sp);
                newly.Add(sp);
            }

            if (IsSwingLow(bars, candidate))
            {
                var sp = Create(bars[candidate], SwingKind.Low, confirmedAt: candidate + _k);
                _swings.Add(sp);
                newly.Add(sp);
            }
        }

        _lastProcessedClosedBar = closedBarIndex;
        return newly;
    }

    private SwingPoint Create(OhlcBar bar, SwingKind kind, int confirmedAt)
    {
        return new SwingPoint
        {
            Id = _nextId++,
            Kind = kind,
            BarIndex = bar.Index,
            Time = bar.OpenTime,
            Price = kind == SwingKind.High ? bar.High : bar.Low,
            ConfirmedAtBar = confirmedAt
        };
    }

    private bool IsSwingHigh(IReadOnlyList<OhlcBar> bars, int j)
    {
        var pivot = bars[j].High;
        for (var i = j - _k; i <= j + _k; i++)
        {
            if (i == j)
                continue;
            // Estrictamente mayor: igualdad con un vecino invalida el pivote actual
            // → la ocurrencia más temprana que sí sea estricta prevalece.
            if (bars[i].High >= pivot)
                return false;
        }

        return true;
    }

    private bool IsSwingLow(IReadOnlyList<OhlcBar> bars, int j)
    {
        var pivot = bars[j].Low;
        for (var i = j - _k; i <= j + _k; i++)
        {
            if (i == j)
                continue;
            if (bars[i].Low <= pivot)
                return false;
        }

        return true;
    }
}
