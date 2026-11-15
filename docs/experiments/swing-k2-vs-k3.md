# Experimento A/B — SwingDetector K=2 vs K=3

Harness independiente: `XauSwingAbHarness`.

## Cómo reproducir

```bash
dotnet test XauSwingAbHarness.Tests/XauSwingAbHarness.Tests.csproj -c Release
dotnet run --project XauSwingAbHarness/XauSwingAbHarness.csproj -c Release -- \
  /home/emsetec/cAlgo/Data/cBots/XauM15HistoryExporter/xauusd_m15_2025-11_2026-11.csv \
  artifacts/swing-ab
```

Salidas en `artifacts/swing-ab/`:

- `dataset_validation.txt`, `temporal_gaps.csv`
- `events_A_K2.csv`, `events_B_K3.csv`
- `metrics_descriptive.csv`, `metrics_lifetime.csv`, `metrics_predictive.csv`
- `report_k2_vs_k3.md`, `limitations.md`

## Reglas de aislamiento

No modifica producción ni `DefaultSwingStrength = 3`. Enlaza el código S/R por `Compile Include` de solo lectura.
