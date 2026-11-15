# Risk Management MVP — limitaciones

1. `EntryReferencePrice = ConfirmationClose` es teórico; no es precio ejecutable.
2. Replay histórico de planes no inventa Equity, spread, comisiones ni specs de contrato: produce SL/TP y rechaza volumen (`InvalidEquity` / `InvalidSymbolData`).
3. Slippage y gaps no están cuantificados; el riesgo real no está garantizado en 0,5 %.
4. Comisión estimable solo para `SymbolCommissionType.UsdPerOneLot`.
5. Sin ejecución de órdenes en esta fase.
6. Artefacto `risk_plans_replay_partial.csv` generado por tests a partir de `artifacts/t3-breakout-signal-mvp/signals_replay.csv`.
