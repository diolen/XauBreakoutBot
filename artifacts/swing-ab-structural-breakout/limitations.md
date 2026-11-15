# Limitaciones — Structural vs Experimental Breakout

1. Replay OHLC proxy; live usaría Bid/Ask tick.
2. Snapshots tras ProcessClosedBar; StateBefore capturado antes del process.
3. Mid/bounds leídos del SrLevel live (incluyen merges); AtrAtT3 fijado en el evento T=3.
4. Definición A exige coherencia con invalidación del motor en la misma lógica de bounds.
5. Decimación reduce solape, no implica independencia i.i.d.
6. HitRate ≠ rentabilidad.
7. No se implementó T3BreakoutSignal productivo.