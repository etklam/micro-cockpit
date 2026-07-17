# Run Micro Cockpit locally

You will start the complete production-like stack, create the first user, sign in, and verify the diary workflow. By the end, the frontend, Edge API, 13 domain services, migration runner, and PostgreSQL will be running with the same network boundaries used by deployment.

## What you need

- Docker with Docker Compose v2.
- `curl` and `jq` only if you choose the gated registration fallback.
- At least 16 GB of free memory is recommended when building the full stack locally.
- A clone of this repository on the `main` branch.

You do not need a local .NET or Node.js installation for this tutorial.

## Step 1: Create local secrets

Copy the environment template:

```sh
cp .env.example .env
```

Edit `.env` and replace every `change-me-*` value with an independently generated local value. The file contains the PostgreSQL administrator and migrator passwords, one password per stateful service, the registration key used when public signup is disabled, and the internal service key. Compose keeps `ALLOW_PUBLIC_REGISTRATION=true` by default for local development.

Confirm that Compose can resolve the configuration:

```sh
docker compose config --quiet
```

No output means the configuration is valid. Do not commit `.env`.

## Step 2: Start the stack and see the first result

Build and start every container:

```sh
docker compose up -d --build --wait --wait-timeout 300
docker compose ps
```

The database startup chain runs before applications:

```text
postgres -> db-role-bootstrap -> db-migrate -> db-role-finalize -> services
```

Verify the two public entry points:

```sh
curl -fsS http://localhost:5099/health/ready
curl -fsS http://localhost:8080/ >/dev/null
```

Open <http://localhost:8080>. The login screen is the first visible application result.

## Step 3: Register the first user

Compose enables public signup for local development. Open <http://localhost:8080/register>, enter your name, email, and a password of at least 12 characters. The browser creates the account and signs in automatically.

For a gated deployment, set `ALLOW_PUBLIC_REGISTRATION=false`, load the local registration key without printing it, choose a local password, and submit the request:

```sh
set -a
source .env
set +a
export TEST_EMAIL=owner@example.com
read -s TEST_PASSWORD
export TEST_PASSWORD

curl -fsS \
  -H "X-Registration-Key: $LOCAL_REGISTRATION_KEY" \
  -H 'Content-Type: application/json' \
  -d "$(jq -nc \
    --arg email "$TEST_EMAIL" \
    --arg password "$TEST_PASSWORD" \
    '{email:$email,password:$password,displayName:"Local Owner",timezone:"UTC",baseCurrency:"USD"}')" \
  http://localhost:5099/api/auth/register

unset TEST_PASSWORD LOCAL_REGISTRATION_KEY INTERNAL_SERVICE_KEY
```

A successful registration returns `201 Created`. A repeated registration for the same email may return `409 Conflict`. If you used the gated curl flow, sign in at <http://localhost:8080/login> with `owner@example.com` and the password you entered.

## Step 4: Exercise the diary loop

After sign-in:

1. Open **Today** and create a Quick Note.
2. Open **Diary**, then open the created diary by its `/diary/:diaryId` URL.
3. Add an optional transaction.
4. Expand **Decision review**, save a structured review, and return to the diary list.
5. Open the current `/calendar/YYYY/MM` route and confirm the diary activity appears on the matching day.

Quick Note, diary creation, and transaction creation use idempotency keys. Retrying the same browser mutation does not create a second logical record.

## Step 5: Inspect health and logs

Check container state:

```sh
docker compose ps
docker compose logs --tail=100 edge
```

Inspect non-sensitive migration state:

```sh
docker compose run --rm db-migrate status
```

The current catalog should show migrations through `0017` applied on a fresh database.

## Step 6: Stop safely

Stop containers while preserving local data:

```sh
docker compose down
```

The `trade-diary-data` and `identity-keys` volumes remain. The next `docker compose up -d` reuses them.

Only delete local data when that is your intent:

```sh
docker compose down -v
```

This removes the database and persisted Identity signing key. Existing sessions become invalid after a new signing key is created.

## Troubleshooting

### Compose reports a missing variable

Reopen `.env` and replace every template value. Compose uses required-variable expressions and fails before startup when a value is absent.

### An application container does not start

Inspect the one-shot database jobs first:

```sh
docker compose ps -a
docker compose logs db-role-bootstrap db-migrate db-role-finalize
```

Application startup waits for all three jobs. Do not delete an existing database merely to bypass a migration or baseline error. Follow [Database migrations](database-migrations.md).

### Registration is unavailable

If the browser signup page reports that registration is unavailable, confirm `ALLOW_PUBLIC_REGISTRATION=true` in `.env` and restart the Identity container. For gated registration, confirm that the `X-Registration-Key` value came from the same `.env` used to start Compose. Do not put the registration key in the JSON body.

### The frontend loads but API calls fail

Verify Edge readiness and inspect Edge logs:

```sh
curl -i http://localhost:5099/health/ready
docker compose logs --tail=200 edge
```

Use the returned `X-Correlation-ID` to match an Edge error with downstream logs.

## What you built

You now have a complete local Micro Cockpit stack with private backend networking, schema-isolated database roles, applied migrations, browser session restoration, and the diary-first workflow.

Next:

- [How to develop and verify changes](how-to-development.md)
- [System reference](reference-system.md)
- [API and data reference](reference-api-data.md)
- [Operations](operations.md)
