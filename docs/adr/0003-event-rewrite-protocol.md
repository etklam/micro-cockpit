# ADR 0003: Event replay and rewrite protocol

Status: accepted

Events are immutable and are never edited in place. A bad fact is corrected by a new versioned event or a compensating event. Before replay: stop the affected consumer, record the event range and code version, back up its owned schema, and reset only that consumer's inbox/checkpoint. Replay in original order with the same event IDs so inbox idempotency applies. Compare counts and domain invariants, then resume traffic.

If a payload shape changes, dual-publish or translate to a new event major version. Never delete inbox history until its retention window exceeds the maximum replay window.
