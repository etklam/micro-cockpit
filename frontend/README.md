# Micro Cockpit frontend

React 19 application built with TypeScript, Vite, React Router, and TanStack Query.

The dependency boundary is:

```text
Route page -> query/mutation hook -> feature API adapter -> generated Edge client
```

- `src/App.tsx` owns URL routing and the authenticated application shell.
- `src/auth/AuthProvider.tsx` owns restoring/authenticated/anonymous session state.
- `src/features/queries.ts` owns server-state keys, caching, and targeted invalidation.
- `src/features/api.ts` adapts screen operations to the generated client.
- `src/generated/edge.ts` is generated from `contracts/openapi/edge-api.openapi.json`; do not edit it manually.
- `src/test/` contains Vitest, Testing Library, user-event, and MSW tests.

The access token stays in memory. Session restoration uses the HttpOnly refresh cookie through Edge. Frontend code calls Edge only and contains no internal service URLs.

## Commands

```sh
npm ci
npm run dev
npm run lint
npm run build
npm run test
npm run api:generate
npm run api:verify
```

For integrated development, startup, architecture, routes, API behavior, and verification:

- [Getting started](../docs/tutorial-getting-started.md)
- [How to develop and verify changes](../docs/how-to-development.md)
- [System reference](../docs/reference-system.md)
- [API and data reference](../docs/reference-api-data.md)
