using cAlgo.Robots.SupportResistance;

namespace XauSwingAbHarness.Structural;

public sealed class PostT3BarSnapshot
{
    public required string VariantId { get; init; }
    public required int SwingK { get; init; }
    public required long LevelId { get; init; }
    public required LevelRole Role { get; init; }
    public required LevelGeometry Geometry { get; init; }
    public required int T3BarIndex { get; init; }
    public required int BarIndex { get; init; }
    public required string Segment { get; init; }
    public required LevelState StateBefore { get; init; }
    public required LevelState StateAfter { get; init; }
    public required double Close { get; init; }
    public required double MidAtBar { get; init; }
    public required double UpperBoundAtBar { get; init; }
    public required double LowerBoundAtBar { get; init; }
    public required double HalfWidth { get; init; }
    public required double? AtrRef { get; init; }
    public required double AtrCurrent { get; init; }
    public required double AtrAtT3 { get; init; }
    public required bool MeetsExperimentalThreshold { get; init; }
    public required bool MeetsStructuralInvalidation { get; init; }
    public required string? InvalidationReason { get; init; }
    public required bool GeometryChanged { get; init; }
    public required string? GeometryChangeDetail { get; init; }
}

public enum BreakoutClass
{
    OnlyC = 0,
    AAndCSameBar = 1,
    AAndCDifferentBars = 2,
    OnlyA = 3,
    Neither = 4
}

public sealed record ClassifiedT3Event
{
    public required string VariantId { get; init; }
    public required int SwingK { get; init; }
    public required long LevelId { get; init; }
    public required LevelRole Role { get; init; }
    public required LevelGeometry Geometry { get; init; }
    public required int T3BarIndex { get; init; }
    public required string Segment { get; init; }
    public required double AtrAtT3 { get; init; }
    public required BreakoutClass Class { get; init; }
    public int? ConfirmBarA { get; init; }
    public int? ConfirmBarC { get; init; }
    public double? ConfirmCloseA { get; init; }
    public double? ConfirmCloseC { get; init; }
    public double? AtrAtConfirmA { get; init; }
    public double? AtrAtConfirmC { get; init; }
    public bool CensoredEndOfData { get; init; }
    public bool OutcomeWindowCrossesSegment { get; init; }
    public string? Detail { get; init; }
}
