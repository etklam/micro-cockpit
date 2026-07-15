#!/usr/bin/env bash
set -euo pipefail

namespace="${K8S_NAMESPACE:-micro-cockpit}"
registry=""
image_tag=""

usage() {
  echo "Usage: $0 [--namespace NAME] --image-registry REGISTRY --image-tag COMMIT_SHA" >&2
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --namespace) namespace=${2:?missing namespace}; shift 2 ;;
    --image-registry) registry=${2:?missing image registry}; shift 2 ;;
    --image-tag) image_tag=${2:?missing image tag}; shift 2 ;;
    *) usage; exit 2 ;;
  esac
done

[[ "$namespace" =~ ^[a-z0-9]([-a-z0-9]*[a-z0-9])?$ ]] || { echo "Invalid namespace." >&2; exit 1; }
[[ "$registry" =~ ^[A-Za-z0-9._:/-]+$ ]] || { echo "Invalid image registry." >&2; exit 1; }
[[ "$image_tag" =~ ^[0-9a-f]{40}$ ]] || { echo "Image tag must be a full lowercase commit SHA." >&2; exit 1; }
[[ "$image_tag" != "latest" ]] || { echo "Mutable image tags are not allowed." >&2; exit 1; }

deployments=(identity journal performance discipline reminder stock-research market-data price-alert rotation partner content tool operations edge frontend)

overlay=$(mktemp -d "${TMPDIR:-/tmp}/micro-cockpit-release.XXXXXX")
cleanup() { rm -rf "$overlay"; }
trap cleanup EXIT HUP INT TERM
chmod 700 "$overlay"
rendered="$overlay/release.yaml"
kustomization="$overlay/kustomization.yaml"
: >"$rendered"
chmod 600 "$rendered"

{
  printf '%s\n' 'apiVersion: kustomize.config.k8s.io/v1beta1' 'kind: Kustomization'
  printf 'resources:\n- %s/k8s/06-services.yaml\n' "$PWD"
  printf 'commonAnnotations:\n  app.kubernetes.io/version: "%s"\n  micro-cockpit/deployed-sha: "%s"\n' "$image_tag" "$image_tag"
  printf '%s\n' 'images:'
  for deployment in "${deployments[@]}"; do
    printf -- '- name: git.913555.xyz/etklam/micro-cockpit/%s\n  newName: %s/%s\n  newTag: "%s"\n' \
      "$deployment" "${registry%/}" "$deployment" "$image_tag"
  done
} >"$kustomization"
chmod 600 "$kustomization"

kubectl kustomize "$overlay" --load-restrictor LoadRestrictionsNone >"$rendered"
if grep -q 'image: .*:latest' "$rendered"; then
  echo "Rendered release contains a mutable image tag." >&2
  exit 1
fi
[ "$(grep -c "image: .*:${image_tag}$" "$rendered")" -eq "${#deployments[@]}" ] || {
  echo "Rendered release does not contain one immutable image per application deployment." >&2
  exit 1
}
grep -q "micro-cockpit/deployed-sha: ${image_tag}" "$rendered" || { echo "Rendered release annotation is missing." >&2; exit 1; }

scripts/apply-k8s-manifests.sh --namespace "$namespace" --skip-services
kubectl apply --namespace "$namespace" -f "$rendered"

for deployment in "${deployments[@]}"; do
  kubectl rollout status deployment/"$deployment" -n "$namespace" --timeout=300s
done
kubectl wait --for=condition=Available deployment --all -n "$namespace" --timeout=300s

for deployment in "${deployments[@]}"; do
  expected="${registry%/}/${deployment}:${image_tag}"
  actual=$(kubectl get deployment "$deployment" -n "$namespace" -o jsonpath='{.spec.template.spec.containers[0].image}')
  version=$(kubectl get deployment "$deployment" -n "$namespace" -o jsonpath='{.metadata.annotations.app\.kubernetes\.io/version}')
  deployed_sha=$(kubectl get deployment "$deployment" -n "$namespace" -o jsonpath='{.metadata.annotations.micro-cockpit/deployed-sha}')
  [ "$actual" = "$expected" ] || { echo "Deployment image verification failed: $deployment" >&2; exit 1; }
  case "$actual" in *:latest) echo "Mutable image detected: $deployment" >&2; exit 1 ;; esac
  [ "$version" = "$image_tag" ] && [ "$deployed_sha" = "$image_tag" ] || {
    echo "Deployment annotation verification failed: $deployment" >&2
    exit 1
  }
done

for deployment in edge frontend identity; do
  available=$(kubectl get deployment "$deployment" -n "$namespace" -o jsonpath='{.status.conditions[?(@.type=="Available")].status}')
  [ "$available" = "True" ] || { echo "Required deployment is not Available: $deployment" >&2; exit 1; }
done

crashloops=$(kubectl get pods -n "$namespace" -o jsonpath='{range .items[*]}{range .status.containerStatuses[*]}{.state.waiting.reason}{"\n"}{end}{end}' | grep -c '^CrashLoopBackOff$' || true)
[ "$crashloops" -eq 0 ] || { echo "CrashLoopBackOff detected after deployment." >&2; exit 1; }

echo "Release ${image_tag} is available in namespace ${namespace}."
