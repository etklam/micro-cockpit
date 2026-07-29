# Getting started

1. Copy `.env.example` to an ignored `.env` and fill every required value.
2. Run `docker compose up -d --build --wait`.
3. Open `http://localhost:8080`.
4. Register when enabled, or use the configured registration key.
5. On Today, save a Quick Observation and enrich it with a Signal, Interpretation, and subject.
6. Add an Expectation. When its horizon has elapsed, review Outcome and reasoning quality under Review.
7. Add an Instrument to Watchlist and inspect the Calendar.
8. Use Tools anonymously or save a preset and Calculation Snapshot when authenticated.
9. In Settings, provision an Agent User and create a read-only Access Grant when needed.

Run the checks from the root README before changing code. `docker compose down` stops the stack; add `-v` only when you explicitly intend to delete local database and key volumes.
