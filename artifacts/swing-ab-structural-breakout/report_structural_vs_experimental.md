# Structural (A) vs Experimental (C) Breakout tras T=3

SHA-256: `023c31d2e0b45a85ad1411350d28539e32c36a60cc96f05d0c8945cbe2bce95d` | Barras: 23615
Originales preservados en `artifacts/swing-ab-original-k2k3/` y `artifacts/swing-ab-breakout-t3/`.

## Definiciones

- **A Structural:** Close más allá de Upper/Lower ± 0.10×AtrCurrent + invalidación; nivel válido justo antes.
- **C Experimental:** primer Close más allá de Mid ± 0.15×AtrAtT3; permite misma vela que invalidación.

## Clasificación mutuamente excluyente

| Variante | OnlyC | A∩C misma vela | A∩C distinta | OnlyA | Neither |
|----------|-------|----------------|--------------|-------|---------|
| A_K2 | 48 | 671 | 88 | 0 | 1022 |
| B_K3 | 40 | 581 | 58 | 0 | 851 |

## ConfirmationRate y HitRate (ExcludeAmbiguous, baseline)

| Var | Def | Seg | ConfirmRate | N_conf | HitRate | Amb | Unres | BarsToConfirm |
|-----|-----|-----|-------------|--------|---------|-----|-------|---------------|
| A_K2 | A | global | 41.5 % | 759 | 47.4 % | 101 | 0 | 3.04 |
| A_K2 | A | oos | 41.6 % | 153 | 42.9 % | 20 | 0 | 3.11 |
| A_K2 | C | global | 44.1 % | 807 | 48.8 % | 104 | 0 | 2.87 |
| A_K2 | C | oos | 44.8 % | 165 | 44.7 % | 24 | 0 | 2.95 |
| B_K3 | A | global | 41.8 % | 639 | 47.2 % | 86 | 0 | 2.98 |
| B_K3 | A | oos | 41.1 % | 125 | 45.0 % | 16 | 0 | 3.10 |
| B_K3 | C | global | 44.4 % | 679 | 47.3 % | 89 | 0 | 2.89 |
| B_K3 | C | oos | 44.1 % | 134 | 44.9 % | 16 | 0 | 2.93 |

## Sensibilidades (ExcludeAmbiguous, OOS)

| Var | Def | Filter | ConfirmRate | HitRate | N_conf |
|-----|-----|--------|-------------|---------|--------|
| A_K2 | A | gap_S1_all | 38.2 % | 39.5 % | 94 |
| A_K2 | C | gap_S1_all | 41.9 % | 40.7 % | 103 |
| A_K2 | A | gap_S2_weekend_multiday | 41.7 % | 45.2 % | 143 |
| A_K2 | C | gap_S2_weekend_multiday | 45.2 % | 47.0 % | 155 |
| A_K2 | A | decimated_16 | 46.9 % | 46.4 % | 76 |
| A_K2 | C | decimated_16 | 50.0 % | 47.2 % | 81 |
| B_K3 | A | gap_S1_all | 40.7 % | 41.3 % | 83 |
| B_K3 | C | gap_S1_all | 44.6 % | 39.8 % | 91 |
| B_K3 | A | gap_S2_weekend_multiday | 40.7 % | 45.6 % | 116 |
| B_K3 | C | gap_S2_weekend_multiday | 43.5 % | 45.9 % | 124 |
| B_K3 | A | decimated_16 | 41.7 % | 47.5 % | 63 |
| B_K3 | C | decimated_16 | 44.4 % | 46.0 % | 67 |

No son operaciones. DefaultSwingStrength=3 intacto.
