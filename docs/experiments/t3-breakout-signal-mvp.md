# MVP — T3BreakoutSignal

## Semántica

Señal = invalidación estructural OHLC (`Close` vs bounds ± `0.10 × ATR_current`) **después** de T=3, dentro de 8 velas, sin la vela T=3.

- Resistance → `BUY`
- Support → `SELL`
- Una señal por `LevelId`
- El nivel permanece `Invalidated` (no se reactiva)
- La invalidación **no veta** la señal: es el evento de ruptura

## Integración

- `SupportResistanceEngine.LastBarInvalidations`: snapshot causal pre-estado (geometría + StateBefore + Close/ATR).
- `T3BreakoutSignalDetector.ProcessAfterClosedBar` tras cada `ProcessClosedBar`.
- `XauBreakoutBot` v1.5: log de señales; **sin órdenes**.
- Replay de arranque: misma ventana lookback, barra a barra, para no perder snapshots.

## Ciclo de vida / edge cases

| Evento | Comportamiento |
|--------|----------------|
| Invalidación con T=3 previo en ventana | Emite señal; nivel queda `Invalidated` |
| Invalidación sin T=3 | Sin señal |
| Misma vela T=3 + invalidación | Sin señal (no elegibilidad posterior) |
| >8 velas tras T=3 | Elegibilidad caduca; sin señal |
| Merge (LevelId absorbido) | Elegibilidad del id absorbido **caduca**; no se transfiere al survivor |
| Nivel desaparece del motor | Igual que merge: caduca |
| Cambio de geometría | Se usa snapshot en el instante de invalidación |
| Tick Bid/Ask invalidation | No genera señal MVP (solo proxy OHLC) |

No se infiere T=3 retrospectivo desde el estado actual: hace falta el cruce causal `prev < 3 → ≥ 3` observado en barra.

## Parámetros del cBot

- `Enable T3 Breakout Signals` (default true)
- `Log Replay T3 Signals` (default false; evita flood en arranque)

## Limitaciones

1. Señales ≠ órdenes; sin SL/TP/volumen.
2. Replay OHLC no reproduce invalidaciones tick intradía.
3. Merge no transfiere elegibilidad.
4. Parámetros fijos (K=3, ATR=14, buffer 0.10, ventana 8); no optimizados.
