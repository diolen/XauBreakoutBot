using System;
using System.Collections.Generic;
using System.Linq;

namespace cAlgo.Robots.HistoryExport;

/// <summary>
/// Validaciones deterministas del lote OHLC previo a escribir CSV.
/// Los saltos temporales se contabilizan pero no invalidan la exportación.
/// </summary>
public static class HistoryExportValidator
{
    public static readonly TimeSpan M15Period = TimeSpan.FromMinutes(15);

    public static HistoryExportValidationResult Validate(
        IReadOnlyList<ExportOhlcBar> rawBars,
        DateTime requestedFromUtc,
        DateTime requestedToUtc,
        TimeSpan expectedPeriod,
        int maxBars)
    {
        var errors = new List<string>();

        if (requestedToUtc < requestedFromUtc)
            errors.Add("El rango solicitado es inválido: ToDate < FromDate.");

        if (expectedPeriod != M15Period)
            errors.Add($"Timeframe esperado M15 ({M15Period}), recibido {expectedPeriod}.");

        if (rawBars.Count == 0)
        {
            errors.Add("No hay velas cerradas disponibles en el rango solicitado.");
            return new HistoryExportValidationResult(
                isValid: false,
                errors,
                Array.Empty<ExportOhlcBar>(),
                duplicateCount: 0,
                temporalGapCount: 0,
                firstOpenTimeUtc: null,
                lastOpenTimeUtc: null);
        }

        // Deduplicar por OpenTime conservando la primera ocurrencia; orden ascendente.
        var ordered = rawBars
            .Select((b, i) => (Bar: NormalizeUtc(b), Index: i))
            .OrderBy(x => x.Bar.OpenTimeUtc)
            .ThenBy(x => x.Index)
            .ToList();

        var unique = new List<ExportOhlcBar>(ordered.Count);
        var seen = new HashSet<DateTime>();
        var duplicateCount = 0;

        foreach (var item in ordered)
        {
            var bar = item.Bar;
            if (!seen.Add(bar.OpenTimeUtc))
            {
                duplicateCount++;
                continue;
            }

            ValidateNumericAndOhlc(bar, errors);
            unique.Add(bar);
        }

        for (var i = 1; i < unique.Count; i++)
        {
            if (unique[i].OpenTimeUtc <= unique[i - 1].OpenTimeUtc)
                errors.Add($"Orden temporal no ascendente en {unique[i].OpenTimeUtc:O}.");
        }

        var gapCount = CountTemporalGaps(unique, expectedPeriod);

        if (maxBars > 0 && unique.Count > maxBars)
            unique = unique.Take(maxBars).ToList();

        DateTime? first = unique.Count > 0 ? unique[0].OpenTimeUtc : null;
        DateTime? last = unique.Count > 0 ? unique[^1].OpenTimeUtc : null;

        if (first.HasValue && first.Value < requestedFromUtc)
            errors.Add($"Primera vela {first:O} es anterior a FromDate {requestedFromUtc:O}.");

        if (last.HasValue && last.Value > requestedToUtc)
            errors.Add($"Última vela {last:O} es posterior a ToDate {requestedToUtc:O}.");

        var isValid = errors.Count == 0 && unique.Count > 0;
        if (unique.Count == 0 && errors.Count == 0)
            errors.Add("Tras deduplicación no quedan velas para exportar.");

        return new HistoryExportValidationResult(
            isValid,
            errors,
            unique,
            duplicateCount,
            gapCount,
            first,
            last);
    }

    public static int CountTemporalGaps(IReadOnlyList<ExportOhlcBar> bars, TimeSpan expectedPeriod)
    {
        var gaps = 0;
        for (var i = 1; i < bars.Count; i++)
        {
            var delta = bars[i].OpenTimeUtc - bars[i - 1].OpenTimeUtc;
            if (delta > expectedPeriod)
                gaps++;
        }

        return gaps;
    }

    private static void ValidateNumericAndOhlc(ExportOhlcBar bar, List<string> errors)
    {
        if (!IsFinite(bar.Open) || !IsFinite(bar.High) || !IsFinite(bar.Low) || !IsFinite(bar.Close))
        {
            errors.Add($"Valores no finitos en {bar.OpenTimeUtc:O}.");
            return;
        }

        var maxOc = Math.Max(bar.Open, bar.Close);
        var minOc = Math.Min(bar.Open, bar.Close);
        if (bar.High < maxOc)
            errors.Add($"OHLC inválido High < max(Open,Close) en {bar.OpenTimeUtc:O}.");
        if (bar.Low > minOc)
            errors.Add($"OHLC inválido Low > min(Open,Close) en {bar.OpenTimeUtc:O}.");
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static ExportOhlcBar NormalizeUtc(ExportOhlcBar bar)
    {
        var t = bar.OpenTimeUtc.Kind switch
        {
            DateTimeKind.Utc => bar.OpenTimeUtc,
            DateTimeKind.Local => bar.OpenTimeUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(bar.OpenTimeUtc, DateTimeKind.Utc)
        };
        return bar with { OpenTimeUtc = t };
    }
}
