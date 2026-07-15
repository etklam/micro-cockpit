#!/usr/bin/env bash
# Shared immutable-attempt lifecycle for Kubernetes database Jobs.

database_job_attempt_number() {
  local name=$1 base=$2
  if [ "$name" = "$base" ]; then echo 1; else echo "${name##*-a}"; fi
}

database_job_list() {
  local base=$1
  kubectl get jobs -n "$namespace" -o jsonpath='{range .items[*]}{.metadata.name}{"\n"}{end}' 2>/dev/null \
    | grep -E "^${base}(-a[2-9][0-9]*)?$" | sort -V || true
}

database_job_render() {
  local name=$1 step=$2 backup=${3:-}
  python3 "$script_dir/k8s-database-job.py" render --name "$name" --namespace "$namespace" \
    --step "$step" --image "$image" --release-sha "$image_tag" --backup "$backup" >"$tmp/$name.json"
  chmod 600 "$tmp/$name.json"
}

database_job_verify() {
  local name=$1 step=$2 backup=${3:-}
  kubectl get job "$name" -n "$namespace" -o json >"$tmp/existing-$name.json"
  python3 "$script_dir/k8s-database-job.py" verify --file "$tmp/existing-$name.json" \
    --step "$step" --image "$image" --release-sha "$image_tag" --backup "$backup" >/dev/null
}

database_job_condition() {
  kubectl get job "$1" -n "$namespace" -o "jsonpath={.status.conditions[?(@.type=='$2')].status}"
}

database_job_wait() {
  local name=$1
  if ! kubectl wait --for=condition=Complete "job/$name" -n "$namespace" --timeout=600s; then
    kubectl get job "$name" -n "$namespace" -o wide >&2 || true
    kubectl get pods -n "$namespace" -l "job-name=$name" -o wide >&2 || true
    echo "Database Job did not complete successfully: $name" >&2
    return 1
  fi
}

database_job_create() {
  local name=$1 step=$2 backup=${3:-}
  database_job_render "$name" "$step" "$backup"
  if ! kubectl create -f "$tmp/$name.json" >/dev/null; then
    echo "Database Job attempt creation conflicted; inspect the existing immutable attempt: $name" >&2
    return 1
  fi
  database_job_wait "$name"
}

database_job_run() {
  local base=$1 step=$2 retry=$3 backup=${4:-}
  local names name complete failed running="" completed="" max_attempt=0 attempt
  names=$(database_job_list "$base")
  if [ -z "$names" ]; then
    database_job_create "$base" "$step" "$backup"
    return
  fi
  while IFS= read -r name; do
    [ -n "$name" ] || continue
    database_job_verify "$name" "$step" "$backup" || { echo "Existing database Job does not match the requested release: $name" >&2; return 1; }
    attempt=$(database_job_attempt_number "$name" "$base")
    [ "$attempt" -gt "$max_attempt" ] && max_attempt=$attempt
    complete=$(database_job_condition "$name" Complete)
    failed=$(database_job_condition "$name" Failed)
    if [ "$failed" = True ]; then
      :
    elif [ "$complete" = True ]; then
      completed=$name
    else
      running=$name
    fi
  done <<<"$names"
  if [ -n "$completed" ]; then
    echo "Database Job already completed with verified specification: $completed"
    return
  fi
  if [ -n "$running" ]; then
    database_job_wait "$running"
    return
  fi
  if [ "$retry" != true ]; then
    echo "Database Job previously failed and was preserved; use the explicit retry option: $base" >&2
    return 1
  fi
  database_job_create "${base}-a$((max_attempt + 1))" "$step" "$backup"
}
