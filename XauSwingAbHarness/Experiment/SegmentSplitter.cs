namespace XauSwingAbHarness.Experiment;

public static class SegmentSplitter
{
    public const string Develop = "develop";
    public const string Validate = "validate";
    public const string Oos = "oos";
    public const string Global = "global";

    public static string SegmentOf(int barIndex, int barCount, double developFrac, double validateFrac)
    {
        if (barCount <= 0)
            return Develop;

        var developEnd = (int)Math.Floor(barCount * developFrac);
        var validateEnd = developEnd + (int)Math.Floor(barCount * validateFrac);
        if (validateEnd > barCount)
            validateEnd = barCount;

        if (barIndex < developEnd)
            return Develop;
        if (barIndex < validateEnd)
            return Validate;
        return Oos;
    }

    public static (int DevelopEnd, int ValidateEnd) Bounds(int barCount, double developFrac, double validateFrac)
    {
        var developEnd = (int)Math.Floor(barCount * developFrac);
        var validateEnd = developEnd + (int)Math.Floor(barCount * validateFrac);
        if (validateEnd > barCount)
            validateEnd = barCount;
        return (developEnd, validateEnd);
    }
}
