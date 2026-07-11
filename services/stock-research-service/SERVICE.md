# Stock Research Service

Owns `stock_research.*` and keeps stock research separate from ETF rotation.

- `stocks` is the shared stock-only directory; symbols are normalized uppercase.
- Watchlists, current notes, and timeline records belong to the authenticated JWT `sub`.
- The current Stock Note is mutable through one upsert endpoint.
- Timeline evidence is append-only. A correction is a new record linked by `correctionOfId`; existing records have no update or delete API.
- Cross-user resources return 404.
