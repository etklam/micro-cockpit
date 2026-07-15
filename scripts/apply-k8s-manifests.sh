#!/bin/sh
set -eu

namespace="${K8S_NAMESPACE:-micro-cockpit}"
if [ "${1:-}" = "--namespace" ]; then
  namespace=${2:?missing namespace}
  shift 2
fi
[ "$#" -eq 0 ] || { echo "Usage: $0 [--namespace NAME]" >&2; exit 2; }

kubectl apply -f k8s/00-namespace.yaml
for manifest in \
  k8s/02a-postgres-pvc.yaml \
  k8s/02b-identity-keys-pvc.yaml \
  k8s/02c-postgres-init-configmap.yaml \
  k8s/03-postgres-deployment.yaml \
  k8s/04-postgres-service.yaml \
  k8s/05-db-init-job.yaml \
  k8s/06-services.yaml \
  k8s/07-ingress.yaml
do
  kubectl apply --namespace "$namespace" -f "$manifest"
done
