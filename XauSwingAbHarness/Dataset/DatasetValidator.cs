using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using cAlgo.Robots.SupportResistance;

namespace XauSwingAbHarness.Dataset;

public sealed class DatasetValidationResult
{
    public required string Path { get; init; }
    public required string Sha256Hex { get; init; }
    public required int BarCount { get; init; }
    public required DateTime FirstOpenUtc { get; init; }
    public required DateTime LastOpenUtc { get; init; }
    public required int DuplicateCount { get; init; }
    public required int OhlcErrorCount { get; init; }
    public required int OrderErrorCount { get; init; }
    public required int NonFiniteCount { get; init; }
    public required IReadOnlyList<TemporalGap> Gaps { get; init; }
    public required IReadOnlyDictionary<GapClass, int> GapClassCounts { get; init; }
    public required IReadOnlyDictionary<string, int> GapDurationBuckets { get; init; }
    public bool IsStructurallyValid =>
        DuplicateCount == 0 &&
        OhlcErrorCount == 0 &&
        OrderErrorCount == 0 &&
        NonFiniteCount == 0 &&
        BarCount > 0;
}

public static class DatasetValidator
{
    public static DatasetValidationResult Validate(string path, IReadOnlyList<OhlcBar> bars)
    {
        var bytes = File.ReadAllBytes(path);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var duplicates = 0;
        var ohlcErrors = 0;
        var orderErrors = 0;
        var nonFinite = 0;
        var seen = new HashSet<DateTime>();

        for (var i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            if (!seen.Add(b.OpenTime))
                duplicates++;

            if (!IsFinite(b.Open) || !IsFinite(b.High) || !IsFinite(b.Low) || !IsFinite(b.Close))
                nonFinite++;

            var maxOc = Math.Max(b.Open, b.Close);
            var minOc = Math.Min(b.Open, b.Close);
            if (b.High < maxOc || b.Low > minOc)
                ohlcErrors++;

            if (i > 0 && b.OpenTime <= bars[i - 1].OpenTime)
                orderErrors++;
        }

        var times = bars.Select(b => b.OpenTime).ToList();
        var gaps = GapClassifier.FindGaps(times);
        var classCounts = gaps.GroupBy(g => g.Classification)
            .ToDictionary(g => g.Key, g => g.Count());
        var durationBuckets = new Dictionary<string, int>
        {
            ["<=30m"] = 0,
            ["30m-2h"] = 0,
            ["2h-6h"] = 0,
            ["6h-24h"] = 0,
            ["1-2d"] = 0,
            [">2d"] = 0
        };
        foreach (var g in gaps)
        {
            var m = g.Delta.TotalMinutes;
            if (m <= 30) durationBuckets["<=30m"]++;
            else if (m <= 120) durationBuckets["30m-2h"]++;
            else if (m <= 360) durationBuckets["2h-6h"]++;
            else if (m <= 1440) durationBuckets["6h-24h"]++;
            else if (m <= 2880) durationBuckets["1-2d"]++;
            else durationBuckets[">2d"]++;
        }

        return new DatasetValidationResult
        {
            Path = path,
            Sha256Hex = sha,
            BarCount = bars.Count,
            FirstOpenUtc = bars[0].OpenTime,
            LastOpenUtc = bars[^1].OpenTime,
            DuplicateCount = duplicates,
            OhlcErrorCount = ohlcErrors,
            OrderErrorCount = orderErrors,
            NonFiniteCount = nonFinite,
            Gaps = gaps,
            GapClassCounts = classCounts,
            GapDurationBuckets = durationBuckets
        };
    }

    public static string FormatSummary(DatasetValidationResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Path: {r.Path}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"SHA-256: {r.Sha256Hex}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Bars: {r.BarCount}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"First: {r.FirstOpenUtc:O}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Last: {r.LastOpenUtc:O}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Duplicates: {r.DuplicateCount}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"OHLC errors: {r.OhlcErrorCount}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Order errors: {r.OrderErrorCount}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Non-finite: {r.NonFiniteCount}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Temporal gaps: {r.Gaps.Count}");
        foreach (var kv in r.GapClassCounts.OrderBy(k => k.Key.ToString()))
            sb.AppendLine(CultureInfo.InvariantCulture, $"  gap.{kv.Key}: {kv.Value}");
        foreach (var kv in r.GapDurationBuckets)
            sb.AppendLine(CultureInfo.InvariantCulture, $"  duration.{kv.Key}: {kv.Value}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"StructurallyValid: {r.IsStructurallyValid}");
        return sb.ToString();
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
