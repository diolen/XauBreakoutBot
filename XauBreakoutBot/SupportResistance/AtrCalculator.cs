using System;
using System.Collections.Generic;

namespace cAlgo.Robots.SupportResistance;

/// <summary>ATR de Wilder calculado solo con velas cerradas.</summary>
public sealed class AtrCalculator
{
    private readonly int _period;
    private readonly List<double> _atrByClosedBar = new();
    private double _prevClose;
    private bool _hasPrevClose;
    private readonly List<double> _seedTr = new();
    private double _currentAtr;
    private bool _ready;

    public AtrCalculator(int period = 14)
    {
        if (period < 1)
            throw new ArgumentOutOfRangeException(nameof(period));
        _period = period;
    }

    public bool IsReady => _ready;
    public double Current => _ready ? _currentAtr : double.NaN;
    public int Period => _period;

    public void Reset()
    {
        _atrByClosedBar.Clear();
        _seedTr.Clear();
        _hasPrevClose = false;
        _prevClose = 0;
        _currentAtr = 0;
        _ready = false;
    }

    /// <summary>Incorpora una vela cerrada. El valor ATR resultante corresponde a ese índice cerrado.</summary>
    public double Update(double high, double low, double close)
    {
        double tr;
        if (!_hasPrevClose)
        {
            tr = high - low;
            _hasPrevClose = true;
        }
        else
        {
            tr = Math.Max(high - low, Math.Max(Math.Abs(high - _prevClose), Math.Abs(low - _prevClose)));
        }

        _prevClose = close;

        if (!_ready)
        {
            _seedTr.Add(tr);
            if (_seedTr.Count < _period)
            {
                _atrByClosedBar.Add(double.NaN);
                return double.NaN;
            }

            _currentAtr = 0;
            for (var i = 0; i < _seedTr.Count; i++)
                _currentAtr += _seedTr[i];
            _currentAtr /= _period;
            _ready = true;
            _atrByClosedBar.Add(_currentAtr);
            return _currentAtr;
        }

        _currentAtr = ((_currentAtr * (_period - 1)) + tr) / _period;
        _atrByClosedBar.Add(_currentAtr);
        return _currentAtr;
    }

    public double GetAtrAtClosedBar(int closedBarIndex)
    {
        if (closedBarIndex < 0 || closedBarIndex >= _atrByClosedBar.Count)
            return double.NaN;
        return _atrByClosedBar[closedBarIndex];
    }
}
