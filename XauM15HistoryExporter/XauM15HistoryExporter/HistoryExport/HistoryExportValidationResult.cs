using System;
using System.Collections.Generic;

namespace cAlgo.Robots.HistoryExport;

public sealed class HistoryExportValidationResult
{
    public HistoryExportValidationResult(
        bool isValid,
        IReadOnlyList<string> errors,
        IReadOnlyList<ExportOhlcBar> barsForExport,
        int duplicateCount,
        int temporalGapCount,
        DateTime? firstOpenTimeUtc,
        DateTime? lastOpenTimeUtc)
    {
        IsValid = isValid;
        Errors = errors;
        BarsForExport = barsForExport;
        DuplicateCount = duplicateCount;
        TemporalGapCount = temporalGapCount;
        FirstOpenTimeUtc = firstOpenTimeUtc;
        LastOpenTimeUtc = lastOpenTimeUtc;
    }

    public bool IsValid { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<ExportOhlcBar> BarsForExport { get; }
    public int DuplicateCount { get; }
    public int TemporalGapCount { get; }
    public DateTime? FirstOpenTimeUtc { get; }
    public DateTime? LastOpenTimeUtc { get; }
}
