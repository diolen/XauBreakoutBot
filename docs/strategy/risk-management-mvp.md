# MVP — Gestión de riesgo (RiskPlan)

## Alcance

Tras una `T3BreakoutSignal`, el bot calcula un `RiskPlan` (SL, TP, volumen, presupuesto).

**No ejecuta órdenes** (`ExecuteMarketOrder` / `PlaceLimitOrder` / `PlaceStopOrder`).

## Arquitectura

```
T3BreakoutSignal
       │
       ▼
RiskPlanCalculator.Build(signal, RiskParameters, Equity, ISymbolRiskApi, positionGate?)
       │
       ├─ geometría causal del snapshot (Lower/UpperBoundAtBreakout + ATR)
       ├─ EntryReference = ConfirmationClose (teórico)
       ├─ volumen vía APIs de símbolo (VolumeForFixedRisk / AmountRisked / Normalize Down)
       └─ elegibilidad MaxOpenPositions (solo Label del bot)
       │
       ▼
   RiskPlan  (IsValid / RejectionReason / CostsNotFullyModeled)
```

Contratos separados: la señal no contiene volumen ni SL/TP; el plan no muta la señal.

| Componente | Rol |
|------------|-----|
| `RiskParameters` | RiskPercent, StopBufferAtr, RewardRiskRatio, MaxOpenPositions, BotPositionLabel |
| `RiskPlanCalculator` | Cálculo puro + validaciones |
| `ISymbolRiskApi` | Abstracción testeable de APIs cTrader |
| `CTraderSymbolRiskApi` | Adapter live (`Symbol.*`) |
| `PipValueSymbolRiskApi` | Referencia PipValue para tests |
| `BotPositionGate` | Elegibilidad por Label (no gestiona posiciones) |

## Parámetros (defaults)

| Parámetro | Default |
|-----------|---------|
| `RiskPercent` | `0.5` |
| `StopBufferAtr` | `0.10` |
| `RewardRiskRatio` | `2.0` |
| `MaxOpenPositions` | `1` |
| `BotPositionLabel` | `XauBreakoutBot` |

Validación previa a cualquier plan usable. No optimizados en esta fase.

## Fórmulas

**BUY**

- `StopLossPrice = LowerBoundAtBreakout − StopBufferAtr × AtrAtBreakout`
- `TakeProfitPrice = EntryReferencePrice + RewardRiskRatio × R`

**SELL**

- `StopLossPrice = UpperBoundAtBreakout + StopBufferAtr × AtrAtBreakout`
- `TakeProfitPrice = EntryReferencePrice − RewardRiskRatio × R`

donde `R = abs(EntryReferencePrice − StopLossPrice)` y
`EntryReferencePrice = ConfirmationClose` (**no es precio ejecutable**).

`RiskBudget = Equity × RiskPercent / 100`.

Volumen: `VolumeForFixedRisk(budget, stopPips)` → `NormalizeVolumeInUnits(..., Down)`.
Nunca se redondea hacia arriba si supera el presupuesto. Si el mínimo del símbolo supera el presupuesto → rechazo.

## Costes

| Concepto | Tratamiento MVP |
|----------|-----------------|
| Riesgo de precio teórico | `EstimatedPriceRisk` via `AmountRisked` |
| Spread + comisión | `EstimatedTotalRisk` solo si spread y comisión `UsdPerOneLot` son fiables |
| Slippage / gaps | **No cuantificados** |
| Garantía 0,5 % | **No afirmada** |

Si faltan datos de costes → `CostsNotFullyModeled = true` y `EstimatedTotalRisk = null`.

## Posiciones

`BotPositionGate` cuenta solo posiciones con `Label == BotPositionLabel` (y símbolo).
Posiciones manuales u otros robots **no** bloquean. Solo produce elegibilidad.

## Integración cBot (v1.6)

1. Señal T3 nueva.
2. Evaluar gate de posiciones.
3. `RiskPlanCalculator.Build`.
4. Registrar / loguear (`Log Risk Plans`, `Log Replay Risk Plans`).
5. **No enviar órdenes.**

## Limitaciones

1. Entry = ConfirmationClose teórico; ejecución futura debe recalcular con precio/cotización real.
2. Replay histórico sin Equity/specs inventados → plan parcial (SL/TP) y volumen rechazado.
3. `PipValue` de cTrader no es real-time; `VolumeForFixedRisk`/`AmountRisked` pueden ser imprecisos (doc oficial).
4. Comisión solo modelada para `UsdPerOneLot`.
5. Slippage y gaps fuera de alcance.
6. `DefaultSwingStrength = 3` y reglas S/R / T3 intactas.

## Ejemplos

**Válido (BUY, equity 10 000, XAU-like PipSize=0.01 PipValue=0.01):**

- Entry 2650 / SL 2639 / TP 2672 / Budget 50 / Vol 4 units / PriceRisk 44

**Inválido — mínimo supera presupuesto:**

- `RejectionReason = MinVolumeExceedsRiskBudget`, `VolumeInUnits = null`

**Inválido — posición propia abierta:**

- `RejectionReason = MaxOpenPositionsReached`

**Parcial histórico (sin Equity/símbolo):**

- SL/TP calculados; `IsValid = false`; `InvalidEquity` o `InvalidSymbolData`
