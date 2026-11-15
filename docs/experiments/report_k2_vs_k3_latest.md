# Informe comparativo Swing K=2 vs K=3 (XAUUSD M15)

## Dataset

- Path: `/home/emsetec/cAlgo/Data/cBots/XauM15HistoryExporter/xauusd_m15_2025-11_2026-11.csv`
- SHA-256: `023c31d2e0b45a85ad1411350d28539e32c36a60cc96f05d0c8945cbe2bce95d`
- Barras: 23615
- Rango: 2025-11-14T00:00:00.0000000Z → 2026-11-13T21:30:00.0000000Z
- Duplicados / OHLC / orden / non-finite: 0 / 0 / 0 / 0
- Saltos temporales: 258
  - IntradayOrSessionUnknown: 5
  - MultiDayUnknown: 3
  - NearM15Irregular: 1
  - ShortGapUnknown: 133
  - WeekdayDailyBreak: 65
  - WeekendClose: 51

## Diseño experimental

- Variante A: `SupportResistanceEngine(swingStrength: 2)`
- Variante B: `SupportResistanceEngine(swingStrength: 3)`
- Lookback idéntico: 480
- ATR period idéntico: 14
- Procesamiento: `ProcessClosedBar` secuencial sobre **todo** el CSV (sin truncar con `ReplayClosedBars` lookback).
- Preload inherente al motor: A=`16` (14+2), B=`17` (14+3). Diferencia documentada, no igualada artificialmente.
- Split temporal fijo: 60% develop / 20% validate / 20% OOS (por índice cronológico).
- Eventos T≥3 **no** son operaciones reales.

## Métricas descriptivas (global)

| Variante | K | Swings | Latencia media conf. | Niveles | Activados | T=2 cand. | T≥3 | Invalid. | T3/1000 barras |
|----------|---|--------|----------------------|---------|-----------|-----------|-----|----------|----------------|
| A_K2 | 2 | 6314 | 2.00 | 4051 | 2824 | 2808 | 1829 | 3722 | 77.45 |
| B_K3 | 3 | 4523 | 3.00 | 3233 | 2332 | 2324 | 1530 | 2956 | 64.79 |

## Por segmento

| Variante | Segmento | Swings | T=2 | T≥3 | Invalid. | T3/1000 |
|----------|----------|--------|-----|-----|----------|---------|
| A_K2 | develop | 3829 | 1702 | 1090 | 2229 | 76.93 |
| A_K2 | oos | 1227 | 545 | 368 | 722 | 77.92 |
| A_K2 | validate | 1258 | 561 | 371 | 771 | 78.55 |
| B_K3 | develop | 2734 | 1396 | 913 | 1759 | 64.44 |
| B_K3 | oos | 910 | 463 | 304 | 582 | 64.37 |
| B_K3 | validate | 879 | 465 | 313 | 615 | 66.27 |

## Vida útil de niveles invalidados

| Variante | Segmento | N | Media barras | Mediana |
|----------|----------|---|--------------|---------|
| A_K2 | develop | 2270 | 304.5 | 17.0 |
| A_K2 | global | 3722 | 243.9 | 17.0 |
| A_K2 | oos | 687 | 64.5 | 14.0 |
| A_K2 | validate | 765 | 225.4 | 19.0 |
| B_K3 | develop | 1793 | 331.7 | 21.0 |
| B_K3 | global | 2956 | 261.3 | 21.0 |
| B_K3 | oos | 555 | 72.8 | 19.0 |
| B_K3 | validate | 608 | 225.6 | 24.0 |

## Evaluación predictiva (T≥3, retrospectiva)

Metodología fijada a priori: tras el primer T≥3 de cada nivel, horizonte H∈{16,32}, umbral 0.5×ATR; Resistance favorece alza, Support favorece baja. No son trades.

| Variante | Segmento | H | N | HitRate | Media MFE·ATR | Media MAE·ATR | Invalid. en H |
|----------|----------|---|---|---------|---------------|---------------|---------------|
| A_K2 | develop | 16 | 1090 | 54.0 % | 2.42 | 2.30 | 535 |
| A_K2 | develop | 32 | 1090 | 54.0 % | 3.44 | 3.28 | 620 |
| A_K2 | oos | 16 | 368 | 54.9 % | 2.40 | 2.36 | 186 |
| A_K2 | oos | 32 | 368 | 54.9 % | 3.43 | 3.29 | 213 |
| A_K2 | validate | 16 | 371 | 53.9 % | 2.50 | 2.40 | 192 |
| A_K2 | validate | 32 | 371 | 53.9 % | 3.46 | 3.43 | 228 |
| B_K3 | develop | 16 | 913 | 55.0 % | 2.43 | 2.32 | 439 |
| B_K3 | develop | 32 | 913 | 55.0 % | 3.46 | 3.32 | 514 |
| B_K3 | oos | 16 | 304 | 55.9 % | 2.30 | 2.32 | 159 |
| B_K3 | oos | 32 | 304 | 55.9 % | 3.31 | 3.20 | 182 |
| B_K3 | validate | 16 | 313 | 55.6 % | 2.52 | 2.38 | 166 |
| B_K3 | validate | 32 | 313 | 55.6 % | 3.50 | 3.33 | 195 |
| A_K2 | global | 16 | 1829 | 54.2 % | 2.43 | 2.34 | 913 |
| A_K2 | global | 32 | 1829 | 54.2 % | 3.45 | 3.31 | 1061 |
| B_K3 | global | 16 | 1530 | 55.3 % | 2.42 | 2.33 | 764 |
| B_K3 | global | 32 | 1530 | 55.3 % | 3.44 | 3.30 | 891 |

## Lectura cautelosa

- Más swings/señales **no** implica superioridad.
- Comparar HitRate y MFE/MAE en **validate** y **oos**, no solo develop.
- Invalidación OHLC por Close es un proxy (sin ticks Bid/Ask).
- `DefaultSwingStrength = 3` en producción **no** se modifica por este informe.
