# Diagnóstico de pérdidas — Trade Simulator MVP (v1.8)

| Campo | Valor |
|-------|--------|
| Estado | **Diagnóstico (solo lectura)** |
| Fuentes | `artifacts/trade-simulator-mvp/*`, código `XauTradeSimulator/` |
| Escenario principal | B — Spread USD 0.40 |
| Comparación de costes | A — Frictionless |
| Dataset | SHA-256 `023c31d2…ce95d`, 23 615 barras M15 |
| Señales T3 | 639 (gate de replay OK) |
| Commit / push | **No** realizados por esta tarea |

**Alcance:** determinar si las pérdidas vienen de errores del simulador o del comportamiento económico de la estrategia.  
**Prohibido aquí:** modificar código productivo, parámetros, reglas de trading, o generar variantes.

---

## Resumen ejecutivo

1. **No se encontraron errores verificables de implementación** que invaliden el signo del resultado (entrada N+1, sin lookahead operativo, Bid/Ask coherente, PnL, equity, gaps, SL-first).
2. **La pérdida existe también sin costes** (Scenario A: Net PnL **−886.40**, PF **0.945**).
3. **BUY y SELL divergen:** BUY concentra la pérdida; SELL es ligeramente positivo en B.
4. **Pérdidas distribuidas en el tiempo** (8/13 meses negativos), con clusters mensuales (may/ago/oct 2026).
5. **Hipótesis principal (soportada por hechos):** la tasa de acierto queda **por debajo del break-even de un payoff 2R** (~33.3%); el spread empeora el resultado pero no lo crea.

---

## 1. Verificación del simulador

### 1.1 Checklist

| Comprobación | Resultado | Evidencia |
|--------------|-----------|-----------|
| Entrada solo en N+1 | **OK** | Código: pending al cierre de N → `TryOpen` en open de N+1 (`TradeSimulator.cs` L52–67, L85–99, L164–166). Artefacto B: `EntryBarIndex == SignalBarIndex + 1` en **531/531** trades. Ejemplo: `L30-B229` señal 229 → entrada 230 (`trades_spread040.csv` L2). |
| Sin lookahead de entrada | **OK** | Entrada = Open Ask/Bid de N+1; High/Low no deciden la entrada (`TradeSimulator.cs` L164–166). Señales se generan en replay causal previo (`SignalReplay.cs`). |
| Spread BUY/SELL | **OK** | BUY entry Ask, SELL entry Bid (`TradeSimulator.cs` L164–166). Convención mid±spread/2 (`BidAskQuote.cs` L12–28). Ejemplo BUY: A entry `4048.44` vs B `4048.64` (Δ=+0.20 = spread/2). |
| SL/TP lado Bid/Ask | **OK** | BUY evalúa Bid; SELL evalúa Ask (`PositionExitEvaluator.cs` L35–82). |
| PnL y volumen | **OK** | `PricePnL` = vol × (Δprecio/PipSize) × PipValue (`TradeSizer.cs` L124–136). Con PipSize=PipValue=0.01 ⇒ PnL = vol × Δprecio. **0** mismatches vs CSV B. SL limpios ⇒ RealizedR = −1.000; TP limpios ⇒ +2.000. |
| Equity | **OK** | `equity += net` tras cada cierre (`TradeSimulator.cs` L225). Curva B: 531 puntos; último Equity = 10 000 + Σ NetPnL = **8130.414510**. Orden SignalId trades↔equity idéntico. |
| Gaps | **OK** | Open más allá de SL/TP → salida al open, `GapExit=true` (`PositionExitEvaluator.cs` L42–45, L76–79). B: **4** gap exits. |
| SL-first ambiguas | **OK** | Misma vela SL+TP → SL + `AmbiguousSameBar` (`PositionExitEvaluator.cs` L50–58, L84–92). B: **7** casos. |
| Consistencia trades/rechazos/equity | **OK** | B: 531 + 108 = **639** señales. GrossPnL = NetPnL (comisión no modelada). RiskBudget = 0.5% × EquityAtEntry en todas las filas. TP = Entry ± 2R en **531/531**. SL lado correcto **531/531**. |

### 1.2 Errores verificables de implementación

**Ninguno encontrado** con evidencia de archivo/línea/ejemplo que altere el signo del PnL o rompa las invariantes anteriores.

### 1.3 Limitaciones de modelado (no son bugs de código)

| Limitación | Por qué importa | No es bug porque… |
|------------|-----------------|-------------------|
| OHLC asumido mid | Bid/Ask reales del broker pueden diferir | Documentado en `assumptions.md`; convención explícita |
| SL-first en OHLC ambiguo | Sesgo bajista (~−311 USD en 7 trades B) | Requisito del MVP (`assumptions.md` / spec) |
| Specs DEMO snapshot | PipValue/vol históricos pueden variar | Declarado; no se inventaron fills |
| Comisión/slippage = 0 | Scenario B ≠ costes reales completos | Explícito en `summary.md` |

No se corrige nada en esta fase.

---

## 2. Descomposición — Scenario B (spread 0.40)

Fuente: `artifacts/trade-simulator-mvp/trades_spread040.csv`, `rejected_signals.csv`, `equity_curve.csv`, `summary.md`.  
Geometría: join con `artifacts/t3-breakout-signal-mvp/signals_replay.csv` vía `SignalId = L{LevelId}-B{BreakoutBarIndex}` (información suficiente, sin inferir).

### 2.1 Totales

| Métrica | Valor |
|---------|-------|
| Señales | 639 |
| Trades | 531 |
| Rechazos | 108 (99 `PositionAlreadyOpen`, 9 `MinVolumeExceedsRiskBudget`) |
| Net PnL | **−1 869.59** |
| Profit Factor | **0.8767** |
| Average R | **−0.0837** |
| Win rate | 30.32% (161/531) |
| Ending Equity | 8 130.41 |

### 2.2 BUY vs SELL

| Dirección | n | Win rate | Net PnL | Profit Factor | Average R |
|-----------|---|----------|---------|---------------|-----------|
| **BUY** | 248 | 26.61% | **−2 034.04** | **0.729** | **−0.202** |
| **SELL** | 283 | 33.57% | **+164.45** | **1.022** | **+0.020** |
| Total | 531 | 30.32% | −1 869.59 | 0.877 | −0.084 |

**Hecho:** la pérdida agregada es casi enteramente **BUY**. SELL está ligeramente por encima de break-even.

Comparación frictionless (A):

| Dirección | A Net PnL | B Net PnL |
|-----------|-----------|-----------|
| BUY | −1 846.84 | −2 034.04 |
| SELL | +960.44 | +164.45 |

SELL se degrada mucho con spread; BUY ya era muy negativo sin spread.

### 2.3 Resultados mensuales (B, por `EntryTime`)

| Mes | n | WR % | Net PnL |
|-----|---|------|---------|
| 2025-11 | 18 | 38.9 | +140.27 |
| 2025-12 | 40 | 27.5 | −353.72 |
| 2026-01 | 44 | 25.0 | −430.17 |
| 2026-02 | 40 | 37.5 | +156.94 |
| 2026-03 | 42 | 33.3 | −28.13 |
| 2026-04 | 48 | 29.2 | −39.28 |
| 2026-05 | 53 | 22.6 | **−686.50** |
| 2026-06 | 43 | 39.5 | +269.09 |
| 2026-07 | 44 | 36.4 | +129.61 |
| 2026-08 | 47 | 23.4 | **−529.36** |
| 2026-09 | 48 | 29.2 | −215.36 |
| 2026-10 | 50 | 26.0 | **−470.01** |
| 2026-11 | 14 | 42.9 | +187.04 |

Meses negativos: **8 / 13**.  
Top 3 meses negativos (may/ago/oct 2026) = **61.2%** de la suma de meses negativos.

### 2.4 Concentración de pérdidas

| Vista | Observación |
|-------|-------------|
| Mensual | Clusters claros (may, ago, oct 2026); no es un único mes |
| Por trade | Peor 20% de trades perdedores ≈ **23%** de la pérdida bruta → **poco concentrado a nivel trade** |
| Conclusión | **Distribuida** en el tiempo, con **clusters mensuales** |

### 2.5 Distribución de Realized R (B)

| Bucket | Count |
|--------|------:|
| (−1.5, −0.5] | 370 |
| [1.5, 2.5) | 160 |
| ≥ 2.5 | 1 |

- Cerca de −1R: **368** (casi todos los SL no-gap).
- Cerca de +2R: **160** (TP limpios).
- Gap exits: 3× SL (R ≈ −1.02…−1.07) + 1× TP favorable (R ≈ +5.71, PnL +265).

### 2.6 SL / TP / Gap / EndOfData (B)

| Exit | GapExit | Ambiguous | Count | Notas |
|------|---------|-----------|------:|-------|
| StopLoss | False | False | 360 | RealizedR = −1 exacto |
| TakeProfit | False | False | 160 | RealizedR = +2 exacto |
| StopLoss | False | True | 7 | SL-first; Net ≈ **−311** |
| StopLoss | True | False | 3 | salida al open |
| TakeProfit | True | False | 1 | gap favorable |
| EndOfData | — | — | **0** | |

Tasa SL = **69.7%**, TP = **30.3%**.  
Expectativa teórica si SL=−1R y TP=+2R:  
`0.303×2 + 0.697×(−1) ≈ −0.090` ≈ Average R observado (−0.084).

### 2.7 Contribución de costes (A vs B)

| Concepto | Valor |
|----------|------:|
| Net A (frictionless) | −886.40 |
| Net B (spread 0.40) | −1 869.59 |
| Δ (B − A) | **−983.18** |
| Trades comunes (mismo SignalId) | 527 |
| Δ Net en comunes | −516.19 |
| Solo en A (6 trades) | Net A +288.09 |
| Solo en B (4 trades) | Net B −178.90 |

**Hecho:** el spread explica ~**53%** de la pérdida de B (|Δ|/|Net B|), pero **A ya es negativo**.  
Comisión = 0 (no modelada); GrossPnL = NetPnL en todos los trades B.  
El spread se aplica **una vez** vía Bid/Ask (no se resta otra vez del PnL).

### 2.8 Horizontal vs Diagonal (join con `signals_replay.csv`)

| Geometry | n | WR % | Net PnL | PF | Average R |
|----------|---|------|---------|----|-----------|
| Horizontal | 494 | 30.16 | −1 769.99 | 0.875 | −0.088 |
| Diagonal | 37 | 32.43 | −99.59 | 0.902 | −0.027 |

Ambos negativos; la muestra diagonal es pequeña. No hay señal de que la pérdida sea un artefacto solo de un tipo de geometría.

---

## 3. Validación temporal (70% / 30%)

Corte por índice de barra de señal: `SignalBarIndex < 16 530` (70% de 23 615).  
Primer trade EVAL: señal 16530, entrada `2026-07-29T18:15:00Z`.

> **Aviso:** este split es **descriptivo**. Experimentos previos del repo usaron el mismo CSV con particiones develop/validate/OOS (p. ej. 60/20/20 en `docs/experiments/report_k2_vs_k3_latest.md`). El último 30% **no** debe presentarse como OOS independiente.

### Scenario B

| Segmento | n | WR % | Net PnL | PF | Average R | Ventana aprox. |
|----------|---|------|---------|----|-----------|----------------|
| DEV 70% | 369 | 31.17 | **−983.00** | 0.908 | −0.055 | 2025-11-18 → 2026-07-29 |
| EVAL 30% | 162 | 28.40 | **−886.58** | 0.800 | −0.148 | 2026-07-29 → 2026-11-12 |

### Scenario A (referencia)

| Segmento | n | WR % | Net PnL | PF | Average R |
|----------|---|------|---------|----|-----------|
| DEV 70% | 370 | 33.24 | −107.17 | 0.990 | +0.007 |
| EVAL 30% | 163 | 29.45 | −779.24 | 0.842 | −0.117 |

**Hecho:** en frictionless el tramo temprano es casi plano; el deterioro fuerte aparece en el tramo tardío. Con spread, **ambos** tramos pierden.

---

## 4. Diagnóstico (hechos vs hipótesis)

### 4.1 ¿Errores verificables de implementación?

**Hecho:** No. Invariantes de entrada, geometría SL/TP=2R, PnL, equity y conteos 639 están consistentes con el código citado en §1.

### 4.2 ¿La pérdida existe también sin costes?

**Hecho:** Sí. Scenario A Net PnL = **−886.40**, PF = **0.945**, Average R = **−0.031**.

### 4.3 ¿BUY y SELL se comportan distinto?

**Hecho:** Sí. En B, BUY PF 0.73 / WR 26.6%; SELL PF 1.02 / WR 33.6%. La pérdida agregada es un fenómeno **BUY-dominant**.

### 4.4 ¿Pérdidas concentradas o distribuidas?

**Hecho:** **Distribuidas** en 8/13 meses y en muchos trades (peor 20% de losers ≈ 23% de la pérdida bruta), con **clusters** mensuales (may/ago/oct 2026).

### 4.5 ¿Qué hipótesis explica mejor el resultado?

| # | Afirmación | Tipo |
|---|------------|------|
| H1 | Con payoff objetivo 2R, el break-even de win rate es ≈ **33.3%**. Observado A **32.1%**, B **30.3%**. La expectativa teórica desde tasas SL/TP (−0.09R) coincide con Average R. | **Hecho + interpretación directa** |
| H2 | El spread **amplifica** (~−983 USD) pero **no crea** la pérdida (A ya negativo). | **Hecho** |
| H3 | Los breakouts de **resistencia (BUY)** fallan más que los de **soporte (SELL)** en esta muestra. | **Hipótesis** (patrón observado; causa de mercado no demostrada) |
| H4 | El tramo tardío (post ~2026-07-29) es más adverso, incluso sin spread (A EVAL −779). | **Hecho descriptivo** (no OOS limpio) |
| H5 | La política SL-first y 4 gap exits no explican el resultado global (ambiguas −311; gaps netos +149). | **Hecho** |
| H6 | Un bug de doble spread / lookahead / PnL explica la pérdida. | **Rechazada** por §1 |

**Hipótesis mejor soportada:** la estrategia, tal como está especificada (1 posición, SL estructural, TP=2R, entrada N+1), produce una **tasa de acierto insuficiente** frente al payoff 2R en este dataset; el asymmetry BUY/SELL y el régimen temporal tardío concentran el daño; el spread modelado empeora el resultado sin invertirlo.

---

## 5. Referencias de artefactos

| Archivo | Uso |
|---------|-----|
| `artifacts/trade-simulator-mvp/trades_spread040.csv` | Descomposición B |
| `artifacts/trade-simulator-mvp/trades_frictionless.csv` | Baseline sin costes / Δ spread |
| `artifacts/trade-simulator-mvp/rejected_signals.csv` | 108 rechazos B |
| `artifacts/trade-simulator-mvp/equity_curve.csv` | Consistencia equity |
| `artifacts/trade-simulator-mvp/summary.md` | Totales A/B |
| `artifacts/trade-simulator-mvp/assumptions.md` | Convención Bid/Ask y costes |
| `artifacts/t3-breakout-signal-mvp/signals_replay.csv` | Geometry Horizontal/Diagonal |
| `XauTradeSimulator/Simulation/TradeSimulator.cs` | Orquestación N+1 / equity |
| `XauTradeSimulator/Simulation/PositionExitEvaluator.cs` | Bid/Ask, gaps, SL-first |
| `XauTradeSimulator/Simulation/TradeSizer.cs` | SL/TP/volumen/PnL |
| `XauTradeSimulator/Simulation/BidAskQuote.cs` | Mid ± spread/2 |

---

## 6. Conclusión operativa

El simulador MVP es **internamente coherente** con sus supuestos documentados. Las pérdidas observadas **no se atribuyen a un error de implementación verificable** en esta revisión.  
El resultado negativo es **económico**: win rate sub-break-even a 2R, asimetría BUY/SELL, deterioro en el tramo reciente, y fricción de spread adicional.

**Siguiente paso (fuera de alcance):** revisión humana. No optimizar parámetros ni variantes en esta tarea.
