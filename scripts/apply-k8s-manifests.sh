#!/bin/sh
set -eu

namespace="${K8S_NAMESPACE:-micro-cockpit}"
skip_services=false
while [ "$#" -gt 0 ]; do
  case "$1" in
    --namespace) namespace=${2:?missing namespace}; shift 2 ;;
    --skip-services) skip_services=true; shift ;;
    *) echo "Usage: $0 [--namespace NAME] [--skip-services]" >&2; exit 2 ;;
  esac
done

if [ "$skip_services" = false ]; then
  echo "Application manifests require an immutable release tag; use scripts/deploy-k8s-release.sh." >&2
  exit 1
fi

kubectl apply -f k8s/00-namespace.yaml
for manifest in \
  k8s/02a-postgres-pvc.yaml \
  k8s/02b-identity-keys-pvc.yaml \
  k8s/03-postgres-deployment.yaml \
  k8s/04-postgres-service.yaml \
  k8s/07-ingress.yaml
do
  kubectl apply --namespace "$namespace" -f "$manifest"
done
