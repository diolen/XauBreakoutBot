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