using System;
using System.Collections.Generic;

namespace cAlgo.Robots.SupportResistance;

/// <summary>
/// Instantánea de candidatos estructurales previos al tercer toque.
/// Solo lectura; no implica momento de entrada ni ruptura confirmada.
/// </summary>
public sealed class EntryCandidateSnapshot
{
    public EntryCandidateSnapshot(
        IReadOnlyList<SrLevel> longCandidates,
        IReadOnlyList<SrLevel> shortCandidates)
    {
        LongCandidates = longCandidates;
        ShortCandidates = shortCandidates;
    }

    /// <summary>Resistencias Active con T=2 por encima del Ask (futuro LONG).</summary>
    public IReadOnlyList<SrLevel> LongCandidates { get; }

    /// <summary>Soportes Active con T=2 por debajo del Bid (futuro SHORT).</summary>
    public IReadOnlyList<SrLevel> ShortCandidates { get; }

    public int Count => LongCandidates.Count + ShortCandidates.Count;

    public bool IsEmpty => Count == 0;

    /// <summary>Todos los candidatos (LONG luego SHORT), sin mutar el motor.</summary>
    public List<SrLevel> All()
    {
        var list = new List<SrLevel>(Count);
        list.AddRange(LongCandidates);
        list.AddRange(ShortCandidates);
        return list;
    }
}

/// <summary>
/// Capa independiente de selección de candidatos estructurales (Active, T=2, lado correcto).
/// No modifica el motor S/R ni los niveles Active. No define entrada ni ruptura.
/// </summary>
public static class EntryCandidateSelector
{
    public const int RequiredIndependentTouches = 2;

    public const string EmptyMessage =
        "No entry candidates: no Active T=2 levels on the correct side of price.";

    /// <summary>
    /// Selecciona resistencias Active con exactamente 2 toques independientes por encima del Ask
    /// (LONG) y soportes Active con exactamente 2 toques por debajo del Bid (SHORT).
    /// Horizontales y diagonales usan la misma métrica de precio proyectado.
    /// </summary>
    public static EntryCandidateSnapshot Select(
        IReadOnlyList<SrLevel> levels,
        double bid,
        double ask,
        int referenceBarIndex)
    {
        var longs = new List<SrLevel>();
        var shorts = new List<SrLevel>();

        foreach (var level in levels)
        {
            if (!IsStructuralCandidate(level))
                continue;

            var px = ProjectedPrice(level, referenceBarIndex);

            if (level.Role == LevelRole.Resistance)
            {
                if (px > ask)
                    longs.Add(level);
            }
            else if (level.Role == LevelRole.Support)
            {
                if (px < bid)
                    shorts.Add(level);
            }
        }

        // Orden estable: distancia al precio de referencia, empate → Id más antiguo.
        longs.Sort((a, b) => CompareByDistanceThenId(a, b, ask, referenceBarIndex));
        shorts.Sort((a, b) => CompareByDistanceThenId(a, b, bid, referenceBarIndex));

        return new EntryCandidateSnapshot(longs, shorts);
    }

    /// <summary>
    /// Active, exactamente T=2, no invalidado. No comprueba lado del precio.
    /// </summary>
    public static bool IsStructuralCandidate(SrLevel level)
    {
        if (level.State != LevelState.Active)
            return false;
        if (level.Invalidation != null)
            return false;
        return level.IndependentTouchCount == RequiredIndependentTouches;
    }

    public static long[] ToIdArray(EntryCandidateSnapshot snapshot)
    {
        var all = snapshot.All();
        var ids = new long[all.Count];
        for (var i = 0; i < all.Count; i++)
            ids[i] = all[i].Id;
        Array.Sort(ids);
        return ids;
    }

    public static bool SameIdSet(long[]? a, long[]? b) =>
        TradingViewLevelSelector.SameIdSet(a, b);

    private static double ProjectedPrice(SrLevel level, int referenceBarIndex) =>
        level.Geometry == LevelGeometry.Horizontal
            ? level.Center
            : level.ProjectPriceAtBar(referenceBarIndex);

    private static int CompareByDistanceThenId(
        SrLevel a,
        SrLevel b,
        double referencePrice,
        int referenceBarIndex)
    {
        var da = Math.Abs(ProjectedPrice(a, referenceBarIndex) - referencePrice);
        var db = Math.Abs(ProjectedPrice(b, referenceBarIndex) - referencePrice);
        var cmp = da.CompareTo(db);
        if (cmp != 0)
            return cmp;
        return a.Id.CompareTo(b.Id);
    }
}
