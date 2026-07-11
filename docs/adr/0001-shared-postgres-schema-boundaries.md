# ADR 0001: Shared PostgreSQL, isolated schemas

Status: accepted

Each service owns one database schema (or is explicitly stateless) while deployments share one PostgreSQL cluster. This keeps local operation and backup simple without making tables shared. Service roles receive DML only on owned schemas. Cross-schema writes and private-table reads are forbidden; HTTP, events, or versioned `_public` views cross boundaries.

Move a service to its own database when independent scaling, compliance, availability, or restore requirements justify the operational cost. The contracts already avoid cross-schema transactions, so that move remains mechanical.
