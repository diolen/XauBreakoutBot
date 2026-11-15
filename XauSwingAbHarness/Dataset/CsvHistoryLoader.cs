using System.Globalization;
using cAlgo.Robots.SupportResistance;

namespace XauSwingAbHarness.Dataset;

public static class CsvHistoryLoader
{
    public static IReadOnlyList<OhlcBar> Load(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2)
            throw new InvalidDataException("CSV vacío o sin filas de datos.");

        if (!lines[0].Equals("OpenTime,Open,High,Low,Close", StringComparison.Ordinal))
            throw new InvalidDataException($"Cabecera inesperada: '{lines[0]}'.");

        var bars = new List<OhlcBar>(lines.Length - 1);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(',');
            if (parts.Length != 5)
                throw new InvalidDataException($"Fila {i}: se esperaban 5 columnas.");

            var openTime = DateTime.Parse(
                parts[0],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            openTime = DateTime.SpecifyKind(openTime, DateTimeKind.Utc);

            var open = double.Parse(parts[1], CultureInfo.InvariantCulture);
            var high = double.Parse(parts[2], CultureInfo.InvariantCulture);
            var low = double.Parse(parts[3], CultureInfo.InvariantCulture);
            var close = double.Parse(parts[4], CultureInfo.InvariantCulture);

            bars.Add(new OhlcBar(bars.Count, openTime, open, high, low, close));
        }

        return bars;
    }
}
