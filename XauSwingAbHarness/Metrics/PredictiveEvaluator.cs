using System.Globalization;
using System.Text;
using cAlgo.Robots.SupportResistance;
using XauSwingAbHarness.Experiment;

namespace XauSwingAbHarness.Metrics;

/// <summary>
/// Metodología predictiva fijada a priori (sin usar OOS para elegir umbrales):
/// - Evento evaluado: primer TouchCountReached3 por nivel (NO es una operación real).
/// - Dirección: Resistance → favorable = subida; Support → favorable = bajada.
/// - Referencia: Close de la vela del evento; ATR del evento (si no finito, se omite).
/// - Horizonte fijo H ∈ {16,32} velas posteriores (sin incluir la del evento).
/// - MFE/MAE en múltiplos de ATR dentro del horizonte.
/// - Hit: alcanza +thr·ATR favorable antes que −thr·ATR adverso (orden por barra).
/// - InvalidationWithinHorizon: el nivel se invalida en (event, event+H] (si hay evento).
/// Sin relleno de huecos; sin doble conteo (1 evaluación por nivel y horizonte).
/// </summary>
public sealed class PredictiveEvalRow
{
    public required string VariantId { get; init; }
    public required int SwingK { get; init; }
    public required string Segment { get; init; }
    public required int HorizonBars { get; init; }
    public required long LevelId { get; init; }
    public required LevelRole Role { get; init; }
    public required DateTime EventTimeUtc { get; init; }
    public required int EventBarIndex { get; init; }
    public required double AtrRef { get; init; }
    public required double MfeAtr { get; init; }
    public required double MaeAtr { get; init; }
    public required bool HitBeforeAdverse { get; init; }
    public required bool InvalidatedWithinHorizon { get; init; }
}

public sealed class PredictiveSummary
{
    public required string VariantId { get; init; }
    public required int SwingK { get; init; }
    public required string Segment { get; init; }
    public required int HorizonBars { get; init; }
    public int Evaluated { get; init; }
    public int Hits { get; init; }
    public int Invalidated { get; init; }
    public double HitRate { get; init; }
    public double MeanMfeAtr { get; init; }
    public double MeanMaeAtr { get; init; }
}

public static class PredictiveEvaluator
{
    public static IReadOnlyList<PredictiveEvalRow> Evaluate(
        VariantRunResult run,
        IReadOnlyList<OhlcBar> bars,
        AbExperimentConfig config)
    {
        var t3 = run.Events
            .Where(e => e.Kind == EventKind.TouchCountReached3)
            .GroupBy(e => e.LevelId!.Value)
            .Select(g => g.OrderBy(e => e.BarIndex).First())
            .OrderBy(e => e.BarIndex)
            .ToList();

        var invalidations = run.Events
            .Where(e => e.Kind == EventKind.LevelInvalidated && e.LevelId.HasValue)
            .GroupBy(e => e.LevelId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(e => e.BarIndex));

        var rows = new List<PredictiveEvalRow>();
        foreach (var ev in t3)
        {
            if (!ev.AtrAtEvent.HasValue || !IsFinite(ev.AtrAtEvent.Value) || ev.AtrAtEvent.Value <= 0)
                continue;
            if (!ev.Role.HasValue)
                continue;

            foreach (var h in config.PredictiveHorizonsBars)
            {
                var row = EvaluateOne(ev, bars, h, config.ReactionThresholdAtr, invalidations, run.Events);
                if (row != null)
                    rows.Add(row);
            }
        }

        return rows;
    }

    public static IReadOnlyList<PredictiveSummary> Summarize(IEnumerable<PredictiveEvalRow> rows)
    {
        return rows
            .GroupBy(r => (r.VariantId, r.SwingK, r.Segment, r.HorizonBars))
            .Select(g =>
            {
                var list = g.ToList();
                return new PredictiveSummary
                {
                    VariantId = g.Key.VariantId,
                    SwingK = g.Key.SwingK,
                    Segment = g.Key.Segment,
                    HorizonBars = g.Key.HorizonBars,
                    Evaluated = list.Count,
                    Hits = list.Count(x => x.HitBeforeAdverse),
                    Invalidated = list.Count(x => x.InvalidatedWithinHorizon),
                    HitRate = list.Count == 0 ? 0 : list.Count(x => x.HitBeforeAdverse) / (double)list.Count,
                    MeanMfeAtr = list.Count == 0 ? 0 : list.Average(x => x.MfeAtr),
                    MeanMaeAtr = list.Count == 0 ? 0 : list.Average(x => x.MaeAtr)
                };
            })
            .OrderBy(s => s.VariantId)
            .ThenBy(s => s.Segment)
            .ThenBy(s => s.HorizonBars)
            .ToList();
    }

    public static string ToCsv(IEnumerable<PredictiveSummary> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "VariantId,SwingK,Segment,HorizonBars,Evaluated,Hits,Invalidated,HitRate,MeanMfeAtr,MeanMaeAtr");
        foreach (var r in rows)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.VariantId},{r.SwingK},{r.Segment},{r.HorizonBars},{r.Evaluated},{r.Hits}," +
                $"{r.Invalidated},{r.HitRate:F4},{r.MeanMfeAtr:F4},{r.MeanMaeAtr:F4}");
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static PredictiveEvalRow? EvaluateOne(
        ExperimentEvent ev,
        IReadOnlyList<OhlcBar> bars,
        int horizon,
        double thrAtr,
        Dictionary<long, int> invalidations,
        IReadOnlyList<ExperimentEvent> allEvents)
    {
        var i0 = ev.BarIndex;
        if (i0 < 0 || i0 >= bars.Count - 1)
            return null;

        var end = Math.Min(bars.Count - 1, i0 + horizon);
        if (end <= i0)
            return null;

        var atr = ev.AtrAtEvent!.Value;
        var entry = bars[i0].Close;
        var role = ev.Role!.Value;
        var thr = thrAtr * atr;

        double mfe = 0;
        double mae = 0;
        var hit = false;
        var adverseFirst = false;

        for (var i = i0 + 1; i <= end; i++)
        {
            var b = bars[i];
            double favMove;
            double advMove;
            if (role == LevelRole.Resistance)
            {
                // Breakout alcista anticipado.
                favMove = b.High - entry;
                advMove = entry - b.Low;
            }
            else
            {
                // Breakout bajista anticipado.
                favMove = entry - b.Low;
                advMove = b.High - entry;
            }

            mfe = Math.Max(mfe, favMove);
            mae = Math.Max(mae, advMove);

            if (!hit && !adverseFirst)
            {
                if (favMove >= thr)
                    hit = true;
                else if (advMove >= thr)
                    adverseFirst = true;
            }
        }

        var invalidated = false;
        if (ev.LevelId.HasValue && invalidations.TryGetValue(ev.LevelId.Value, out var invBar))
            invalidated = invBar > i0 && invBar <= end;

        // Segmento del evento (no del horizonte).
        var segment = ev.Segment;

        return new PredictiveEvalRow
        {
            VariantId = ev.VariantId,
            SwingK = ev.SwingK,
            Segment = segment,
            HorizonBars = horizon,
            LevelId = ev.LevelId!.Value,
            Role = role,
            EventTimeUtc = ev.OpenTimeUtc,
            EventBarIndex = i0,
            AtrRef = atr,
            MfeAtr = mfe / atr,
            MaeAtr = mae / atr,
            HitBeforeAdverse = hit && !adverseFirst,
            InvalidatedWithinHorizon = invalidated
        };
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
