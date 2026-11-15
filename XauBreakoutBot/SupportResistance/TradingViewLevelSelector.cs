using System;
using System.Collections.Generic;

namespace cAlgo.Robots.SupportResistance;

/// <summary>
/// Filtrado visual para modo Trading View. Solo lectura sobre niveles; no muta el motor.
/// </summary>
public static class TradingViewLevelSelector
{
    public const int MaxPerSide = 3;

    /// <summary>
    /// Devuelve hasta <see cref="MaxPerSide"/> resistencias Active por encima de <paramref name="ask"/>
    /// y hasta <see cref="MaxPerSide"/> soportes Active por debajo de <paramref name="bid"/>,
    /// ordenados por distancia (H y D en la misma métrica). Empate → Id más antiguo.
    /// </summary>
    public static List<SrLevel> Select(
        IReadOnlyList<SrLevel> levels,
        double bid,
        double ask,
        int referenceBarIndex)
    {
        var resistances = SelectNearest(
            levels, LevelRole.Resistance, above: true, ask, referenceBarIndex, MaxPerSide);
        var supports = SelectNearest(
            levels, LevelRole.Support, above: false, bid, referenceBarIndex, MaxPerSide);

        var result = new List<SrLevel>(resistances.Count + supports.Count);
        result.AddRange(resistances);
        result.AddRange(supports);
        return result;
    }

    /// <summary>Ids de la selección actual (estable para comparar cambios visuales).</summary>
    public static long[] ToIdArray(IReadOnlyList<SrLevel> selected)
    {
        var ids = new long[selected.Count];
        for (var i = 0; i < selected.Count; i++)
            ids[i] = selected[i].Id;
        Array.Sort(ids);
        return ids;
    }

    public static bool SameIdSet(long[]? a, long[]? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a == null || b == null || a.Length != b.Length)
            return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    private static List<SrLevel> SelectNearest(
        IReadOnlyList<SrLevel> levels,
        LevelRole role,
        bool above,
        double referencePrice,
        int referenceBarIndex,
        int maxCount)
    {
        // Lista pequeña de candidatos (precio proyectado, distancia, id).
        var candidates = new List<(SrLevel Level, double Dist, long Id)>();

        foreach (var level in levels)
        {
            if (level.State != LevelState.Active || level.Role != role)
                continue;

            var px = level.Geometry == LevelGeometry.Horizontal
                ? level.Center
                : level.ProjectPriceAtBar(referenceBarIndex);

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

            candidates.Add((level, Math.Abs(px - referencePrice), level.Id));
        }

        candidates.Sort(static (a, b) =>
        {
            var cmp = a.Dist.CompareTo(b.Dist);
            if (cmp != 0)
                return cmp;
            return a.Id.CompareTo(b.Id);
        });

        var take = Math.Min(maxCount, candidates.Count);
        var result = new List<SrLevel>(take);
        for (var i = 0; i < take; i++)
            result.Add(candidates[i].Level);
        return result;
    }
}
