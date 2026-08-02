# 03 — Review guided comparison builder and Agent setup state

Status: complete

## What to build

Turn the Review comparison setup into a compact, guided three-step builder: select comparison target, select comparison period, and select an AI Agent. Use user-facing terms such as comparison target, target type, comparison period, and AI Agent. Add Last 7 days, Last 30 days, and Custom period presets while retaining custom start/end controls. When no Agent exists, show a setup card explaining its role and linking to Agent creation; keep the rest of Review usable and preserve a return path.

## Acceptance criteria

- [ ] The Review page explains Human × AI comparison before presenting controls, and the builder exposes target type, searchable/keyboard-usable target selection, period preset/custom dates, Agent selection, and one primary “Create comparison” action.
- [ ] The primary action stays disabled until the selected target, Agent, and valid date range are complete; custom ranges reject start dates after end dates and preserve values on validation errors.
- [ ] Presets populate a valid range compatible with the existing comparison endpoint and switching to Custom keeps custom controls available.
- [ ] A no-Agent response renders an explanatory setup card with an accessible “Create AI Agent” action instead of a broken-looking disabled select; previous Review content remains viewable.
- [ ] Pre-submit space is a compact output explanation or existing review content, not a large empty result container; loading, API error, retry, and no-data states remain distinct.
- [ ] Existing Expectation Review, Pattern Review, and Discipline functionality remains operational, with tests for no-Agent, presets, invalid/valid ranges, loading, error, and mobile layout.

## Blocked by

None - can start immediately.
