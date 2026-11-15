# ExecutionPlan — ejemplos (fixtures de test)

## BUY elegible (prep DEMO)

- Signal ConfirmationClose = 2650 (teórico; no usado como entry)
- Ask = 2650.2 / Bid = 2650.0 / Spread = 0.2 (20 pips)
- SL estructural = 2639
- TP = 2650.2 + 2 × (2650.2 − 2639) = 2672.4
- Volumen recalculado desde Ask (≠ RiskPlan volume)
- `IsEligible = true`, `RejectionReason = OrdersDisabled`

## SELL elegible (prep DEMO)

- Bid = 2629.9 como entry
- SL estructural = 2641
- TP = Bid − 2R
- `OrdersDisabled`

## Rechazos

| Caso | Reason |
|------|--------|
| `Account.IsLive` | `LiveAccountForbidden` |
| Spread 100 pips | `InvalidOrExcessiveSpread` |
| Ask &lt; Bid / Bid=0 | `InvalidQuote` |
| MinStopLossDistance enorme | `SymbolConstraintsViolated` |
| Vol mín &gt; budget 0.5% | `MinVolumeExceedsRiskBudget` |
| Edad &gt; 2 barras | `SignalExpired` |
| Label del bot abierta | `MaxOpenPositionsReached` |
| SignalId ya planificado | `SignalDuplicate` |

## SymbolSpec DEMO

Requiere cBot en cTrader. El host imprime `SymbolSpecSnapshot.FormatSafeLogLine()` sin AccountNumber.
