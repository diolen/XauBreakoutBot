using System.Collections.Generic;

namespace cAlgo.Robots.SupportResistance;

/// <summary>
/// Descriptor de marcador visual de toque. Solo lectura; no muta el motor.
/// </summary>
public readonly struct TouchMarkerSpec
{
    public TouchMarkerSpec(long levelId, int touchOrdinal, int barIndex, System.DateTime time, double price, string objectName, string label)
    {
        LevelId = levelId;
        TouchOrdinal = touchOrdinal;
        BarIndex = barIndex;
        Time = time;
        Price = price;
        ObjectName = objectName;
        Label = label;
    }

    public long LevelId { get; }
    /// <summary>Número de toque 1-based en orden cronológico de registros independientes.</summary>
    public int TouchOrdinal { get; }
    public int BarIndex { get; }
    public System.DateTime Time { get; }
    public double Price { get; }
    public string ObjectName { get; }
    public string Label { get; }
}

/// <summary>
/// Construye marcadores de toques para Trading View a partir de niveles ya seleccionados.
/// </summary>
public static class TradingViewTouchMarkers
{
    public const string ObjectPrefix = "SR_TVTH_";

    /// <summary>
    /// Si <paramref name="showTouches"/> es false, lista vacía.
    /// Si true, un marcador por cada <see cref="TouchRecord"/> independiente de los niveles dados,
    /// usando Time/Price originales, ordenados por BarIndex/Time, etiquetados `#Id T1`, `#Id T2`, …
    /// </summary>
    public static List<TouchMarkerSpec> Build(IReadOnlyList<SrLevel> selectedLevels, bool showTouches)
    {
        var result = new List<TouchMarkerSpec>();
        if (!showTouches || selectedLevels.Count == 0)
            return result;

        foreach (var level in selectedLevels)
        {
            var independent = new List<TouchRecord>();
            foreach (var t in level.Touches)
            {
                if (t.IsIndependent)
                    independent.Add(t);
            }

            independent.Sort(static (a, b) =>
            {
                var cmp = a.BarIndex.CompareTo(b.BarIndex);
                if (cmp != 0)
                    return cmp;
                return a.Time.CompareTo(b.Time);
            });

            for (var i = 0; i < independent.Count; i++)
            {
                var t = independent[i];
                var ordinal = i + 1;
                var name = ObjectPrefix + level.Id + "_" + t.BarIndex + "_" + ordinal;
                var label = "#" + level.Id + " T" + ordinal;
                result.Add(new TouchMarkerSpec(
                    level.Id,
                    ordinal,
                    t.BarIndex,
                    t.Time,
                    t.Price,
                    name,
                    label));
            }
        }

        return result;
    }
}
