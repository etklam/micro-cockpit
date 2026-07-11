# Operations

## Start and stop

Set non-default secrets before exposing the stack:

```sh
export POSTGRES_PASSWORD='replace-me'
export LOCAL_REGISTRATION_KEY='replace-me-too'
docker compose up -d --build
docker compose ps
```

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

The v0.1 Identity signing key is generated in memory on startup. Restarting
Identity invalidates existing 15-minute access tokens; users can refresh or
sign in again.
