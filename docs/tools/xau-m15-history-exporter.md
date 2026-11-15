# XauM15HistoryExporter — exportación histórica XAUUSD M15

cBot auxiliar **aislado** para exportar OHLC M15 a CSV desde cTrader Desktop (Windows).  
No opera, no modifica el bot productivo y no implementa el experimento A/B de swings.

## Requisitos

- cTrader Desktop (Windows) con Algo / Automate.
- Framework del proyecto: **.NET 6.0** + paquete `cTrader.Automate` (mismo stack que `XauBreakoutBot`).
- Cuenta **demo** recomendada. No ejecutar en cuenta real sin confirmación humana.

## Parámetros

| Parámetro | Descripción |
|-----------|-------------|
| `Symbol Override` | Por defecto `XAUUSD`. Si el broker usa otro nombre (`XAUUSD.a`, `GOLD`, etc.), indíquelo aquí. Vacío = símbolo del gráfico. |
| `From Date UTC` | Inicio del rango (`yyyy-MM-dd` o ISO 8601). |
| `To Date UTC` | Fin del rango. Si es solo fecha, se interpreta como fin del día UTC. |
| `Output File Name` | Nombre del CSV. Vacío = auto (`xauusd_m15_YYYY-MM_YYYY-MM.csv`). |
| `Max Bars` | Tope opcional (0 = sin límite). |

## Compilar e instalar en cTrader Desktop

### Opción A — desde cTrader Algo (recomendada)

1. Abrir cTrader Desktop → **Algo**.
2. **New** → **cBot** o **Open** el proyecto:
   `XauM15HistoryExporter/XauM15HistoryExporter/XauM15HistoryExporter.csproj`
3. Compilar (**Build**). Debe generar el algoritmo `XauM15HistoryExporter`.
4. En el gráfico, seleccionar el símbolo de oro del broker y timeframe **M15** (el cBot fuerza M15 vía API, pero ayuda a la trazabilidad).
5. Añadir instancia del cBot `XauM15HistoryExporter`.
6. Configurar fechas (~12 meses) y símbolo si hace falta.
7. **Start** en cuenta demo. El cBot exporta en `OnStart` y se detiene solo.

### Opción B — desde esta solución .NET

```bash
dotnet build XauM15HistoryExporter/XauM15HistoryExporter/XauM15HistoryExporter.csproj -c Release
```

Luego abrir/importar el proyecto en cTrader Algo (la plataforma empaqueta el `.algo` al compilar dentro de Algo).

## Recuperar el CSV

Con `AccessRights.None`, la escritura usa **ruta relativa** en la carpeta designada:

```text
%USERPROFILE%\Documents\cAlgo\Data\cBots\XauM15HistoryExporter\
```

Ejemplo de archivo:

```text
xauusd_m15_2025-11_2026-11.csv
```

El log del cBot imprime la ruta relativa, la ruta Windows esperada, el rango efectivo, conteos y SHA-256.

## Formato CSV

```csv
OpenTime,Open,High,Low,Close
2025-11-14T10:00:00Z,4400.1,4402.5,4398.2,4401.3
```

- `OpenTime` UTC ISO 8601 con sufijo `Z`.
- Precios con cultura invariante.
- Solo velas **cerradas**, orden ascendente, sin duplicados ni velas sintéticas.

## Limitaciones

- El historial visible en el gráfico **no** implica que `Bars` esté completo; el cBot llama a `LoadMoreHistory()` en bucle.
- El broker puede no entregar 12 meses completos: se exporta solo lo disponible y se registra en el log.
- Pausas de mercado / fines de semana generan saltos temporales contabilizados, no rellenados.
- No se exporta la vela en formación (`Count - 1`).
- Este entorno Linux de desarrollo **no** ejecuta cTrader Desktop; la exportación real queda pendiente en Windows.

## Aislamiento

No modifica ni referencia:

- `XauBreakoutBot.cs`
- `SwingDetector.cs` / `DefaultSwingStrength`
- `SupportResistanceEngine.cs`, `SrModels.cs`, `EntryCandidateSelector.cs`
- Parámetros de trading del bot productivo
