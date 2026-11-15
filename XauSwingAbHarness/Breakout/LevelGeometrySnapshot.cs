using cAlgo.Robots.SupportResistance;

namespace XauSwingAbHarness.Breakout;

/// <summary>
/// Copia causal del nivel en el momento T=3 para proyectar precio sin reconsultar el motor.
/// Diagonales: Anchor0 + SlopePerBar*(bar - Anchor0) ≡ ProjectPriceAtBar.
/// </summary>
public sealed class LevelGeometrySnapshot
{
    public required long LevelId { get; init; }
    public required LevelGeometry Geometry { get; init; }
    public required LevelRole Role { get; init; }
    public required double Center { get; init; }
    public required double SlopePerBar { get; init; }
    public required int Anchor0BarIndex { get; init; }
    public required double Anchor0Price { get; init; }
    public required bool HasDiagonalAnchors { get; init; }

    public static LevelGeometrySnapshot FromLevel(SrLevel level)
    {
        var hasAnchors = level.Geometry == LevelGeometry.Diagonal && level.Anchors.Count >= 1;
        var a0 = hasAnchors ? level.Anchors[0] : null;
        return new LevelGeometrySnapshot
        {
            LevelId = level.Id,
            Geometry = level.Geometry,
            Role = level.Role,
            Center = level.Center,
            SlopePerBar = level.SlopePerBar,
            Anchor0BarIndex = a0?.BarIndex ?? 0,
            Anchor0Price = a0?.Price ?? level.Center,
            HasDiagonalAnchors = hasAnchors
        };
    }

    public double PriceAtBar(int barIndex)
    {
        if (Geometry == LevelGeometry.Horizontal || !HasDiagonalAnchors)
            return Center;
        return Anchor0Price + SlopePerBar * (barIndex - Anchor0BarIndex);
    }

    public bool CanEvaluateBreakout =>
        Geometry == LevelGeometry.Horizontal || HasDiagonalAnchors;
}
