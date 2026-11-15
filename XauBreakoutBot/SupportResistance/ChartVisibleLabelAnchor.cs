using System;

namespace cAlgo.Robots.SupportResistance;

/// <summary>
/// Posicionamiento gráfico de etiquetas dentro del viewport del Chart.
/// No participa en ATR, toques, niveles ni selección de candidatos (todo eso sigue en M15).
/// </summary>
public static class ChartVisibleLabelAnchor
{
    /// <summary>
    /// Fracción desde el borde derecho del viewport. Evita el clip en el extremo
    /// de la última vela visible (problema típico en M15).
    /// </summary>
    public const double DefaultInsetFromRight = 0.12;

    /// <summary>
    /// Índice de barra del Chart donde anclar una etiqueta, dentro de
    /// [<paramref name="firstVisible"/>, <paramref name="lastVisible"/>].
    /// </summary>
    public static int ResolveLabelBarIndex(
        int firstVisible,
        int lastVisible,
        double insetFromRight = DefaultInsetFromRight)
    {
        if (lastVisible < firstVisible)
            return Math.Max(0, lastVisible);

        if (insetFromRight < 0)
            insetFromRight = 0;
        if (insetFromRight > 0.45)
            insetFromRight = 0.45;

        var span = lastVisible - firstVisible;
        var idx = lastVisible - (int)Math.Round(span * insetFromRight);
        if (idx < firstVisible)
            idx = firstVisible;
        if (idx > lastVisible)
            idx = lastVisible;
        return idx;
    }

    /// <summary>
    /// Tiempo de etiqueta a partir de open-times visibles; si el viewport no es usable,
    /// usa <paramref name="fallbackTime"/> (p. ej. extremo M15 de datos).
    /// </summary>
    public static DateTime ResolveLabelTime(
        DateTime? visibleStart,
        DateTime? visibleEnd,
        DateTime fallbackTime,
        double insetFromRight = DefaultInsetFromRight)
    {
        if (!visibleStart.HasValue || !visibleEnd.HasValue)
            return fallbackTime;

        var start = visibleStart.Value;
        var end = visibleEnd.Value;
        if (end <= start)
            return end;

        if (insetFromRight < 0)
            insetFromRight = 0;
        if (insetFromRight > 0.45)
            insetFromRight = 0.45;

        var ticks = (long)((end - start).Ticks * (1.0 - insetFromRight));
        return start.AddTicks(ticks);
    }

    /// <summary>
    /// Extremo derecho de línea: al menos el extent de datos M15, y no antes del fin visible.
    /// </summary>
    public static DateTime ResolveLineEnd(DateTime dataExtentEnd, DateTime? visibleEnd)
    {
        if (!visibleEnd.HasValue)
            return dataExtentEnd;
        return visibleEnd.Value > dataExtentEnd ? visibleEnd.Value : dataExtentEnd;
    }

    /// <summary>
    /// Indica si el rango de índices visibles del Chart es usable (Desktop).
    /// En Cloud First/Last suelen ser 0 y no representan viewport.
    /// </summary>
    public static bool IsUsableVisibleRange(int firstVisible, int lastVisible, int barsCount)
    {
        if (barsCount <= 0)
            return false;
        if (firstVisible < 0 || lastVisible < 0)
            return false;
        if (firstVisible >= barsCount || lastVisible >= barsCount)
            return false;
        // Cloud suele reportar First=Last=0 con serie no vacía; no es un viewport real.
        if (firstVisible == 0 && lastVisible == 0 && barsCount > 1)
            return false;
        return lastVisible >= firstVisible;
    }
}
