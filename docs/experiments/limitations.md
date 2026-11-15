# Limitaciones del harness A/B

1. Sin datos tick: invalidación y candidatos usan Close OHLC / estado del motor tras vela cerrada.
2. `EntryCandidateSelector` estructural (T=2 Active) no exige lado Bid/Ask de mercado en este harness; se registra el nacimiento estructural del candidato.
3. PreloadBars difiere con K (A=16, B=17) por fórmula del motor `ATR+K`.
4. No se simulan órdenes, slippage ni spreads.
5. Eventos T≥3 no son operaciones; la métrica predictiva es un proxy de reacción de precio.
6. Huecos temporales no se rellenan; horizontes cuentan velas disponibles, no minutos de reloj.
7. Niveles fusionados/absorbidos pueden alterar Ids y conteos relativos a un escenario sin merge.
8. No se dispone de un ledger de invalidaciones tick-faithful en el motor offline.
