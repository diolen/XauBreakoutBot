using System;

namespace cAlgo.Robots.Execution;

/// <summary>
/// Parámetros de preparación de ejecución DEMO. No habilitan el envío de órdenes.
/// </summary>
public sealed class ExecutionParameters
{
    /// <summary>Spread máximo permitido en pips (precio / PipSize).</summary>
    public const double DefaultMaxSpreadPips = 50.0;

    /// <summary>Edad máxima de la señal en barras cerradas tras el breakout.</summary>
    public const int DefaultMaxSignalAgeBars = 2;

    /// <summary>
    /// Hard-off de órdenes en v1.7. Aunque sea true en el futuro, esta versión
    /// no contiene llamadas de ejecución.
    /// </summary>
    public const bool DefaultOrdersEnabled = false;

    public double MaxSpreadPips { get; init; } = DefaultMaxSpreadPips;
    public int MaxSignalAgeBars { get; init; } = DefaultMaxSignalAgeBars;
    public bool OrdersEnabled { get; init; } = DefaultOrdersEnabled;

    public bool TryValidate(out string rejectionReason)
    {
        if (!double.IsFinite(MaxSpreadPips) || MaxSpreadPips <= 0)
        {
            rejectionReason = ExecutionRejectionReasons.InvalidParameters;
            return false;
        }

        if (MaxSignalAgeBars < 0)
        {
            rejectionReason = ExecutionRejectionReasons.InvalidParameters;
            return false;
        }

        rejectionReason = "";
        return true;
    }
}
