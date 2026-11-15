# Breakout en T=3 — informe experimental

## Preservación

- Resultados originales K=2 vs K=3: `artifacts/swing-ab-original-k2k3/`
- Dataset SHA-256: `023c31d2e0b45a85ad1411350d28539e32c36a60cc96f05d0c8945cbe2bce95d`
- Barras: 23615 | Gaps: 258

## Capacidades del motor (inspección)

- Rol: `LevelRole` Support/Resistance
- Precio horizontal: `Center`; diagonal: `ProjectPriceAtBar(barIndex)` causal
- Estado: Provisional / Active / Invalidated
- T=3: primer `IndependentTouchCount >= 3` por nivel
- Invalidación OHLC productiva: Close vs bounds + `BreakoutBufferAtr=0.10` (no modificada)
- Confirmación experimental: Close vs LevelPrice ± **0.15×ATR** (hipótesis, ≠ productiva)

## HitRate corregido desde Close@T3 (H=16)

| Study | Var | Seg | Policy | N | Hits | Miss | Amb | Unres | HitRate |
|-------|-----|-----|--------|---|------|------|-----|-------|---------|
| baseline_t3_close | A_K2 | global | Optimistic | 1829 | 991 | 838 | 225 | 0 | 54.2 % |
| baseline_t3_close | A_K2 | global | Conservative | 1829 | 766 | 1063 | 225 | 0 | 41.9 % |
| baseline_t3_close | A_K2 | global | ExcludeAmbiguous | 1604 | 766 | 838 | 225 | 0 | 47.8 % |
| baseline_t3_close | A_K2 | oos | Optimistic | 368 | 202 | 166 | 50 | 0 | 54.9 % |
| baseline_t3_close | A_K2 | oos | Conservative | 368 | 152 | 216 | 50 | 0 | 41.3 % |
| baseline_t3_close | A_K2 | oos | ExcludeAmbiguous | 318 | 152 | 166 | 50 | 0 | 47.8 % |
| baseline_t3_close | B_K3 | global | Optimistic | 1530 | 846 | 684 | 208 | 0 | 55.3 % |
| baseline_t3_close | B_K3 | global | Conservative | 1530 | 638 | 892 | 208 | 0 | 41.7 % |
| baseline_t3_close | B_K3 | global | ExcludeAmbiguous | 1322 | 638 | 684 | 208 | 0 | 48.3 % |
| baseline_t3_close | B_K3 | oos | Optimistic | 304 | 170 | 134 | 45 | 0 | 55.9 % |
| baseline_t3_close | B_K3 | oos | Conservative | 304 | 125 | 179 | 45 | 0 | 41.1 % |
| baseline_t3_close | B_K3 | oos | ExcludeAmbiguous | 259 | 125 | 134 | 45 | 0 | 48.3 % |
| decimated_16 | A_K2 | global | Optimistic | 825 | 447 | 378 | 88 | 0 | 54.2 % |
| decimated_16 | A_K2 | global | Conservative | 825 | 359 | 466 | 88 | 0 | 43.5 % |
| decimated_16 | A_K2 | global | ExcludeAmbiguous | 737 | 359 | 378 | 88 | 0 | 48.7 % |
| decimated_16 | A_K2 | oos | Optimistic | 162 | 91 | 71 | 17 | 0 | 56.2 % |
| decimated_16 | A_K2 | oos | Conservative | 162 | 74 | 88 | 17 | 0 | 45.7 % |
| decimated_16 | A_K2 | oos | ExcludeAmbiguous | 145 | 74 | 71 | 17 | 0 | 51.0 % |
| decimated_16 | B_K3 | global | Optimistic | 754 | 403 | 351 | 82 | 0 | 53.4 % |
| decimated_16 | B_K3 | global | Conservative | 754 | 321 | 433 | 82 | 0 | 42.6 % |
| decimated_16 | B_K3 | global | ExcludeAmbiguous | 672 | 321 | 351 | 82 | 0 | 47.8 % |
| decimated_16 | B_K3 | oos | Optimistic | 151 | 84 | 67 | 20 | 0 | 55.6 % |
| decimated_16 | B_K3 | oos | Conservative | 151 | 64 | 87 | 20 | 0 | 42.4 % |
| decimated_16 | B_K3 | oos | ExcludeAmbiguous | 131 | 64 | 67 | 20 | 0 | 48.9 % |
| gap_S1_all | A_K2 | global | Optimistic | 1218 | 651 | 567 | 159 | 0 | 53.4 % |
| gap_S1_all | A_K2 | global | Conservative | 1218 | 492 | 726 | 159 | 0 | 40.4 % |
| gap_S1_all | A_K2 | global | ExcludeAmbiguous | 1059 | 492 | 567 | 159 | 0 | 46.5 % |
| gap_S1_all | A_K2 | oos | Optimistic | 246 | 128 | 118 | 35 | 0 | 52.0 % |
| gap_S1_all | A_K2 | oos | Conservative | 246 | 93 | 153 | 35 | 0 | 37.8 % |
| gap_S1_all | A_K2 | oos | ExcludeAmbiguous | 211 | 93 | 118 | 35 | 0 | 44.1 % |
| gap_S1_all | B_K3 | global | Optimistic | 1028 | 561 | 467 | 141 | 0 | 54.6 % |
| gap_S1_all | B_K3 | global | Conservative | 1028 | 420 | 608 | 141 | 0 | 40.9 % |
| gap_S1_all | B_K3 | global | ExcludeAmbiguous | 887 | 420 | 467 | 141 | 0 | 47.4 % |
| gap_S1_all | B_K3 | oos | Optimistic | 204 | 111 | 93 | 28 | 0 | 54.4 % |
| gap_S1_all | B_K3 | oos | Conservative | 204 | 83 | 121 | 28 | 0 | 40.7 % |
| gap_S1_all | B_K3 | oos | ExcludeAmbiguous | 176 | 83 | 93 | 28 | 0 | 47.2 % |
| gap_S2_weekend_multiday | A_K2 | global | Optimistic | 1699 | 919 | 780 | 212 | 0 | 54.1 % |
| gap_S2_weekend_multiday | A_K2 | global | Conservative | 1699 | 707 | 992 | 212 | 0 | 41.6 % |
| gap_S2_weekend_multiday | A_K2 | global | ExcludeAmbiguous | 1487 | 707 | 780 | 212 | 0 | 47.5 % |
| gap_S2_weekend_multiday | A_K2 | oos | Optimistic | 343 | 187 | 156 | 47 | 0 | 54.5 % |
| gap_S2_weekend_multiday | A_K2 | oos | Conservative | 343 | 140 | 203 | 47 | 0 | 40.8 % |
| gap_S2_weekend_multiday | A_K2 | oos | ExcludeAmbiguous | 296 | 140 | 156 | 47 | 0 | 47.3 % |
| gap_S2_weekend_multiday | B_K3 | global | Optimistic | 1419 | 779 | 640 | 190 | 0 | 54.9 % |
| gap_S2_weekend_multiday | B_K3 | global | Conservative | 1419 | 589 | 830 | 190 | 0 | 41.5 % |
| gap_S2_weekend_multiday | B_K3 | global | ExcludeAmbiguous | 1229 | 589 | 640 | 190 | 0 | 47.9 % |
| gap_S2_weekend_multiday | B_K3 | oos | Optimistic | 285 | 155 | 130 | 42 | 0 | 54.4 % |
| gap_S2_weekend_multiday | B_K3 | oos | Conservative | 285 | 113 | 172 | 42 | 0 | 39.6 % |
| gap_S2_weekend_multiday | B_K3 | oos | ExcludeAmbiguous | 243 | 113 | 130 | 42 | 0 | 46.5 % |

## Confirmación de breakout (máx. 8 velas post-T3)

### A_K2

- T=3: **1829**
- Confirmados: **437** (23.9%)
- No confirmados en ventana: 720
- Invalidados antes/en confirmación: 672
- Excluidos sin geometría: 0
- BarsToConfirm medio: 1.84

### B_K3

- T=3: **1530**
- Confirmados: **376** (24.6%)
- No confirmados en ventana: 573
- Invalidados antes/en confirmación: 581
- Excluidos sin geometría: 0
- BarsToConfirm medio: 2.01


## Post-confirmación (H=16, umbral 0.5×ATR)

| Var | Seg | Policy | N | Hits | Miss | Amb | Unres | HitRate |
|-----|-----|--------|---|------|------|-----|-------|---------|
| A_K2 | global | Optimistic | 437 | 249 | 188 | 47 | 0 | 57.0 % |
| A_K2 | global | Conservative | 437 | 202 | 235 | 47 | 0 | 46.2 % |
| A_K2 | global | ExcludeAmbiguous | 390 | 202 | 188 | 47 | 0 | 51.8 % |
| A_K2 | oos | Optimistic | 99 | 61 | 38 | 13 | 0 | 61.6 % |
| A_K2 | oos | Conservative | 99 | 48 | 51 | 13 | 0 | 48.5 % |
| A_K2 | oos | ExcludeAmbiguous | 86 | 48 | 38 | 13 | 0 | 55.8 % |
| B_K3 | global | Optimistic | 376 | 206 | 170 | 47 | 0 | 54.8 % |
| B_K3 | global | Conservative | 376 | 159 | 217 | 47 | 0 | 42.3 % |
| B_K3 | global | ExcludeAmbiguous | 329 | 159 | 170 | 47 | 0 | 48.3 % |
| B_K3 | oos | Optimistic | 74 | 44 | 30 | 7 | 0 | 59.5 % |
| B_K3 | oos | Conservative | 74 | 37 | 37 | 7 | 0 | 50.0 % |
| B_K3 | oos | ExcludeAmbiguous | 67 | 37 | 30 | 7 | 0 | 55.2 % |

## Limitaciones

# Limitaciones — Breakout T=3

1. Confirmación con buffer 0.15×ATR es hipótesis experimental, distinta de BreakoutBufferAtr productivo (0.10).
2. OHLC no revela orden intrabar; políticas Optimistic/Conservative/Exclude son cotas, no verdad.
3. Invalidación productiva (proxy Close) puede correlacionarse con la confirmación experimental.
4. ATR del buffer de cruce usa snapshot en T=3; ATR post-confirmación usa barra de confirmación.
5. Horizonte y gaps en barras, no en tiempo de reloj.
6. Decimación reduce solape pero no implica independencia i.i.d.
7. Diagonales incluidas vía ProjectPriceAtBar causal; si faltaran anclas se excluirían.
8. No hay órdenes, spreads ni PnL.
9. Resultados originales del A/B previo están en artifacts/swing-ab-original-k2k3/ sin sobrescribir.

No son operaciones reales. `DefaultSwingStrength=3` no se modifica.
