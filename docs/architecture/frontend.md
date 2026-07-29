# Frontend architecture

The React 19 frontend uses React Router, TanStack Query, typed i18n, and a generated Edge client.

```text
Route page
  → query or mutation hook
    → feature API adapter
      → generated Edge client
        → Edge
```

Authenticated routes are `/today`, `/today/observations`, `/review`, `/watchlist`, `/calendar/:year/:month`, `/tools`, and `/settings`. `/`, `/login`, `/register`, and `/tools` have public behavior where appropriate. Retired routes render not found.

`frontend/src/features/queries.ts` owns cache keys and invalidation. `features/api.ts` gives domain names to generated operations. `frontend/src/api.ts` keeps the access token in memory and performs one refresh retry. Locale and appearance mirrors may use local storage; credentials may not.

Quick Observation is the lowest-friction write. Tools are independent calculators; authenticated persistence stores presets and Calculation Snapshots, not Diary or Trade drafts.

Core flows must work on narrow phones, through a keyboard, without color-only meaning, and in both English and Traditional Chinese.
