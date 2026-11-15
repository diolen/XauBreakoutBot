using cAlgo.Robots.SupportResistance;

namespace XauSwingAbHarness.Experiment;

public enum EventKind
{
    SwingConfirmed = 0,
    LevelCreated = 1,
    LevelActivated = 2,
    IndependentTouch = 3,
    TouchCountReached3 = 4,
    LevelInvalidated = 5,
    EntryCandidateT2 = 6
}

public enum Observability
{
    /// <summary>Observable al cierre de la vela sin futuro.</summary>
    RealtimeObservable = 0,
    /// <summary>Evaluación retrospectiva con horizonte futuro (solo análisis).</summary>
    RetrospectiveEval = 1
}

public sealed class ExperimentEvent
{
    public required string VariantId { get; init; }
    public required int SwingK { get; init; }
    public required EventKind Kind { get; init; }
    public required Observability Observability { get; init; }
    public required int BarIndex { get; init; }
    public required DateTime OpenTimeUtc { get; init; }
    public required string Segment { get; init; }
    public long? LevelId { get; init; }
    public long? SwingId { get; init; }
    public LevelRole? Role { get; init; }
    public LevelGeometry? Geometry { get; init; }
    public int? TouchCount { get; init; }
    public int? ConfirmationLatencyBars { get; init; }
    public double? Price { get; init; }
    public double? AtrAtEvent { get; init; }
    public string? Detail { get; init; }
}
