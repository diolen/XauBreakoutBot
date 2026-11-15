using XauSwingAbHarness.Dataset;

namespace XauSwingAbHarness.Breakout;

public static class EventFilters
{
    /// <summary>
    /// S1: excluir T3 en [gapResume, gapResume+16] o con gap resume en (T3, T3+H].
    /// S2: mismas reglas solo para WeekendClose y MultiDayUnknown.
    /// </summary>
    public static bool IsExcludedByGap(
        T3EventRecord t3,
        IReadOnlyList<TemporalGap> gaps,
        IReadOnlyList<DateTime> openTimes,
        int horizonBars,
        bool restrictedClassesOnly)
    {
        var i0 = t3.T3BarIndex;
        for (var g = 0; g < gaps.Count; g++)
        {
            var gap = gaps[g];
            if (restrictedClassesOnly &&
                gap.Classification is not (GapClass.WeekendClose or GapClass.MultiDayUnknown))
                continue;

            var resumeIdx = IndexOfTime(openTimes, gap.NextOpenUtc);
            if (resumeIdx < 0)
                continue;

            // Evento en las primeras 16 velas tras el gap.
            if (i0 >= resumeIdx && i0 <= resumeIdx + horizonBars)
                return true;

            // Gap (su resume) cae dentro del horizonte de evaluación (T3, T3+H].
            if (resumeIdx > i0 && resumeIdx <= i0 + horizonBars)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Submuestra determinista: orden temporal, conservar primero, saltar 16 barras.
    /// Reduce solapamiento; no garantiza independencia estadística.
    /// </summary>
    public static List<T3EventRecord> DecimateByMinGap(
        IEnumerable<T3EventRecord> events,
        int minGapBars)
    {
        var ordered = events.OrderBy(e => e.T3BarIndex).ThenBy(e => e.LevelId).ToList();
        var kept = new List<T3EventRecord>();
        var nextAllowed = int.MinValue;
        foreach (var e in ordered)
        {
            if (e.T3BarIndex < nextAllowed)
                continue;
            kept.Add(e);
            nextAllowed = e.T3BarIndex + minGapBars + 1; // 16 barras completas entre eventos
        }

        return kept;
    }

    private static int IndexOfTime(IReadOnlyList<DateTime> openTimes, DateTime utc)
    {
        // Búsqueda lineal suficiente; dataset ~23k.
        var target = DateTime.SpecifyKind(utc.ToUniversalTime(), DateTimeKind.Utc);
        for (var i = 0; i < openTimes.Count; i++)
        {
            var t = openTimes[i].Kind == DateTimeKind.Utc
                ? openTimes[i]
                : DateTime.SpecifyKind(openTimes[i], DateTimeKind.Utc);
            if (t == target)
                return i;
        }

        return -1;
    }
}
