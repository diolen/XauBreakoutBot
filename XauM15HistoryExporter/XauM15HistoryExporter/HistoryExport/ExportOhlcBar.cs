namespace cAlgo.Robots.HistoryExport;

/// <summary>
/// Vela OHLC cerrada lista para exportación (sin índice de gráfico).
/// </summary>
public readonly record struct ExportOhlcBar(
    System.DateTime OpenTimeUtc,
    double Open,
    double High,
    double Low,
    double Close);
