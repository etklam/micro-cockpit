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

lock_dir="${MICRO_COCKPIT_LOCK_DIR:-/run/lock/micro-cockpit}"
[ ! -L "$lock_dir" ] || { echo "Kubernetes operation lock directory must not be a symbolic link." >&2; exit 1; }
[ -d "$lock_dir" ] || { echo "Kubernetes operation lock directory is not a directory: $lock_dir" >&2; exit 1; }
mode=$(stat -c '%a' "$lock_dir" 2>/dev/null || stat -f '%Lp' "$lock_dir")
mode=$((8#$mode))
[ $((mode & 2)) -eq 0 ] || { echo "Kubernetes operation lock directory must not be world-writable." >&2; exit 1; }

umask 0007
lock_path="$lock_dir/micro-cockpit-${namespace}.operation.lock"
[ ! -L "$lock_path" ] || { echo "Kubernetes operation lock file must not be a symbolic link." >&2; exit 1; }
exec 9>"$lock_path"
chmod 0660 "$lock_path"
if ! flock -w "$timeout" 9; then
  echo "Timed out waiting for the Kubernetes operation lock for namespace $namespace." >&2
  exit 1
fi

MICRO_COCKPIT_OPERATION_LOCK_NAMESPACE="$namespace" "$@"
