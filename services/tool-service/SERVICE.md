# Tool Service

Authenticated calculator endpoints mirror the public client-side position sizing, risk/reward, average cost, and profit/loss formulas. The service owns `tool.*` for user-scoped presets and saved calculation snapshots. Saved outputs are recalculated from strict schema-v1 inputs; frontend result values are never trusted. It still owns no portfolio accounting.
