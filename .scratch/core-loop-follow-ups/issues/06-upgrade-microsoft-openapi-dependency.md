# Upgrade the vulnerable Microsoft.OpenApi dependency

Status: complete

Type: AFK

## What to build

Move every service and test project off the vulnerable Microsoft.OpenApi 2.0.0 dependency to a currently supported patched version that is compatible with the repository's runtime OpenAPI generation. Preserve the committed contracts unless the newer library requires an intentional, reviewed normalization change.

## Acceptance criteria

- [x] All direct and transitive Microsoft.OpenApi references resolve to a patched version not affected by GHSA-v5pm-xwqc-g5wc.
- [x] Package versions remain consistent across services, gateway, tooling, and test projects.
- [x] The full solution restores and builds without the current vulnerability warning.
- [x] Runtime generation still produces all service OpenAPI documents and the composed Edge document successfully.
- [x] Any output normalization difference is explained and reviewed; no endpoint, schema, security response, or authorization metadata is lost.
- [x] The generated frontend client remains current and compiles against the frontend application.
- [x] Full .NET tests, frontend tests/build, OpenAPI validation, authorization parity, and architecture verification pass.
- [x] The change contains no unrelated dependency upgrades.

## Blocked by

None - can start immediately.

## Comments

Completed by pinning Microsoft.OpenApi 2.7.5 only in the four OpenAPI-producing services. NuGet reports no vulnerable packages across all 14 projects.
