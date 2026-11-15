using System.Text;
using cAlgo.Robots.HistoryExport;
using Xunit;

namespace XauM15HistoryExporter.Tests;

public class HistoryExportValidatorTests
{
    private static readonly DateTime From = new(2025, 11, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2025, 11, 14, 23, 59, 59, DateTimeKind.Utc);

    [Fact]
    public void Validate_AcceptsAscendingUniqueFiniteOhlc()
    {
        var bars = new[]
        {
            Bar(From, 100, 101, 99, 100.5),
            Bar(From.AddMinutes(15), 100.5, 102, 100, 101),
            Bar(From.AddMinutes(30), 101, 101.5, 100.5, 101.2),
        };

        var result = HistoryExportValidator.Validate(bars, From, To, HistoryExportValidator.M15Period, maxBars: 0);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(3, result.BarsForExport.Count);
        Assert.Equal(0, result.DuplicateCount);
        Assert.Equal(0, result.TemporalGapCount);
    }

    [Fact]
    public void Validate_RemovesDuplicates_KeepsFirst()
    {
        var bars = new[]
        {
            Bar(From, 100, 101, 99, 100),
            Bar(From, 999, 999, 999, 999), // duplicado
            Bar(From.AddMinutes(15), 100, 101, 99, 100),
        };

        var result = HistoryExportValidator.Validate(bars, From, To, HistoryExportValidator.M15Period, 0);

        Assert.True(result.IsValid);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(2, result.BarsForExport.Count);
        Assert.Equal(100, result.BarsForExport[0].Open);
    }

    [Fact]
    public void Validate_CountsWeekendGap_ButDoesNotFail()
    {
        var friday = new DateTime(2025, 11, 14, 21, 0, 0, DateTimeKind.Utc);
        var sunday = new DateTime(2025, 11, 16, 22, 0, 0, DateTimeKind.Utc);
        var bars = new[]
        {
            Bar(friday, 100, 101, 99, 100),
            Bar(sunday, 100, 101, 99, 100),
        };

        var result = HistoryExportValidator.Validate(
            bars,
            friday,
            sunday.AddHours(1),
            HistoryExportValidator.M15Period,
            0);

        Assert.True(result.IsValid);
        Assert.Equal(1, result.TemporalGapCount);
    }

    [Fact]
    public void Validate_RejectsBrokenOhlc()
    {
        var bars = new[]
        {
            Bar(From, 100, 99, 98, 100), // High < Open
        };

        var result = HistoryExportValidator.Validate(bars, From, To, HistoryExportValidator.M15Period, 0);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("High", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsNonM15Period()
    {
        var bars = new[] { Bar(From, 100, 101, 99, 100) };
        var result = HistoryExportValidator.Validate(bars, From, To, TimeSpan.FromMinutes(5), 0);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("M15", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AppliesMaxBarsCap()
    {
        var bars = Enumerable.Range(0, 10)
            .Select(i => Bar(From.AddMinutes(15 * i), 100, 101, 99, 100))
            .ToArray();

        var result = HistoryExportValidator.Validate(bars, From, To, HistoryExportValidator.M15Period, maxBars: 4);

        Assert.True(result.IsValid);
        Assert.Equal(4, result.BarsForExport.Count);
        Assert.Equal(From, result.FirstOpenTimeUtc);
    }

    [Fact]
    public void Collector_ExcludesOpenAndOutOfRange()
    {
        var times = new[]
        {
            From.AddMinutes(-15),
            From,
            From.AddMinutes(15),
            From.AddMinutes(30), // "en formación" si lastClosed=1 → índices 0..1
        };
        var opens = new[] { 1d, 2d, 3d, 4d };
        var highs = new[] { 2d, 3d, 4d, 5d };
        var lows = new[] { 0.5d, 1.5d, 2.5d, 3.5d };
        var closes = new[] { 1.5d, 2.5d, 3.5d, 4.5d };

        // lastClosedInclusiveIndex = 2 → incluye hasta índice 2; el índice 3 queda fuera (vela abierta).
        var collected = HistoryBarCollector.CollectClosedInRange(
            times, opens, highs, lows, closes,
            lastClosedInclusiveIndex: 2,
            fromUtcInclusive: From,
            toUtcInclusive: From.AddMinutes(15));

        Assert.Equal(2, collected.Count);
        Assert.Equal(2d, collected[0].Open);
        Assert.Equal(3d, collected[1].Open);
    }

    [Fact]
    public void Csv_UsesInvariantIso8601AndHeader()
    {
        var bars = new[] { Bar(From, 4400.1, 4402.5, 4398.2, 4401.3) };
        var csv = HistoryCsvFormatter.FormatDocument(bars);

        Assert.StartsWith("OpenTime,Open,High,Low,Close\n", csv, StringComparison.Ordinal);
        Assert.Contains("2025-11-14T00:00:00Z,4400.1,4402.5,4398.2,4401.3", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("4400,1", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Sha256_IsStableForSameBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("OpenTime,Open,High,Low,Close\n");
        var a = HistoryCsvFormatter.ComputeSha256Hex(bytes);
        var b = HistoryCsvFormatter.ComputeSha256Hex(bytes);
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
    }

    [Fact]
    public void SuggestFileName_UsesSymbolAndMonths()
    {
        var name = HistoryCsvFormatter.SuggestFileName(
            "XAUUSD",
            new DateTime(2025, 11, 14, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("xauusd_m15_2025-11_2026-11.csv", name);
    }

    private static ExportOhlcBar Bar(DateTime t, double o, double h, double l, double c) =>
        new(t, o, h, l, c);
}
