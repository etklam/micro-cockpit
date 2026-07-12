# Micro Cockpit

Diary-first trade journal. The current build targets the v0.1 boundary in `development-plan.md`.

## Run the production-like stack

All required secrets live in `.env` (git-ignored). Copy the template and edit every value
before exposing the stack — Compose fails fast if any of the 16 variables is unset.

```sh
cp .env.example .env      # then edit .env and replace every change-me-* value
docker compose up -d --build
docker compose ps
```

Open <http://localhost:8080>. The Edge API is at <http://localhost:5099>; PostgreSQL is bound
to localhost on `5433`. Backend services have no host ports — only Frontend and Edge are public.

Register the first user by sending `X-Registration-Key: <LOCAL_REGISTRATION_KEY>` to
`POST /api/auth/register`, then sign in through the UI.

```sh
docker compose logs -f edge
docker compose down        # stops containers, keeps the data volume
```

## Operations

Backup, restore, reset, and signing-key handling (full commands in [docs/operations.md](docs/operations.md)):

- **Backup** — `pg_dump -Fc` of the `trade_diary` database (all schemas) to a file.
- **Restore** — `pg_restore --clean --if-exists` into the target database; verify application tables.
- **Destructive reset** — `docker compose down -v` removes the data volume; the next `up` re-runs migrations on an empty database.
- **Signing key backup** — Identity's RSA private key lives in the `identity-keys` volume (`/keys/signing-key.pem`). Back it up; losing it invalidates all outstanding refresh tokens and forces re-login. The JWT `kid` is derived from the public key, so it is stable across restarts and only changes on key rotation.

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
