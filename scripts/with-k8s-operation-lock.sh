#!/usr/bin/env bash
set -euo pipefail

namespace=""
timeout=""

usage() {
  echo "Usage: $0 --namespace NAME --timeout SECONDS -- COMMAND [ARG...]" >&2
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --namespace) namespace=${2:?missing namespace}; shift 2 ;;
    --timeout) timeout=${2:?missing timeout}; shift 2 ;;
    --) shift; break ;;
    *) usage; exit 2 ;;
  esac
done

[[ "$namespace" =~ ^[a-z0-9]([-a-z0-9]*[a-z0-9])?$ ]] || { echo "Invalid namespace." >&2; exit 1; }
[[ "$timeout" =~ ^[0-9]+$ ]] || { echo "Lock timeout must be a non-negative integer." >&2; exit 1; }
[ "$#" -gt 0 ] || { usage; exit 2; }
command -v flock >/dev/null 2>&1 || { echo "flock is required." >&2; exit 1; }

lock_path="/tmp/micro-cockpit-${namespace}.operation.lock"
exec 9>"$lock_path"
if ! flock -w "$timeout" 9; then
  echo "Timed out waiting for the Kubernetes operation lock for namespace $namespace." >&2
  exit 1
fi

MICRO_COCKPIT_OPERATION_LOCK_NAMESPACE="$namespace" "$@"
