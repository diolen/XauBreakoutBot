# Execution prep MVP — limitaciones

1. v1.7 no llama a ExecuteMarketOrder / PlaceLimitOrder / PlaceStopOrder.
2. `OrdersEnabled` forzado a false en el host.
3. Cuentas LIVE: todos los ExecutionPlan rechazados.
4. Sin cotización histórica Ask/Bid: no hay ExecutionPlan en replay OHLC.
5. Logs de SymbolSpec requieren ejecución DEMO en cTrader (no inventados en CI).
6. Slippage/gaps no modelados; riesgo real no garantizado al 0,5 %.
