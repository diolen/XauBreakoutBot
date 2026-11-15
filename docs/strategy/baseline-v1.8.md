# XauBreakoutBot — Baseline de investigación v1.8

| Campo | Valor |
|-------|--------|
| Tag | `v1.8-research-baseline` |
| Estado | **Baseline reproducible (investigación)** |
| Trading automático | **No validado / no autorizado** |
| Órdenes | **Deshabilitadas** (`OrdersEnabled` forzado a `false`) |

Este documento fija el estado del proyecto al consolidar el simulador histórico y el diagnóstico de pérdidas.  
No introduce filtros SELL-only ni cambios de parámetros.

---

## 1. Arquitectura actual

```
CSV M15 histórico / cBot M15 cerrado
        │
        ▼
SupportResistanceEngine (Swing K=3, ATR, niveles H/D)
        │
        ▼
T3BreakoutSignalDetector  →  T3BreakoutSignal (BUY+SELL)
        │
        ├─► RiskPlanCalculator     → RiskPlan (SL/TP/volumen teóricos)
        ├─► ExecutionPlanBuilder   → ExecutionPlan DEMO prep (sin envío)
        └─► XauTradeSimulator      → trades / equity / métricas (offline)
```

| Capa | Ubicación | Rol |
|------|-----------|-----|
| S/R + Swing | `XauBreakoutBot/SupportResistance/` | Detección causal K=3 |
| Señales T3 | `T3BreakoutSignalDetector` | Breakout estructural post T=3 |
| Riesgo | `XauBreakoutBot/Risk/` | SL estructural, TP 2R, sizing 0.5% |
| Execution prep | `XauBreakoutBot/Execution/` | Plan Ask/Bid; LIVE rechazado; sin órdenes |
| Simulador | `XauTradeSimulator/` | Replay económico A/B |
| Diagnóstico | `docs/strategy/trade-simulator-diagnostic.md` | Análisis de pérdidas |

---

## 2. Parámetros de estrategia (baseline)

| Parámetro | Valor | Notas |
|-----------|-------|-------|
| SwingStrength (K) | **3** | Productivo; no optimizar aquí |
| Direcciones | **BUY + SELL** | Sin filtro SELL-only |
| Señal | T3 Structural Breakout | Ventana 8 velas; 1 señal/nivel |
| RiskPercent | **0.5%** del Equity | |
| StopBufferAtr | **0.10** | SL = bound ± buffer×ATR |
| RewardRiskRatio | **2.0** | TP desde entrada efectiva |
| MaxOpenPositions | **1** | Label del bot |
| ExecutionPrep | Activo en cBot | `OrdersEnabled = false` forzado |
| Simulator scenarios | A frictionless / B spread 0.40 | Comisión/slippage no modelados |

---

## 3. Dataset

| Campo | Valor |
|-------|--------|
| CSV | `/home/emsetec/cAlgo/Data/cBots/XauM15HistoryExporter/xauusd_m15_2025-11_2026-11.csv` |
| SHA-256 | `023c31d2e0b45a85ad1411350d28539e32c36a60cc96f05d0c8945cbe2bce95d` |
| Barras | 23 615 |
| Rango | 2025-11-14 → 2026-11-13 (UTC) |
| Señales T3 esperadas (K=3) | **639** |

---

## 4. Reproducción

```bash
# Build + tests
dotnet build XauBreakoutBot.sln -c Release
dotnet test XauBreakoutBot.sln -c Release

# Simulador histórico (valida SHA-256 y gate 639 señales)
dotnet run --project XauTradeSimulator/XauTradeSimulator.csproj -c Release -- \
  /home/emsetec/cAlgo/Data/cBots/XauM15HistoryExporter/xauusd_m15_2025-11_2026-11.csv \
  artifacts/trade-simulator-mvp
```

Artefactos de referencia:

- `artifacts/trade-simulator-mvp/trades_frictionless.csv`
- `artifacts/trade-simulator-mvp/trades_spread040.csv`
- `artifacts/trade-simulator-mvp/rejected_signals.csv`
- `artifacts/trade-simulator-mvp/equity_curve.csv`
- `artifacts/trade-simulator-mvp/summary.md`
- `artifacts/trade-simulator-mvp/assumptions.md`
- `docs/strategy/trade-simulator-diagnostic.md`

---

## 5. Resultado histórico — Scenario B (spread 0.40)

Fuente: `artifacts/trade-simulator-mvp/summary.md`

| Métrica | Valor |
|---------|-------|
| Señales | 639 |
| Trades abiertos | 531 |
| Trades rechazados | 108 |
| Win rate | 30.32% |
| Net PnL | **−1 869.59** |
| Profit Factor | **0.8767** |
| Average R | **−0.0837** |
| Max DD | 2 516.21 (24.23%) |
| Ending Equity | 8 130.41 (desde 10 000) |

Frictionless (A) también negativo: Net PnL **−886.40**, PF **0.945**.  
Diagnóstico: sin errores verificables de simulador; pérdida económica (WR bajo break-even 2R; BUY concentra pérdidas). Ver `trade-simulator-diagnostic.md`.

---

## 6. Limitaciones del simulador

1. OHLC tratado como mid/referencia; Bid/Ask sintéticos = mid ± spread/2.
2. Comisión y slippage **no modelados**.
3. Specs DEMO (PipSize/PipValue/vol) son snapshot, no garantía histórica.
4. SL-first en velas ambiguas (sesgo conservador documentado).
5. Gaps no rellenados; salida en gap al open simulado, no al nivel ideal.
6. Scenario B **no** representa costes reales completos.
7. Splits temporales del diagnóstico **no** son OOS independientes (dataset ya usado en experimentos previos).

---

## 7. Hipótesis pendiente

**SELL-only** — en el escenario B, SELL fue ligeramente positivo (Net ≈ +164, PF ≈ 1.02) mientras BUY concentró la pérdida.  

Esta hipótesis **no está implementada** en el baseline.  
No se filtra SELL-only aquí; requiere experimentación futura explícita y controlada.

---

## 8. Estado de validación

| Afirmación | Estado |
|------------|--------|
| Detección S/R + T3 reproducible (639 señales) | Sí |
| RiskPlan / ExecutionPlan sin envío de órdenes | Sí |
| Simulador histórico A/B | Sí |
| Diagnóstico de pérdidas documentado | Sí |
| Estrategia validada para trading automático | **No** |
| Autorización para ejecutar órdenes | **No** |

**Baseline de investigación únicamente.** No operar capital real ni DEMO con envío de órdenes a partir de este tag.
