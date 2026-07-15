#!/usr/bin/env bash
set -euo pipefail

repo=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
helper="$repo/scripts/with-k8s-operation-lock.sh"
tmp=$(mktemp -d "${TMPDIR:-/tmp}/k8s-operation-lock-test.XXXXXX")
cleanup() { rm -rf "$tmp"; }
trap cleanup EXIT HUP INT TERM
mkdir "$tmp/bin"

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

PATH="$tmp/bin:$PATH" "$helper" --namespace "$namespace" --timeout 2 -- sh -c 'printf entered >"$1"; while [ ! -f "$2" ]; do sleep 0.05; done' sh "$tmp/entered" "$tmp/release" &
holder=$!
for _ in $(seq 1 100); do [ -f "$tmp/entered" ] && break; sleep 0.02; done
[ -f "$tmp/entered" ]

if PATH="$tmp/bin:$PATH" "$helper" --namespace "$namespace" --timeout 0 -- sh -c 'printf raced' >"$tmp/race" 2>/dev/null; then
  echo "A second operation entered the same namespace lock." >&2
  exit 1
fi
[ ! -s "$tmp/race" ]

PATH="$tmp/bin:$PATH" "$helper" --namespace "$other_namespace" --timeout 0 -- sh -c 'printf independent' >"$tmp/other"
[ "$(cat "$tmp/other")" = independent ]

touch "$tmp/release"
wait "$holder"

rc=0
PATH="$tmp/bin:$PATH" "$helper" --namespace "$namespace" --timeout 1 -- sh -c 'exit 23' || rc=$?
[ "$rc" -eq 23 ]
! grep -Eq '(^|[[:space:]])eval([[:space:]]|$)' "$helper"

echo "Shared Kubernetes operation lock tests passed."
