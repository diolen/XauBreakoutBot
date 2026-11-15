using System;
using System.Collections.Generic;

namespace cAlgo.Robots.SupportResistance;

public enum LevelRole
{
    Support = 0,
    Resistance = 1
}

public enum LevelGeometry
{
    Horizontal = 0,
    Diagonal = 1
}

public enum LevelState
{
    Provisional = 0,
    Active = 1,
    Invalidated = 2
}

public enum SwingKind
{
    High = 0,
    Low = 1
}

public sealed class SwingPoint
{
    public long Id { get; init; }
    public SwingKind Kind { get; init; }
    public int BarIndex { get; init; }
    public DateTime Time { get; init; }
    public double Price { get; init; }
    public int ConfirmedAtBar { get; init; }
}

public sealed class TouchRecord
{
    public int BarIndex { get; init; }
    public DateTime Time { get; init; }
    public double Price { get; init; }
    public bool IsIndependent { get; init; }
}

public sealed class AnchorPoint
{
    public int BarIndex { get; init; }
    public DateTime Time { get; init; }
    public double Price { get; init; }
}

public sealed class InvalidationInfo
{
    public DateTime Time { get; init; }
    public double Price { get; init; }
    public string RuleTag { get; init; } = "";
    public double BreakoutBuffer { get; init; }
    public bool WasOhlcProxy { get; init; }
}

/// <summary>Nivel mutable gestionado por el motor S/R.</summary>
public sealed class SrLevel
{
    public long Id { get; init; }
    public LevelGeometry Geometry { get; init; }
    public LevelRole Role { get; set; }
    public LevelState State { get; set; }

    public double Center { get; set; }
    public double HalfWidth { get; set; }
    public double? AtrRef { get; set; }

    public List<double> AssociatedExtrema { get; } = new();
    public List<AnchorPoint> Anchors { get; } = new();
    public List<TouchRecord> Touches { get; } = new();
    public List<long> ConfluenceIds { get; } = new();

    /// <summary>Pendiente en precio por barra M15 (diagonales).</summary>
    public double SlopePerBar { get; set; }

    public int IndependentTouchCount { get; set; }
    public bool InTouchEpisode { get; set; }
    public bool RetreatConfirmed { get; set; }

    /// <summary>
    /// Índice de la vela M15 cerrada cuyo Close confirmó el alejamiento (0.5×ATR).
    /// Un nuevo toque independiente solo es válido en una vela estrictamente posterior.
    /// </summary>
    public int? RetreatConfirmedAtBar { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public InvalidationInfo? Invalidation { get; set; }

    public double ProjectPriceAtBar(int barIndex)
    {
        if (Geometry == LevelGeometry.Horizontal || Anchors.Count < 2)
            return Center;

        var a0 = Anchors[0];
        return a0.Price + SlopePerBar * (barIndex - a0.BarIndex);
    }

    public double ProjectPriceAtTime(DateTime time)
    {
        if (Geometry == LevelGeometry.Horizontal || Anchors.Count < 2)
            return Center;

        var a0 = Anchors[0];
        var a1 = Anchors[1];
        var totalMinutes = (a1.Time - a0.Time).TotalMinutes;
        if (Math.Abs(totalMinutes) < 1e-9)
            return a0.Price;

        var elapsed = (time - a0.Time).TotalMinutes;
        return a0.Price + (a1.Price - a0.Price) * (elapsed / totalMinutes);
    }

    public double LowerBoundAtBar(int barIndex) => ProjectPriceAtBar(barIndex) - HalfWidth;
    public double UpperBoundAtBar(int barIndex) => ProjectPriceAtBar(barIndex) + HalfWidth;
    public double LowerBoundAtTime(DateTime time) => ProjectPriceAtTime(time) - HalfWidth;
    public double UpperBoundAtTime(DateTime time) => ProjectPriceAtTime(time) + HalfWidth;
}

public readonly struct OhlcBar
{
    public OhlcBar(int index, DateTime openTime, double open, double high, double low, double close)
    {
        Index = index;
        OpenTime = openTime;
        Open = open;
        High = high;
        Low = low;
        Close = close;
    }

    public int Index { get; }
    public DateTime OpenTime { get; }
    public double Open { get; }
    public double High { get; }
    public double Low { get; }
    public double Close { get; }
}

public sealed class CandidateSnapshot
{
    public SrLevel? NearestResistanceAboveAsk { get; init; }
    public SrLevel? NearestSupportBelowBid { get; init; }
}
