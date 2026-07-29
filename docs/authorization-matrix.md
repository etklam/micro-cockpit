# Authorization matrix

| Surface | Human | Agent User | Additional rule |
|---|---|---|---|
| auth and settings | own account | token exchange only | status/version checked |
| Observation reads | own records | own + granted records | `journal:read` or `agent:read` |
| Observation writes | own records | own records | `journal:write`; grants never authorize writes |
| Expectation review | owner only | owner only | no cross-owner overwrite |
| Watchlist and principles | own | denied through human UI | owner predicates |
| Agent User management | manager | denied | one active token |
| Access Grants | owner creates/revokes | reads only | closure filters enforced in Journal |
| Human / Agent comparison | read-only | denied as a UI surface | Agent column requires matching active grant; owners remain separate |
| agent full/incremental sync | denied | allowed | `agent:read`, grant scope, cursor rules |
| calculators | public | public | deterministic |
| presets and snapshots | own | denied through human UI | owner predicates |
| account export/deletion | own | denied | deletion confirmation required |
| market directory and bars | authenticated | `agent:read` where mapped | completed-session public-market data |

Cross-owner private resources are non-disclosing. Internal ingestion uses `Internal__ServiceKey`; browser clients never receive it. Edge authorization is defense in depth—services still validate ownership.
