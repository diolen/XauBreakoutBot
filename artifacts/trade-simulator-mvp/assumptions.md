# Trade Simulator MVP — Assumptions

## Datos

- CSV SHA-256: `023c31d2e0b45a85ad1411350d28539e32c36a60cc96f05d0c8945cbe2bce95d`
- Barras: 23615
- Gaps temporales: **no rellenados**.
- Sin lookahead: señales solo con velas cerradas disponibles.

## OHLC / Bid-Ask

- El export histórico **no documenta** si OHLC es Bid, Ask o mid.
- **Convención fijada:** OHLC = precio de referencia (mid).
- `Bid = Mid − Spread/2`, `Ask = Mid + Spread/2`.
- Spread=0 ⇒ Bid = Ask = Mid (Scenario A).
- Entrada BUY = Ask open de N+1; SELL = Bid open de N+1.
- Salida BUY = Bid; SELL = Ask (coherente; spread no se resta dos veces).

## Entrada

- Señal al cierre de N → entrada solo en apertura de N+1.
- Sin N+1 → rechazo `MissingNextBar`.
- High/Low de N+1 **no** deciden retrospectivamente la entrada.

## SL / TP

- SL = geometría causal del breakout (bounds ± 0.10×ATR); no reconstruida después.
- TP recalculado desde entrada efectiva: R = |Entry−SL|, TP = Entry ± 2R.
- No se usa el TP teórico desde ConfirmationClose.

## Riesgo / volumen (snapshot DEMO observado)

| Spec | Valor |
|------|-------|
| PipSize | 0.01 |
| PipValue | 0.01 |
| LotSize | 100 |
| VolumeMin/Max/Step | 1 / 10000 / 1 |
| Risk % | 0.5% del Equity actual |
| Initial Equity | 10 000 |

Estas specs son un snapshot actual, **no** garantía histórica.
Volumen: misma semántica Risk (`VolumeForFixedRisk` + floor). Nunca se incrementa.

## Costes

| Escenario | Spread | Commission modeled | Slippage modeled |
|-----------|--------|--------------------|------------------|
| A `frictionless` | 0 | False | False |
| B `spread040` | 0.4 | False | False |

- Commission = 0 y **explícitamente no modelada**.
- Slippage = 0 y **explícitamente no modelado**.
- Scenario B **no** afirma costes reales completos.

## Gestión de posición / gaps

- Máximo 1 posición simultánea; señales con posición abierta → `PositionAlreadyOpen`.
- SL+TP misma vela → SL primero + `AmbiguousSameBar=true`.
- Apertura más allá de SL/TP → salida al open simulado (`GapExit=true`), no al precio ideal.
- Serie termina con posición → cierre al último precio disponible (`EndOfData`).

## Prohibiciones

- No se ejecutaron órdenes reales ni DEMO.
- No se modificaron algoritmos productivos Swing / S/R / T3 / Risk / ExecutionPrep.
- No se optimizaron parámetros.
