using System;
using System.Collections.Generic;

namespace cAlgo.Robots.HistoryExport;

/// <summary>
/// Selección de velas cerradas en un rango, sin sintetizar huecos.
/// La vela en formación (último índice de Bars) debe excluirse antes de llamar aquí.
/// </summary>
public static class HistoryBarCollector
{
    public static List<ExportOhlcBar> CollectClosedInRange(
        IReadOnlyList<DateTime> openTimes,
        IReadOnlyList<double> opens,
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int lastClosedInclusiveIndex,
        DateTime fromUtcInclusive,
        DateTime toUtcInclusive)
    {
        var result = new List<ExportOhlcBar>();
        if (lastClosedInclusiveIndex < 0)
            return result;

        var count = Math.Min(
            lastClosedInclusiveIndex + 1,
            Math.Min(
                openTimes.Count,
                Math.Min(opens.Count, Math.Min(highs.Count, Math.Min(lows.Count, closes.Count)))));

        for (var i = 0; i < count; i++)
        {
            var openTime = ToUtc(openTimes[i]);
            if (openTime < fromUtcInclusive || openTime > toUtcInclusive)
                continue;

            result.Add(new ExportOhlcBar(
                openTime,
                opens[i],
                highs[i],
                lows[i],
                closes[i]));
        }

        return result;
    }

    public static DateTime ToUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
