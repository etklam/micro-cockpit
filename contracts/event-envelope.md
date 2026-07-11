# Event envelope

Events are immutable facts encoded with [`event-envelope.schema.json`](events/event-envelope.schema.json). Producers use a transactional outbox; consumers use an inbox keyed by `eventId`. Event names and payloads are versioned (`ThingHappened.v1`). Unknown fields are tolerated, but a consumer must reject an unsupported event major version without partially applying it.

`occurredAt` is producer time in UTC. `correlationId` traces the initiating request; `causationId` links a derived event to its input. Personally sensitive data belongs in the owning service and should be referenced by ID, not copied into an event.
