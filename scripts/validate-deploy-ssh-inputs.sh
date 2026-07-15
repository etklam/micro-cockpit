#!/usr/bin/env bash
set -euo pipefail

for name in DEPLOY_HOST DEPLOY_USER DEPLOY_SSH_KEY DEPLOY_KNOWN_HOSTS; do
  value=${!name:-}
  [ -n "${value//[[:space:]]/}" ] || { echo "Missing required deployment input: $name" >&2; exit 1; }
done
[[ "$DEPLOY_HOST" =~ ^[A-Za-z0-9._:-]+$ ]] || { echo "Invalid deployment host." >&2; exit 1; }
[[ "$DEPLOY_USER" =~ ^[A-Za-z_][A-Za-z0-9._-]*$ ]] || { echo "Invalid deployment user." >&2; exit 1; }

echo "Deployment SSH inputs are present and structurally valid."
