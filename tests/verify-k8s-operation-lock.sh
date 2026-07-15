#!/usr/bin/env bash
set -euo pipefail

repo=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
helper="$repo/scripts/with-k8s-operation-lock.sh"
tmp=$(mktemp -d "${TMPDIR:-/tmp}/k8s-operation-lock-test.XXXXXX")
cleanup() { rm -rf "$tmp"; }
trap cleanup EXIT HUP INT TERM
mkdir "$tmp/bin" "$tmp/locks"
chmod 0770 "$tmp/locks"
lock_env="MICRO_COCKPIT_LOCK_DIR=$tmp/locks"

cat >"$tmp/bin/flock" <<'EOF'
#!/usr/bin/env python3
import fcntl
import sys
import time

timeout = float(sys.argv[2])
fd = int(sys.argv[3])
deadline = time.monotonic() + timeout
while True:
    try:
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        raise SystemExit(0)
    except BlockingIOError:
        if time.monotonic() >= deadline:
            raise SystemExit(1)
        time.sleep(0.01)
EOF
chmod 700 "$tmp/bin/flock"

namespace="lock-test-$$"
other_namespace="lock-other-$$"

env $lock_env PATH="$tmp/bin:$PATH" "$helper" --namespace "$namespace" --timeout 2 -- sh -c 'printf entered >"$1"; while [ ! -f "$2" ]; do sleep 0.05; done' sh "$tmp/entered" "$tmp/release" &
holder=$!
for _ in $(seq 1 100); do [ -f "$tmp/entered" ] && break; sleep 0.02; done
[ -f "$tmp/entered" ]

if env $lock_env PATH="$tmp/bin:$PATH" "$helper" --namespace "$namespace" --timeout 0 -- sh -c 'printf raced' >"$tmp/race" 2>/dev/null; then
  echo "A second operation entered the same namespace lock." >&2
  exit 1
fi
[ ! -s "$tmp/race" ]

env $lock_env PATH="$tmp/bin:$PATH" "$helper" --namespace "$other_namespace" --timeout 0 -- sh -c 'printf independent' >"$tmp/other"
[ "$(cat "$tmp/other")" = independent ]

touch "$tmp/release"
wait "$holder"

rc=0
env $lock_env PATH="$tmp/bin:$PATH" "$helper" --namespace "$namespace" --timeout 1 -- sh -c 'exit 23' || rc=$?
[ "$rc" -eq 23 ]
mode=$(stat -c '%a' "$tmp/locks/micro-cockpit-${namespace}.operation.lock" 2>/dev/null || stat -f '%Lp' "$tmp/locks/micro-cockpit-${namespace}.operation.lock")
[ "$mode" = 660 ]

mkdir "$tmp/world"
chmod 777 "$tmp/world"
if MICRO_COCKPIT_LOCK_DIR="$tmp/world" PATH="$tmp/bin:$PATH" "$helper" --namespace "$namespace" --timeout 0 -- true >/dev/null 2>&1; then
  echo "World-writable lock directory was accepted." >&2; exit 1
fi
ln -s "$tmp/locks" "$tmp/link"
if MICRO_COCKPIT_LOCK_DIR="$tmp/link" PATH="$tmp/bin:$PATH" "$helper" --namespace "$namespace" --timeout 0 -- true >/dev/null 2>&1; then
  echo "Symbolic-link lock directory was accepted." >&2; exit 1
fi
if MICRO_COCKPIT_LOCK_DIR="$tmp/missing" PATH="$tmp/bin:$PATH" "$helper" --namespace "$namespace" --timeout 0 -- true >/dev/null 2>&1; then
  echo "Missing lock directory was accepted." >&2; exit 1
fi
! grep -Eq '(^|[[:space:]])eval([[:space:]]|$)' "$helper"

echo "Shared Kubernetes operation lock tests passed."
