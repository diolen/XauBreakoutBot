# Trade Simulator MVP — Summary

| Campo | Valor |
|-------|-------|
| Dataset SHA-256 | `023c31d2e0b45a85ad1411350d28539e32c36a60cc96f05d0c8945cbe2bce95d` |
| Bars | 23615 |
| First | 2025-11-14T00:00:00.0000000Z |
| Last | 2026-11-13T21:30:00.0000000Z |
| Signals (T3 structural) | 639 |
| Expected signals | 639 |
| Orders executed | **none** (simulación histórica) |

## Scenario A — Frictionless

| Métrica | Valor |
|---------|-------|
| Total signals | 639 |
| Trades opened | 533 |
| Trades rejected | 106 |
| Win rate % | 32.08 |
| Gross profit | 15094.0081 |
| Gross loss | -15980.4113 |
| Net PnL | -886.4032 |
| Profit Factor | 0.9445 |
| Expectancy / trade | -1.6630 |
| Average R | -0.0305 |
| Max DD USD | 1840.8235 |
| Max DD % | 17.3730 |
| Ending Equity | 9113.5968 |
| Avg holding bars | 6.47 |
| Ambiguous same-bar | 9 |
| Gap exits | 4 |

## Scenario B — Spread USD 0.40

| Métrica | Valor |
|---------|-------|
| Total signals | 639 |
| Trades opened | 531 |
| Trades rejected | 108 |
| Win rate % | 30.32 |
| Gross profit | 13292.0060 |
| Gross loss | -15161.5915 |
| Net PnL | -1869.5855 |
| Profit Factor | 0.8767 |
| Expectancy / trade | -3.5209 |
| Average R | -0.0837 |
| Max DD USD | 2516.2149 |
| Max DD % | 24.2300 |
| Ending Equity | 8130.4145 |
| Avg holding bars | 6.51 |
| Ambiguous same-bar | 7 |
| Gap exits | 4 |

## Comparación A vs B

| Métrica | A Frictionless | B Spread 0.40 | Δ (B−A) |
|---------|----------------|---------------|---------|
| Net PnL | -886.4032 | -1869.5855 | -983.1823 |
| Profit Factor | 0.9445 | 0.8767 | -0.0678 |
| Average R | -0.0305 | -0.0837 | -0.0532 |
| Max DD USD | 1840.8235 | 2516.2149 | 675.3914 |
| Max DD % | 17.3730 | 24.2300 | 6.8570 |
| Ending Equity | 9113.5968 | 8130.4145 | -983.1823 |
| Win Rate % | 32.0826 | 30.3202 | -1.7624 |
| Trades opened | 533.0000 | 531.0000 | -2.0000 |
| Ambiguous same-bar | 9.0000 | 7.0000 | -2.0000 |
| Gap exits | 4.0000 | 4.0000 | 0.0000 |

## Rejection reasons (A)

| Reason | Count |
|--------|-------|
| PositionAlreadyOpen | 99 |
| MinVolumeExceedsRiskBudget | 7 |

## Rejection reasons (B)

| Reason | Count |
|--------|-------|
| PositionAlreadyOpen | 99 |
| MinVolumeExceedsRiskBudget | 9 |

## Exit reasons (A)

| Reason | Count |
|--------|-------|
| StopLoss | 362 |
| TakeProfit | 171 |

## Exit reasons (B)

| Reason | Count |
|--------|-------|
| StopLoss | 370 |
| TakeProfit | 161 |

## Notas

- Scenario B **no** representa costes reales completos (comisión y slippage no modelados).
- OHLC tratado como mid/referencia; Bid/Ask sintéticos = mid ± spread/2.
- Si SL y TP en la misma vela: se asume SL primero (`AmbiguousSameBar`).
- No se calculó Sharpe Ratio en esta fase.
