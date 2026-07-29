# Tools

Four retained deterministic calculators are available without authentication:

- Position Size
- Risk / Reward
- Average Cost
- Profit / Loss

Validation and calculation are pure frontend logic. Authenticated Users can save named presets and versioned Calculation Snapshots. Tool recalculates a saved snapshot from its inputs; the client does not submit authoritative output.

Average Cost and Profit/Loss are standalone. Inputs are manual and do not imply holdings, lots, brokerage state, or current portfolio value. Tools do not create Trade Drafts, Diary Drafts, Action Decisions, or recommendations.

`features/toolsCalc.ts` owns calculation rules, `features/toolsPersistence.ts` owns preset/snapshot transport, and `screens/tools/ToolsPage.tsx` owns the UI.
