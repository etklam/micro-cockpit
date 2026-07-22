# Core flow diagrams

This page collects cross-cutting flows that span modules. Feature-specific diagrams live beside their module documentation.

## Authentication and session restoration

```mermaid
sequenceDiagram
    participant Browser
    participant Auth as AuthProvider
    participant Client as frontend/api.ts
    participant Edge
    participant Identity
    Browser->>Auth: application mounts
    Auth->>Client: restoreSession()
    Client->>Edge: POST /api/auth/refresh with td_refresh cookie
    Edge->>Identity: rotate refresh token
    alt valid token family
        Identity-->>Edge: access + new refresh token
        Edge-->>Client: access token; replace HttpOnly cookie
        Client-->>Auth: authenticated
    else invalid, expired, or reused
        Identity-->>Edge: 401
        Edge-->>Client: clear cookie, 401
        Client-->>Auth: anonymous
        Auth->>Auth: clear protected query cache
    end
```

The access token remains in JavaScript module memory. Only Edge can read or write the refresh cookie.

## Page route to data fetch

```mermaid
flowchart LR
    URL[Browser URL] --> Router[React Router]
    Router --> Guard{Route type}
    Guard -->|public| Page[Page component]
    Guard -->|protected and authenticated| Shell[AppShell]
    Guard -->|protected and anonymous| Login[Login redirect]
    Shell --> Page
    Page --> Query[useQuery hook]
    Query --> Generated[Generated Edge client]
    Generated --> Edge[Edge route]
    Edge --> Service[Owning service or composition]
    Service --> DB[(Owned schema)]
```

## Form submission to persistence

```mermaid
sequenceDiagram
    participant User
    participant Form
    participant Mutation
    participant Edge
    participant Service
    participant DB
    User->>Form: edit fields
    Form->>Form: local validation
    User->>Form: explicit submit
    Form->>Mutation: typed write model
    Mutation->>Edge: POST/PUT/DELETE
    Edge->>Service: JWT, body, correlation ID
    Service->>Service: domain validation and ownership
    Service->>DB: parameterized SQL
    DB-->>Service: stored row or constraint error
    Service-->>Mutation: DTO or ProblemDetails
    Mutation->>Mutation: invalidate dependent query keys
    Mutation-->>Form: confirmed data or translated error
```

The frontend never treats optimistic form state as an authoritative persisted record. Tool-to-trade and tool-to-diary actions are drafts and still require this submit flow.

## API request and error lifecycle

```mermaid
flowchart TD
    Request[Generated client request] --> Token{Access token?}
    Token -->|yes| Edge
    Token -->|no| Edge
    Edge --> Auth{Route authorization}
    Auth -->|denied| Problem[ProblemDetails]
    Auth -->|allowed| Downstream[EdgeTransport request]
    Downstream --> Result{Outcome}
    Result -->|2xx valid JSON| DTO[Typed DTO]
    Result -->|downstream 4xx| Problem
    Result -->|timeout| P504[504]
    Result -->|network unavailable| P503[503]
    Result -->|invalid JSON/shape| P502[502]
    Problem --> Unauthorized{401 once?}
    Unauthorized -->|yes| Refresh[Attempt refresh and retry]
    Unauthorized -->|refresh fails| End[Clear token and protected cache]
    Unauthorized -->|no| UIError[Translate stable error code]
    P504 --> UIError
    P503 --> UIError
    P502 --> UIError
```

## Trade creation and update

```mermaid
flowchart TD
    Diary[Open owned diary] --> TxList[Load transactions]
    TxList --> TxForm[Create or edit form]
    TxForm --> OptionalTool{Need position sizing?}
    OptionalTool -->|yes| Tools[Open Tools with typed context]
    Tools --> Calculate[Validate and calculate]
    Calculate --> Draft[Build editable trade draft]
    Draft --> TxForm
    OptionalTool -->|no| Validate[Validate transaction]
    TxForm --> Validate
    Validate --> Submit[Explicit POST or PUT]
    Submit --> Journal[Journal ownership and SQL]
    Journal --> Refresh[Invalidate diary and transaction queries]
```

## Diary creation and update

```mermaid
flowchart TD
    Entry[New diary, existing diary, quick note, or tool result] --> Draft[Editable diary state]
    Draft --> Date[Validate local date]
    Date --> Content[Validate title, Markdown, and tags]
    Content --> Submit{Operation}
    Submit -->|new| Post[POST /api/app/diaries]
    Submit -->|existing| Put[PUT /api/app/diaries/id]
    Submit -->|quick note| Quick[POST /api/app/quick-note]
    Post --> Stored[Journal persistence]
    Put --> Stored
    Quick --> Stored
    Stored --> Queries[Refresh list, detail, calendar, dashboard]
```

## Tools input to action

```mermaid
flowchart TD
    Input[Editable inputs] --> Validate[validateTool]
    Validate -->|invalid| Errors[Field-level messages]
    Validate -->|valid| Pure[calculateTool]
    Pure --> Result[Result card]
    Result -->|explicit save| Recalc[Backend recalculation and snapshot]
    Result -->|trade action| TradeDraft[Editable transaction draft]
    Result -->|diary action| DiaryDraft[Editable Markdown diary draft]
    Recalc --> History[Recent calculations]
    History --> Reopen[Restore prior editable inputs]
```

## Theme selection and application

```mermaid
sequenceDiagram
    participant Storage as Local mirror
    participant Boot as main.tsx
    participant DOM
    participant Settings
    participant Identity
    Boot->>Storage: read validated appearance/accent
    Boot->>DOM: apply data-theme/data-accent before paint
    Settings->>Identity: PUT account preferences
    Identity-->>Settings: persisted settings
    Settings->>DOM: reconcile scheme and accent
    Settings->>Storage: update last-known mirror
    Note over DOM: semantic gain/loss colors remain independent of accent
```

## Background alert processing

```mermaid
flowchart LR
    Journal[Diary mutation] --> Outbox[Journal outbox]
    Outbox --> Inbox[Reminder inbox]
    Inbox --> ReminderWorker[Reminder worker]
    ReminderWorker --> Delivery[In-app delivery attempt]

    Market[Completed published bar] --> PriceWorker[Price Alert worker]
    PriceWorker --> Claim[Locked deterministic batch]
    Claim --> Trigger[Unique alert/trading-date trigger]
```

Worker retries are protected by inbox/event IDs, locked claims, and unique occurrence keys.
