# Micro Cockpit

Diary-first trade journal. The current build targets the v0.1 boundary in `development-plan.md`.

## Run the production stack

```sh
POSTGRES_PASSWORD='replace-me' \
LOCAL_REGISTRATION_KEY='replace-me-too' \
docker compose up -d --build
```

Open <http://localhost:8080>. The Edge API is also available at
<http://localhost:5099>; PostgreSQL is bound to localhost on port `5433`.
Identity, Journal, Performance, Discipline, and Reminder have no host ports.

```sh
docker compose ps
docker compose logs -f edge
docker compose down
```

Operational backup and restore instructions are in [docs/operations.md](docs/operations.md).

## Run for development

```sh
docker compose up -d postgres
dotnet build TradeDiary.slnx
cd frontend && npm install && npm run dev
dotnet run --project gateway/TradeDiary.EdgeApi
```

Journal and Performance default to the local Compose PostgreSQL connection. Internal APIs accept only Identity-issued Bearer JWTs; user ID headers are not trusted.

The frontend is dark-first and responsive. The gateway exposes `/health/live`, `/health/ready`, `/version`, and the initial `/api/app/dashboard` contract.

## Boundaries

- The frontend calls only the Edge API.
- Each backend capability is an independently buildable service.
- No holdings, cost-basis, brokerage, or portfolio accounting engine.
- Compose keeps backend traffic on a private Docker network; only Frontend and
  Edge are publicly bound.
