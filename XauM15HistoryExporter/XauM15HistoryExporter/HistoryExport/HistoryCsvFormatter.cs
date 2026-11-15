using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace cAlgo.Robots.HistoryExport;

/// <summary>
/// Formato CSV OHLC compatible con .NET (cultura invariante) y herramientas de análisis.
/// </summary>
public static class HistoryCsvFormatter
{
    public const string Header = "OpenTime,Open,High,Low,Close";

    public static string FormatDocument(IReadOnlyList<ExportOhlcBar> bars)
    {
        var sb = new StringBuilder(capacity: 64 + bars.Count * 64);
        sb.Append(Header);
        sb.Append('\n');
        for (var i = 0; i < bars.Count; i++)
        {
            sb.Append(FormatRow(bars[i]));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    public static string FormatRow(ExportOhlcBar bar)
    {
        var openTime = EnsureUtc(bar.OpenTimeUtc)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return string.Join(",",
            openTime,
            FormatPrice(bar.Open),
            FormatPrice(bar.High),
            FormatPrice(bar.Low),
            FormatPrice(bar.Close));
    }

    public static string SuggestFileName(string symbol, DateTime firstUtc, DateTime lastUtc)
    {
        var safeSymbol = SanitizeToken(symbol).ToLowerInvariant();
        var from = firstUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var to = lastUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        return $"{safeSymbol}_m15_{from}_{to}.csv";
    }

    public static string ComputeSha256Hex(byte[] content)
    {
        var hash = SHA256.HashData(content);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>
    /// Cultura invariante; "R" conserva el valor double original (round-trip) sin forzar decimales artificiales.
    /// </summary>
    private static string FormatPrice(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static string SanitizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "symbol";

        var chars = value.Trim().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        return new string(chars);
    }
}
