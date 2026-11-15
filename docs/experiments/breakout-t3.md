# Experimento — Confirmación de breakout tras T=3

Salida: `artifacts/swing-ab-breakout-t3/`  
Originales preservados: `artifacts/swing-ab-original-k2k3/`

## Reproducir

```bash
dotnet test XauSwingAbHarness.Tests/XauSwingAbHarness.Tests.csproj -c Release
dotnet run --project XauSwingAbHarness/XauSwingAbHarness.csproj -c Release -- \
  /home/emsetec/cAlgo/Data/cBots/XauM15HistoryExporter/xauusd_m15_2025-11_2026-11.csv \
  artifacts/swing-ab-breakout-t3
```

Abortará si el SHA-256 del CSV no es `023c31d2…be95d`.

## Hipótesis de confirmación (experimental)

- Resistencia: `Close > LevelPrice + 0.15×ATR`
- Soporte: `Close < LevelPrice - 0.15×ATR`
- Ventana: velas `T3+1 … T3+8` (sin la vela T3)
- Diagonales: `ProjectPriceAtBar` causal
- ≠ `BreakoutBufferAtr` productivo (0.10)
