# Operations

## Start and stop

Set non-default secrets before exposing the stack:

```sh
export POSTGRES_PASSWORD='replace-me'
export MIGRATOR_DB_PASSWORD='replace-me-migrator'
export IDENTITY_DB_PASSWORD='replace-me-identity'
export JOURNAL_DB_PASSWORD='replace-me-journal'
export PERFORMANCE_DB_PASSWORD='replace-me-performance'
export DISCIPLINE_DB_PASSWORD='replace-me-discipline'
export REMINDER_DB_PASSWORD='replace-me-reminder'
export MARKET_DATA_DB_PASSWORD='replace-me-market-data'
export PRICE_ALERT_DB_PASSWORD='replace-me-price-alert'
export ROTATION_DB_PASSWORD='replace-me-rotation'
export STOCK_RESEARCH_DB_PASSWORD='replace-me-stock-research'
export PARTNER_DB_PASSWORD='replace-me-partner'
export CONTENT_DB_PASSWORD='replace-me-content'
export OPERATIONS_DB_PASSWORD='replace-me-operations'
export LOCAL_REGISTRATION_KEY='replace-me-too'
docker compose up -d --build
docker compose ps
```

Use independently generated values from the deployment secret store. Compose
has no database-password defaults: missing secrets fail configuration instead
of silently starting with production credentials from source control.

`db-init` runs once after PostgreSQL becomes healthy. It keeps schema/object
ownership on `trade_diary_migrator` and grants each LOGIN runtime role only
`SELECT`, `INSERT`, `UPDATE`, and `DELETE` on its own schema. Price Alert and
Rotation receive `SELECT` only on published market views. Re-run it after a
migration to apply grants to existing objects:

```sh
docker compose run --rm db-init
./tests/postgres-role-isolation.sh
```

Apply later SQL migrations as `trade_diary_migrator`, not as a runtime role,
then run `db-init` again. The bootstrap administrator is reserved for role and
ownership administration.

`frontend` is served at `http://localhost:8080` and proxies `/api` to Edge.
Edge is also bound at `http://localhost:5099`. PostgreSQL is reachable only
from the host at `127.0.0.1:5433`; the other services stay on the private
Docker network.

```sh
docker compose logs -f edge
docker compose down
```

Use `docker compose down -v` only when intentionally deleting all database
data.

## Backup

The custom-format dump includes every service schema:

```sh
docker compose exec -T postgres \
  pg_dump -U trade_diary -d trade_diary -Fc > trade-diary.dump
```

## Restore

Stop application writes first. Restore replaces matching database objects:

```sh
docker compose stop frontend edge reminder discipline performance journal identity
docker compose exec -T postgres \
  pg_restore -U trade_diary -d trade_diary --clean --if-exists < trade-diary.dump
docker compose start identity journal performance discipline reminder edge frontend
```

## Upgrade

```sh
docker compose build --pull
docker compose up -d
```

Identity stores its RSA signing key in the `identity-keys` volume at
`/keys/signing-key.pem`, so ordinary container replacement does not invalidate
access tokens. Back up that volume and restrict access to it like any other
authentication secret.
