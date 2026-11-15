namespace XauSwingAbHarness.Dataset;

public enum GapClass
{
    ExactM15 = 0,
    WeekendClose = 1,
    WeekdayDailyBreak = 2,
    ShortGapUnknown = 3,
    IntradayOrSessionUnknown = 4,
    MultiDayUnknown = 5,
    NearM15Irregular = 6,
    Unknown = 7
}

public sealed record TemporalGap(
    DateTime PreviousOpenUtc,
    DateTime NextOpenUtc,
    TimeSpan Delta,
    GapClass Classification);

public static class GapClassifier
{
    public static readonly TimeSpan M15 = TimeSpan.FromMinutes(15);

    public static GapClass Classify(DateTime previousOpenUtc, DateTime nextOpenUtc)
    {
        var delta = nextOpenUtc - previousOpenUtc;
        var mins = delta.TotalMinutes;
        if (Math.Abs(mins - 15) < 1e-9)
            return GapClass.ExactM15;

        var wd = previousOpenUtc.DayOfWeek;
        var wdEnd = nextOpenUtc.DayOfWeek;

        // Cierre típico XAU: viernes tarde → domingo noche (UTC).
        if (wd == DayOfWeek.Friday && wdEnd == DayOfWeek.Sunday && mins >= 24 * 60 && mins <= 3 * 24 * 60)
            return GapClass.WeekendClose;

        if (wd == DayOfWeek.Friday && wdEnd == DayOfWeek.Monday && mins >= 24 * 60)
            return GapClass.WeekendClose;

        // Hueco diario recurrente ~21:45 → 23:00 UTC (mantenimiento / break de sesión).
        if (mins > 15 && mins <= 120 &&
            previousOpenUtc.TimeOfDay >= TimeSpan.FromHours(21) &&
            previousOpenUtc.TimeOfDay < TimeSpan.FromHours(22) &&
            nextOpenUtc.TimeOfDay >= TimeSpan.FromHours(22) &&
            nextOpenUtc.TimeOfDay <= TimeSpan.FromHours(23) &&
            wd is >= DayOfWeek.Monday and <= DayOfWeek.Thursday)
            return GapClass.WeekdayDailyBreak;

        if (mins <= 30)
            return GapClass.NearM15Irregular;
        if (mins <= 120)
            return GapClass.ShortGapUnknown;
        if (mins <= 24 * 60)
            return GapClass.IntradayOrSessionUnknown;
        if (mins > 24 * 60)
            return GapClass.MultiDayUnknown;
        return GapClass.Unknown;
    }

    public static IReadOnlyList<TemporalGap> FindGaps(IReadOnlyList<DateTime> openTimesUtc)
    {
        var gaps = new List<TemporalGap>();
        for (var i = 1; i < openTimesUtc.Count; i++)
        {
            var delta = openTimesUtc[i] - openTimesUtc[i - 1];
            if (delta <= M15)
            {
                if (delta < M15)
                {
                    gaps.Add(new TemporalGap(
                        openTimesUtc[i - 1],
                        openTimesUtc[i],
                        delta,
                        GapClass.NearM15Irregular));
                }

                continue;
            }

            gaps.Add(new TemporalGap(
                openTimesUtc[i - 1],
                openTimesUtc[i],
                delta,
                Classify(openTimesUtc[i - 1], openTimesUtc[i])));
        }

        return gaps;
    }
}
