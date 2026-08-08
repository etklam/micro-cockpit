# Agent Task — Complete the Micro Cockpit Product

你只可以讀取及修改目前 repository。

不要：

* 搜尋其他 repository
* 假設存在舊系統可供參考
* 複製其他專案的 source code
* 要求使用者提供舊 codebase
* 建立舊資料 migration
* 加入與本任務無關的 compatibility layer

所有需求已包含在本 prompt。請先閱讀目前 repository 的：

```text
PRODUCT.md
DESIGN.md
DEVELOPMENT_PLAN.md
README.md
ADR files
SERVICE.md files
database migrations
OpenAPI contracts
frontend source
tests
CI workflows
```

然後根據實際 codebase 判斷哪些功能已完成、部分完成或仍然缺失。

---

## Goal

將目前系統完善為一個完整的：

```text
Diary-first Trade Journal
+ Structured Review
+ Trade Planning
+ Daily P/L Calendar
+ Stock Research
+ Market Rotation
+ Diary and Price Alerts
+ Partner and Agent Collaboration
```

必須保持現有架構：

```text
React + TypeScript + Vite frontend
ASP.NET Core Edge API / BFF
Independently replaceable backend services
One PostgreSQL instance
One schema per service
Frontend only calls Edge API
Runtime OpenAPI contracts
Generated frontend API client
```

---

# Hard Constraints

禁止加入：

```text
Holdings engine
Average-cost calculation
Portfolio accounting
Tax-lot accounting
Broker sync
Automatic trading
Real-time trading terminal
Cross-service direct SQL
Shared DbContext
Shared domain/application kernel
Frontend direct calls to backend services
Handwritten frontend request/response contracts
```

其他規則：

* 每個 service 只可讀寫自己的 PostgreSQL schema。
* Cross-user resource 必須回傳 `404`，不是 `403`。
* Edge 可以 compose screen-ready responses，但不可擁有 domain tables。
* API 必須 app-first，不要只暴露 database CRUD。
* 新增或修改 endpoint 後必須重新產生 OpenAPI 和 frontend client。
* 所有 code comments 必須使用英文。
* 保持改動集中，不做無關 refactor。
* 不要合併現有 services。
* 優先延伸現有 service，而不是建立新 service。
* 所有 feature 必須包括 migration、API、Edge、frontend、tests 和 docs。
* 寫入操作必須評估 idempotency、retry 和 concurrency。

---

# Step 1 — Current-State Audit

在修改之前，建立一份簡短 audit：

```text
docs/product-completion-audit.md
```

按以下分類列出功能：

```text
Complete
Backend complete but frontend incomplete
Partially implemented
Missing
Intentionally excluded
```

至少檢查：

```text
Authentication
Diary
Quick Note
Transactions
Daily P/L
Calendar
Structured review
Trade plans
Disciplines
Diary alerts
Watchlist
Stock research
Price alerts
Market rotation
Partners
Agents and API keys
Articles
Tools
Routing
PWA
Internationalization
User settings
Notifications
```

Audit 必須引用實際 file paths、routes、tables 和 tests，不可只根據 documentation 推測。

完成 audit 後直接開始實作，不需要等待確認。

---

# Priority 1 — Structured Diary Review

目前 Diary 必須由基本日記升級為可長期複盤的核心功能。

## Diary fields

在 Diary capability 增加：

```text
tags
thesis
risk
execution
reviewDueAt
reviewStatus
reviewedAt
```

建議 review status：

```text
not_scheduled
pending
completed
skipped
```

可以根據現有 naming convention 微調，但必須：

* 使用 database constraint
* 使用 typed DTO
* 出現在 OpenAPI
* frontend 不可依賴散落的 magic strings
* status transitions 必須由 backend enforce

## Tags

支援：

```text
Create and update diary tags
Trim whitespace
Reject empty tags
Normalize duplicates
Display tags in diary list
Filter diary list by tag
```

不要建立 taxonomy microservice。

Tags 可以由 Journal service 自己管理。

## Markdown

Diary content：

* 以 Markdown source 儲存
* 編輯時使用 textarea
* 閱讀時安全 render
* 禁止 unsanitized raw HTML
* 支援 headings、lists、emphasis、links、quotes、inline code 和 code blocks

## Review Queue

新增 frontend-facing endpoint：

```text
GET /api/app/reviews
```

回傳：

```text
overdue
dueToday
upcoming
recentlyCompleted
```

每個 review item 至少包含：

```text
diaryId
localDate
title
tags
reviewDueAt
reviewStatus
thesis
risk
execution
```

提供 application actions：

```text
POST /api/app/reviews/{diaryId}/complete
POST /api/app/reviews/{diaryId}/skip
POST /api/app/reviews/{diaryId}/reschedule
```

Frontend 建立 Review Queue page：

* Overdue
* Due today
* Upcoming
* Open diary
* Complete
* Skip
* Reschedule

## Quick Note Templates

提供使用者自訂 template：

```text
id
name
content
sortOrder
isActive
createdAt
updatedAt
```

功能：

* List
* Create
* Update
* Delete
* Reorder
* Enable/disable
* Today page 選擇 template
* Template 只填入 composer，不可自動提交

## Append to Existing Diary

Quick Note submission 必須支援：

```text
Create a new diary
Append to a selected diary on the same local date
```

禁止靜默覆蓋原有內容。

Append 必須：

* 保留原 content
* 使用明確 separator
* 保持 idempotency
* 驗證 diary ownership
* diary date 必須符合 requested local date

## Acceptance Criteria

* 可以建立及更新 structured review fields。
* 更新 Diary 不會損壞 existing transactions。
* Markdown rendering 已 sanitize。
* Review Queue 只回傳目前 user 的資料。
* Cross-user review action 回傳 404。
* Review transition 有 unit 和 integration tests。
* Quick Note retry 不會重複 append。
* Tag normalization 有 tests。
* Today、Diary、Calendar 頁面保持可用。

---

# Priority 2 — Trade Plans

實作完整 Trade Plan workflow。

除非現有架構有明確不同 owner，否則 Trade Plan 應由 Journal capability 擁有，不要新增 service。

## Data Model

```text
id
userId
diaryId nullable
symbol
setupType nullable
entryPrice nullable
entryZoneLow nullable
entryZoneHigh nullable
stopLoss nullable
targetPrice nullable
maxPositionSize nullable
invalidationCondition nullable
notes nullable
status
createdAt
updatedAt
```

Status：

```text
draft
active
closed
cancelled
```

## Validation

* Symbol trim 並轉為 uppercase。
* Entry、stop、target values 必須大於零。
* `entryZoneLow <= entryZoneHigh`。
* `maxPositionSize >= 0`。
* Diary link 必須屬於目前 user。
* Cross-user diary link 回傳 404。
* Status transitions 由 backend 控制。
* Closed/cancelled plan 不可隨意回到 active，除非定義明確 reopen action。
* Diary 被刪除時，Trade Plan 應保留並將 `diaryId` 設為 null。
* 將 Diary deletion 行為寫入 service documentation。

## API

```text
GET    /api/app/trade-plans
POST   /api/app/trade-plans
GET    /api/app/trade-plans/{id}
PUT    /api/app/trade-plans/{id}
DELETE /api/app/trade-plans/{id}

POST /api/app/trade-plans/{id}/activate
POST /api/app/trade-plans/{id}/close
POST /api/app/trade-plans/{id}/cancel
POST /api/app/trade-plans/{id}/reopen
```

List endpoint 支援：

```text
status
symbol
diaryId
sort
```

## Frontend

建立：

```text
Trade Plan list
Trade Plan create page
Trade Plan detail page
Trade Plan edit page
```

提供：

* Filter by status
* Search by symbol
* Entry-zone display
* Stop and target display
* Risk/reward preview
* Invalidation condition
* Notes
* Diary link/unlink
* Activate
* Close
* Cancel
* Reopen

UI 必須 calm、precise、restrained，不使用 gamification。

## Acceptance Criteria

E2E workflow：

```text
Create draft
→ Edit
→ Link diary
→ Activate
→ Close
```

另外測試：

* Invalid price ranges
* Cross-user access
* Invalid transitions
* Diary deletion unlinks plan
* OpenAPI/client drift check

---

# Priority 3 — Complete Existing Backend Workflows

先檢查 backend 是否已具備能力；已有的不可重寫。

## Price Alerts

Frontend 必須完整支援現有 condition types，包括：

```text
Above price
Below price
Percent change
Moving-average crossing
```

視 backend contract 顯示：

```text
lookbackDays
direction
baselinePrice
lastEvaluatedAt
lastTriggeredAt
status
triggerHistory
dataHealth
```

提供：

```text
Create
Delete
Dismiss
Reactivate
View trigger history
```

Market data unavailable 或 stale 時要明確顯示，不能當成「未觸發」。

## Partner Workflow

補齊：

```text
Invite human partner
View outgoing invitations
View incoming invitations
Accept
Reject
Revoke
Edit sharing policy
View partner profile
Compare diaries
```

Sharing policy 應至少支援現有 domain capability：

```text
shareDiaries
shareTransactions
sharePerformance
```

權限必須由 backend enforce。

建立 Partner Compare read model：

```text
GET /api/app/partners/{partnerId}/compare
```

回傳按 user timezone 對齊的日期：

```text
localDate
ownerDiary
partnerDiary
ownerPerformance
partnerPerformance
```

只可回傳 sharing policy 容許的資料。

Frontend 提供 side-by-side compare view。

## Agent and API Key Management

建立完整管理頁：

```text
List agents
Create agent
List API keys
Create API key
Show raw key once
Copy raw key
Revoke key
Set expiry
Set scopes
View last-used timestamp
```

Raw key：

* 只在 creation response 顯示一次
* database 只儲存 hash
* 之後不可再次讀取
* 不可出現在 logs

Agent scopes 至少考慮：

```text
diary:write
research:read
research:write
watchlist:read
timeline:write
```

根據現有 architecture 補齊：

```text
Agent diary ingestion
Agent stock-note write
Agent stock-timeline bulk ingestion
Agent watchlist read
```

Bulk ingestion 必須：

* 有 batch size limit
* 每筆支援 idempotency key
* 不因單筆 duplicate 建立重複 event
* 回傳逐項結果

## Articles

補齊：

```text
Article list
Article detail
Slug-based route
Safe Markdown render
Draft
Published
Archived
Admin editor
Preview
Publish
Archive
```

Public API 只能回傳 published articles。

---

# Priority 4 — Deepen Market Rotation

保留 Rotation service，不要建立 portfolio accounting。

## Required Read Model

Market Rotation UI 至少支援：

```text
Universe selector
sectors
indexes
core
```

每個 item 顯示：

```text
symbol
label
snapshotDate
rank
rankDelta
twoWeekReturn
rsi14
rsiDelta
maStatus
percentFromHigh
rotationScore
rotationScoreDelta
signal
```

Dashboard 顯示：

```text
Market state
Breadth confirmation
Universe coverage
Latest qualified date
Comparison date
Top improving
Bottom weakening
Deterministic summary
Stale-data warning
```

## Historical View

提供：

```text
2-week normalized trend
Historical market-state view
Historical rank movement
```

不要做：

```text
Portfolio exposure gap
Holdings classification
Cost-basis analysis
Broker integration
Automatic allocation
```

可以提供 market-only risk posture：

```text
risk_on
neutral
defensive
risk_off
unknown
```

並提供 deterministic explanation，但不可聲稱是個人化投資建議。

## Worker Correctness

修正並驗證：

* 使用最新已發布 market trading date，而不是 UTC calendar date。
* `insufficient_data` 在 source data 更新後可以 retry。
* Failed batch run 必須保留 failure record。
* Claim、calculation 和 final status 有正確 transaction boundary。
* 多 instance 不可重複處理同一 batch。
* Stale snapshot 必須由 API 和 UI 明確顯示。

---

# Priority 5 — Stock and ETF Research

## Stock Research

補齊：

```text
Stock search
Watchlist ordering
Mutable current research note
Note revision history
Immutable timeline events
Create timeline event
Correction event
Timeline filters
Historical price chart
Research-state empty/loading/error views
```

禁止由 frontend 直接修改 immutable timeline record。

若需要修正，新增 correction event 或明確 correction endpoint。

## ETF Research

Stock 和 ETF 必須保持分開。

提供：

```text
ETF directory
ETF watchlist
ETF detail page
ETF quote
Historical chart
Relative strength
Risk metrics
Research note
```

Valuation 只有在現有 data source 可靠且有測試時才加入；不可製造假數據。

---

# Priority 6 — Application Completeness

## Routing

加入正式 client-side routing：

```text
Today
Diary
Diary detail
Reviews
Trade Plans
Calendar
Disciplines
Diary Alerts
Watchlist
Stock Research
ETF Research
Price Alerts
Market Rotation
Partners
Agents
Articles
Tools
Settings
```

要求：

* 每頁有穩定 URL
* Browser back/forward 正常
* Refresh 不會回到 Today
* 支援 deep link
* 有 404 page
* Auth redirect 保留 intended destination

## Registration and Settings

補齊：

```text
Registration UI
Password change
Timezone setting
Base currency setting
Display name
Session/logout management
```

Registration key 仍可由部署設定控制，但一般使用者不應需要手動呼叫 API。

## Internationalization

至少建立：

```text
English
Traditional Chinese
```

要求：

* UI strings 不可散落 hardcode
* Locale selection
* Locale persistence
* Date/number/currency formatting 使用 locale
* API error code 與 UI translation 分離

## PWA

加入：

```text
Web manifest
Installable app
Icons
Service worker
Offline shell
Safe update strategy
```

不要 cache authentication responses、private API payload 或 sensitive diary content，除非有明確安全設計。

## Notifications

Price Alert 和 Diary Alert 至少提供：

```text
Unread indicator
Notification centre
Polling or SSE
Trigger timestamp
Dismiss action
```

不要在第一階段加入複雜 browser push infrastructure，除非現有架構已支持。

---

# Testing Requirements

每個 phase 都必須同時加入測試。

最低要求：

## Unit Tests

* Tag normalization
* Review status transitions
* Trade Plan validation
* Trade Plan transitions
* Reminder recurrence
* Price-alert conditions
* Rotation calculations
* Partner policy mapping
* Markdown sanitization

## PostgreSQL Integration Tests

使用 production migrations，而不是自行建立簡化 test tables。

覆蓋：

* Diary structured fields
* Review queue
* Quick Note idempotency
* Trade Plans
* Cross-user 404
* Partner authorization
* API key hashing and revocation
* Rotation batch state transitions

## Edge Integration Tests

覆蓋：

* Edge → Journal
* Edge → Partner
* Edge → Rotation
* Authorization header forwarding
* Idempotency-Key forwarding
* Location header forwarding
* Optional service degradation
* Required service failure

## E2E Tests

至少覆蓋：

```text
Register/login/logout
Create and review diary
Quick Note append
Trade Plan lifecycle
Daily P/L calendar
Price Alert lifecycle
Partner invitation and compare
Agent API key creation/revocation
Market Rotation universe selection
Deep-link refresh
```

---

# CI and Release Requirements

CI 必須執行：

```text
dotnet build
dotnet test
frontend lint
frontend typecheck
frontend build
OpenAPI drift check
Generated client drift check
migration validation
architecture boundary checks
Docker Compose validation
E2E smoke workflow
```

E2E workflow 必須使用實際可連接的 ports 或 Edge routes。

禁止出現 smoke script 呼叫未 expose 的 backend port。

---

# Documentation

更新：

```text
README.md
DEVELOPMENT_PLAN.md
PRODUCT.md
relevant SERVICE.md files
OpenAPI docs
docs/operations.md
docs/product-completion-audit.md
```

如新增重要跨模組決定，建立 ADR，例如：

```text
Trade Plan ownership
Diary deletion and linked Trade Plans
Partner compare privacy policy
Review status transition model
Markdown sanitization policy
```

---

# Delivery Strategy

按以下順序提交：

```text
1. Current-state audit
2. Structured Diary Review
3. Trade Plans
4. Complete existing backend workflows
5. Deep Market Rotation
6. Stock and ETF Research
7. Routing, settings, i18n and PWA
8. Final tests, CI and documentation
```

每個 phase 都必須：

```text
Build successfully
Pass tests
Regenerate OpenAPI
Regenerate frontend client
Avoid unrelated changes
Include migration rollback considerations
Update completion audit
```

---

# Final Definition of Done

任務只有在以下條件全部成立時才算完成：

* Diary 支援 structured review、tags、Markdown 和 review queue。
* Trade Plan 完整可用。
* Price Alert UI 覆蓋 backend condition types。
* Partner invitation、policy 和 compare workflow 可用。
* Agent API key 可完整管理。
* Market Rotation 不再只是簡單 ranking cards。
* Stock 和 ETF research 有獨立且可用的頁面。
* 所有主要頁面有 URL routing。
* Registration、settings、i18n 和 PWA 可用。
* 所有新增功能有 production migration 和 tests。
* OpenAPI 和 generated client 無 drift。
* E2E workflow 真正通過。
* 沒有加入 holdings、cost basis 或 portfolio accounting。
* 沒有讀取、引用或依賴任何其他 repository。
