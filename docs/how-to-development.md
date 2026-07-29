# Development and verification

## Backend

```bash
dotnet restore TradeDiary.slnx
dotnet build TradeDiary.slnx --no-restore -m:1 --disable-build-servers
dotnet test TradeDiary.slnx --no-build -m:1 --disable-build-servers
```

Add Journal endpoints to the focused endpoint module named by `Program.cs`. Map browser routes through the matching Edge module, add authorization tests, regenerate OpenAPI, and update the generated client. Persistence changes require a new immutable migration and manifest checksum.

## Frontend

```bash
npm --prefix frontend ci
npm --prefix frontend run lint
npm --prefix frontend run build
npm --prefix frontend test -- --run
```

Use typed message keys in both locales. Put remote state in query hooks, form state in the page, and deterministic calculations in pure modules. Core changes need phone layout, keyboard behavior, empty/error states, and non-color labels.

## Contracts and architecture

```bash
npm --prefix frontend run api:generate
npm --prefix frontend run api:verify
python3 scripts/validate-openapi.py
python3 scripts/validate-migrations.py
./tests/verify-architecture.sh
```

## Full stack

The manual E2E workflow creates ephemeral secrets, starts Compose, registers a User, and runs auth, Market Observation idempotency, JWT ownership, the complete release smoke, and PostgreSQL role isolation.

Never run the release smoke against a valued database: it creates and removes fixture market data and deletes its test Market Observation.
