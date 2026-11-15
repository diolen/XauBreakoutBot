namespace cAlgo.Robots.SupportResistance;

/// <summary>Modo de dibujo en gráfico. No afecta detección ni ciclo de vida.</summary>
public enum VisualizationMode
{
    /// <summary>Solo Active cercanos (hasta 3 R arriba / 3 S abajo). Estilo limpio.</summary>
    TradingView = 0,

    /// <summary>Visualización completa de depuración (todos los niveles según flags).</summary>
    FullDebug = 1
}
