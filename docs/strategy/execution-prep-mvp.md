# MVP — ExecutionPlan (DEMO prep, v1.7)

## Alcance

Preparación de ejecución para XAUUSD en cTrader **DEMO**.

- Lee especificaciones reales de `Symbol` / `Account`.
- Construye `ExecutionPlan` independiente de `RiskPlan`.
- **No envía órdenes** (`OrdersEnabled` forzado a `false` en el host).

No modifica SwingDetector, S/R Engine ni T3BreakoutSignal. `DefaultSwingStrength = 3`.

## Arquitectura

```
T3BreakoutSignal (live, post-replay)
        │
        ├─ RiskPlan (ConfirmationClose teórico)
        │
        └─ ExecutionPlanBuilder
              ├ ExecutionMarketContext (Ask/Bid, spread, IsLive, MinSL/TP…)
              ├ ISymbolRiskApi (VolumeForFixedRisk / AmountRisked / Normalize Down)
              ├ BotPositionGate (Label del bot)
              └ Signal age / duplicate checks
                    │
                    ▼
              ExecutionPlan  (IsEligible + RejectionReason)
```

## Entry ejecutable

| Dirección | Precio de referencia |
|-----------|----------------------|
| BUY | `Ask` |
| SELL | `Bid` |

- SL = estructural del snapshot de breakout (igual fórmula Risk: bounds ± `StopBufferAtr × ATR`).
- TP = `Entry ± RewardRiskRatio × R` recalculado desde el precio ejecutable.
- Volumen **recalculado**; no se reutiliza el de `RiskPlan`/`ConfirmationClose`.

## Rechazos

| Motivo | Código |
|--------|--------|
| Cuenta live | `LiveAccountForbidden` |
| Cotización inválida | `InvalidQuote` |
| Spread inválido / > MaxSpreadPips | `InvalidOrExcessiveSpread` |
| Min SL/TP del símbolo | `SymbolConstraintsViolated` |
| Volumen mínimo > presupuesto | `MinVolumeExceedsRiskBudget` |
| Riesgo > 0,5 % equity | `ExcessiveMonetaryRisk` |
| Posición propia abierta | `MaxOpenPositionsReached` |
| Señal caducada | `SignalExpired` |
| Señal duplicada | `SignalDuplicate` |
| Datos insuficientes | `InsufficientRiskData` |
| Órdenes off (prep OK) | `OrdersDisabled` (IsEligible=true) |

## Log DEMO-safe

Al arranque (`Log Symbol Specs`):

`SymbolName`, `PipSize`, `TickSize`, `PipValue`, volúmenes min/max/step, `LotSize`, Bid/Ask/Spread, `Account.IsLive`, `Account.Asset`.

**Sin** número de cuenta ni credenciales.

Unidades de riesgo: volumen en **unidades** (no lotes); stop en **pips** (`distancia / PipSize`).

## Parámetros

| Parámetro | Default |
|-----------|---------|
| `Max Spread Pips` | 50 |
| `Max Signal Age Bars` | 2 |
| `Orders Enabled` | false (forzado en código) |
| `Enable Execution Plans` | true |

## Limitaciones

1. Sin ejecución real de órdenes en v1.7.
2. ExecutionPlan solo en señales **live** post-replay (Ask/Bid actuales; no cotización histórica).
3. LIVE siempre rechazado.
4. Slippage/gaps no cuantificados.
5. Logs de specs DEMO requieren ejecutar el cBot en cTrader; no se inventan en CI.

## Ejemplos

**BUY elegible (prep):** Ask 2650.2, SL 2639, TP = Ask + 2R, vol recalculado, `RejectionReason=OrdersDisabled`.

**LIVE:** `IsEligible=false`, `LiveAccountForbidden`.

**Spread 100 pips > 50:** `InvalidOrExcessiveSpread`.
