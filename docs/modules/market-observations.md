# Market Observations

A Market Observation is the private root record for one User and Journal Day. Observation Updates preserve how that owner’s view changed over time.

An update may contain a Signal, Interpretation, Mental State, tags, subjects, and external evidence. Instrument subjects use stable Instrument identity; completed-session raw and adjusted Daily Close values are attached as evidence when available.

Expectations are testable children with a deadline, invalidation condition, confidence, and market. An Expectation Review belongs to the same owner and separates Outcome from reasoning quality. Action Decisions and Trades are optional evidence, not a portfolio model.

Access Grants expose a read-only closure of selected observations to an Agent User. Full sync returns a cursor; incremental sync returns changed content or content-free deletion tombstones. Grant filtering is enforced in Journal and cannot be widened by Edge or the frontend.

The comparison read model keeps Human and Agent-owned Observation Updates and Expectations in separate owner-labeled collections. Agent data is returned only where an active Access Grant covers the subject and day. It reports objective latest Outcome consistency and confidence difference; it never merges the records or creates a verdict.

Pattern Review aggregates owner-selected reasoning labels with counts, denominators, and evidence links. The system does not infer a verdict.
