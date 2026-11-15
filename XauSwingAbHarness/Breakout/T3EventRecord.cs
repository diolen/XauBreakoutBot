using cAlgo.Robots.SupportResistance;
using XauSwingAbHarness.Experiment;

namespace XauSwingAbHarness.Breakout;

public sealed class T3EventRecord
{
    public required string VariantId { get; init; }
    public required int SwingK { get; init; }
    public required long LevelId { get; init; }
    public required int T3BarIndex { get; init; }
    public required DateTime T3OpenTimeUtc { get; init; }
    public required string Segment { get; init; }
    public required LevelGeometrySnapshot Geometry { get; init; }
    public required double AtrAtT3 { get; init; }
    public required double CloseAtT3 { get; init; }
    public int? InvalidationBarIndex { get; set; }
}

public sealed class RichVariantResult
{
    public required VariantRunResult Base { get; init; }
    public required IReadOnlyList<T3EventRecord> T3Events { get; init; }
    public required IReadOnlyList<double> AtrByBar { get; init; }
}
