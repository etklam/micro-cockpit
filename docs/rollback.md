# Rollback runbook

1. Stop writes or route traffic away from the affected release; record image and migration versions.
2. Prefer rolling application code back while keeping backward-compatible migrations.
3. For a destructive/incompatible migration, take a fresh backup, restore the pre-migration backup into a new database, run readiness and smoke checks there, then switch connection secrets.
4. Never reverse a shared migration by hand in production. Use a reviewed forward repair or the tested restore path.
5. Verify `/health/ready`, `/version`, ownership isolation, event backlog, row counts, and a representative read/write flow. Record operator, timestamps, evidence, and follow-up.

Run `scripts/verify-backup-restore.sh` against a disposable database before relying on a backup. The script never drops or overwrites a database.
