# Developer documentation

This index separates system explanation from operational procedures. Start with the architecture overview, then follow the module or flow that matches the change you plan to make.

## Architecture

- [System overview](architecture/overview.md)
- [Frontend architecture](architecture/frontend.md)
- [Backend architecture](architecture/backend.md)
- [Data ownership and cross-service flow](architecture/data-flow.md)

## Modules

- [Tools](modules/tools.md)
- [Diary, transactions, and reviews](modules/diary-trades.md)
- [Authentication, theme, shared UI, and API state](modules/platform-frontend.md)
- [Backend service catalog](modules/backend-services.md)

## References

- [Browser-facing application API](api/application-api.md)
- [Database and domain schema](database/schema.md)
- [Core flow diagrams](flows/core-flows.md)
- [Existing API and data conventions](reference-api-data.md)
- [System and configuration reference](reference-system.md)

## Development and operations

- [Getting started](tutorial-getting-started.md)
- [Development and verification](how-to-development.md)
- [Database migrations](database-migrations.md)
- [Operations](operations.md)
- [K3s deployment](deploy-k3s.md)
- [Rollback](rollback.md)
- [Authorization matrix](authorization-matrix.md)
- [Security review checklist](security-review-checklist.md)

## Design decisions

- [Shared PostgreSQL schema boundaries](adr/0001-shared-postgres-schema-boundaries.md)
- [No cross-schema writes](adr/0002-no-cross-schema-writes.md)
- [Event rewrite protocol](adr/0003-event-rewrite-protocol.md)
- [Migration runner decision](decisions/ADR-database-migration-runner.md)
- [Frontend internationalization](decisions/ADR-frontend-i18n.md)

## Contracts and service-local notes

- [Service catalog](../SERVICE_CATALOG.md), with links to each `services/*/SERVICE.md`
- [API conventions](../contracts/api-conventions.md)
- [Event envelope](../contracts/event-envelope.md)
- [Published database views](../contracts/published-views.md)
- [Frontend package notes](../frontend/README.md)

## Security and incident history

- [Git history remediation](security/git-history-remediation.md)
- [Secret incident record](security/secret-incident.md)

## Planning and agent conventions

- [Product definition](../PRODUCT.md)
- [Design direction](../DESIGN.md)
- [Completion audit](completion-audit.md)
- [Original development plan](../development-plan.md)
- [Follow-up development plan](../development-plan-2.md)
- [Agent domain conventions](agents/domain.md)
- [Issue tracker conventions](agents/issue-tracker.md)
- [Triage labels](agents/triage-labels.md)
- [Repository agent instructions](../CLAUDE.md)

## Documentation maintenance rules

1. Link to source entry points instead of copying implementation bodies.
2. Update the relevant module flow when a route, endpoint, table, or ownership boundary changes.
3. Treat generated OpenAPI as the request/response source of truth; the markdown API reference explains intent and important failure behavior.
4. Record missing domains explicitly. Do not document planned portfolio or brokerage behavior as current behavior.
5. Keep Mermaid diagrams small enough to review in a pull request.
