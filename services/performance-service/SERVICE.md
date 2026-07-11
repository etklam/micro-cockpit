# Performance Service

Owns `performance.*`: one manually entered P/L record per user and local date.

- Missing P/L stays absent; it is never coerced to zero.
- Percentage is returned only when a positive capital base exists.
- No portfolio, holdings, brokerage, or cost-basis behaviour.
