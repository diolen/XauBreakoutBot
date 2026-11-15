namespace XauSwingAbHarness.Metrics;

public enum OhlcAmbiguityPolicy
{
    Optimistic = 0,
    Conservative = 1,
    ExcludeAmbiguous = 2
}

public enum ReactionOutcome
{
    Hit = 0,
    Miss = 1,
    AmbiguousSameBar = 2,
    Unresolved = 3
}

public readonly record struct SignedMove(double Favorable, double Adverse);

public sealed class ReactionEvaluation
{
    public required ReactionOutcome Outcome { get; init; }
    public required OhlcAmbiguityPolicy Policy { get; init; }
    public required double MfeAtr { get; init; }
    public required double MaeAtr { get; init; }
    public required int? ResolutionBarOffset { get; init; }
    public required bool SameBarBothThresholds { get; init; }
}

/// <summary>
/// Evaluación de trayectoria OHLC post-evento con políticas explícitas de ambigüedad.
/// Miss = adverso alcanza umbral primero (o primero bajo Conservative).
/// Unresolved = ningún umbral alcanzado en el horizonte (distinto de Miss).
/// </summary>
public static class OhlcPathEvaluator
{
    public static SignedMove MoveForRole(bool resistanceExpectsUp, double entry, double high, double low)
    {
        if (resistanceExpectsUp)
            return new SignedMove(Favorable: high - entry, Adverse: entry - low);
        return new SignedMove(Favorable: entry - low, Adverse: high - entry);
    }

    public static ReactionEvaluation Evaluate(
        IReadOnlyList<(double High, double Low)> pathBars,
        double entry,
        double atr,
        double thresholdAtrMult,
        bool resistanceExpectsUp,
        OhlcAmbiguityPolicy policy)
    {
        if (atr <= 0 || double.IsNaN(atr) || pathBars.Count == 0)
        {
            return new ReactionEvaluation
            {
                Outcome = ReactionOutcome.Unresolved,
                Policy = policy,
                MfeAtr = 0,
                MaeAtr = 0,
                ResolutionBarOffset = null,
                SameBarBothThresholds = false
            };
        }

        var thr = thresholdAtrMult * atr;
        double mfe = 0;
        double mae = 0;
        var sawAmbiguous = false;

        for (var i = 0; i < pathBars.Count; i++)
        {
            var (high, low) = pathBars[i];
            var move = MoveForRole(resistanceExpectsUp, entry, high, low);
            mfe = Math.Max(mfe, move.Favorable);
            mae = Math.Max(mae, move.Adverse);

            var favHit = move.Favorable >= thr;
            var advHit = move.Adverse >= thr;

            if (!favHit && !advHit)
                continue;

            if (favHit && advHit)
            {
                sawAmbiguous = true;
                return policy switch
                {
                    OhlcAmbiguityPolicy.Optimistic => Done(ReactionOutcome.Hit, policy, mfe, mae, atr, i, true),
                    OhlcAmbiguityPolicy.Conservative => Done(ReactionOutcome.Miss, policy, mfe, mae, atr, i, true),
                    OhlcAmbiguityPolicy.ExcludeAmbiguous => Done(ReactionOutcome.AmbiguousSameBar, policy, mfe, mae, atr, i, true),
                    _ => Done(ReactionOutcome.AmbiguousSameBar, policy, mfe, mae, atr, i, true)
                };
            }

            if (favHit)
                return Done(ReactionOutcome.Hit, policy, mfe, mae, atr, i, sawAmbiguous);
            return Done(ReactionOutcome.Miss, policy, mfe, mae, atr, i, sawAmbiguous);
        }

        return Done(ReactionOutcome.Unresolved, policy, mfe, mae, atr, null, sawAmbiguous);
    }

    public static IReadOnlyDictionary<OhlcAmbiguityPolicy, ReactionEvaluation> EvaluateAllPolicies(
        IReadOnlyList<(double High, double Low)> pathBars,
        double entry,
        double atr,
        double thresholdAtrMult,
        bool resistanceExpectsUp)
    {
        var dict = new Dictionary<OhlcAmbiguityPolicy, ReactionEvaluation>();
        foreach (OhlcAmbiguityPolicy p in Enum.GetValues(typeof(OhlcAmbiguityPolicy)))
            dict[p] = Evaluate(pathBars, entry, atr, thresholdAtrMult, resistanceExpectsUp, p);
        return dict;
    }

    private static ReactionEvaluation Done(
        ReactionOutcome outcome,
        OhlcAmbiguityPolicy policy,
        double mfe,
        double mae,
        double atr,
        int? offset,
        bool ambiguous) =>
        new()
        {
            Outcome = outcome,
            Policy = policy,
            MfeAtr = mfe / atr,
            MaeAtr = mae / atr,
            ResolutionBarOffset = offset,
            SameBarBothThresholds = ambiguous
        };
}

public sealed class PolicyAggregate
{
    public required string VariantId { get; init; }
    public required int SwingK { get; init; }
    public required string Segment { get; init; }
    public required string Study { get; init; }
    public required OhlcAmbiguityPolicy Policy { get; init; }
    public required int HorizonBars { get; init; }
    public int Evaluable { get; init; }
    public int Hits { get; init; }
    public int Misses { get; init; }
    public int AmbiguousSameBar { get; init; }
    public int Unresolved { get; init; }
    public double HitRateAmongResolved { get; init; }
    public double HitRateAmongEvaluableExcludeAmbiguous { get; init; }
    public double MeanMfeAtr { get; init; }
    public double MeanMaeAtr { get; init; }

    /// <summary>
    /// HitRate principal por política:
    /// Optimistic/Conservative: Hits / (Hits+Misses+Unresolved) — Unresolved no es Hit.
    /// ExcludeAmbiguous: Hits / (Hits+Misses+Unresolved) excluyendo Ambiguous del denominador.
    /// </summary>
    public double HitRateReported { get; init; }
}

public static class PolicyAggregateBuilder
{
    public static PolicyAggregate Build(
        string variantId,
        int swingK,
        string segment,
        string study,
        OhlcAmbiguityPolicy policy,
        int horizon,
        IReadOnlyList<ReactionEvaluation> rows)
    {
        var hits = rows.Count(r => r.Outcome == ReactionOutcome.Hit);
        var misses = rows.Count(r => r.Outcome == ReactionOutcome.Miss);
        var ambOutcome = rows.Count(r => r.Outcome == ReactionOutcome.AmbiguousSameBar);
        var ambFlag = rows.Count(r => r.SameBarBothThresholds);
        var unresolved = rows.Count(r => r.Outcome == ReactionOutcome.Unresolved);

        // ExcludeAmbiguous: AmbiguousSameBar fuera del denominador.
        // Optimistic/Conservative: ambigüedades ya mapeadas a Hit/Miss; denom = todas las filas.
        var evaluable = policy == OhlcAmbiguityPolicy.ExcludeAmbiguous
            ? hits + misses + unresolved
            : rows.Count;
        var denomReported = evaluable;
        var hitRateReported = denomReported == 0 ? 0 : hits / (double)denomReported;
        var resolved = hits + misses;
        var hitAmongResolved = resolved == 0 ? 0 : hits / (double)resolved;

        var mfe = rows.Count == 0 ? 0 : rows.Average(r => r.MfeAtr);
        var mae = rows.Count == 0 ? 0 : rows.Average(r => r.MaeAtr);

        return new PolicyAggregate
        {
            VariantId = variantId,
            SwingK = swingK,
            Segment = segment,
            Study = study,
            Policy = policy,
            HorizonBars = horizon,
            Evaluable = evaluable,
            Hits = hits,
            Misses = misses,
            AmbiguousSameBar = policy == OhlcAmbiguityPolicy.ExcludeAmbiguous ? ambOutcome : ambFlag,
            Unresolved = unresolved,
            HitRateAmongResolved = hitAmongResolved,
            HitRateAmongEvaluableExcludeAmbiguous = hitRateReported,
            MeanMfeAtr = mfe,
            MeanMaeAtr = mae,
            HitRateReported = hitRateReported
        };
    }
}
