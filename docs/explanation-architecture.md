# Why the architecture looks this way

Micro Cockpit centers on private Market Observations rather than transactions. Identity, Journal, Market Data, and Tool are separate because they have different ownership, retention, and trust boundaries, while Edge gives the browser one stable API.

Journal holds the complete observation closure so owner and Access Grant rules can be enforced atomically. Market Data stays separate because public completed-session evidence has a different lifecycle from personal interpretation. Tool snapshots stay separate because calculations are useful without a Journal record and must not imply portfolio truth.

PostgreSQL is shared to keep deployment small, but runtime roles enforce logical ownership. HTTP and published views are explicit boundary crossings. The migrator is the only DDL identity.

Agent Users are first-class owners, not hidden system intelligence. A named Agent can write its own view and read explicitly granted human records. The system itself remains neutral.

Deletion physically removes personal content. Incremental consumers receive content-free tombstones so synchronization does not create a second immutable copy of private prose.
