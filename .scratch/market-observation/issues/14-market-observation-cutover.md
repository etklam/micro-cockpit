# 14 — 切換至新產品並移除舊模型

**What to build:** 將尚未上線的產品正式切換到 Market Observation 與 self-review 模型，移除不再需要的舊功能與服務，並以一條 release golden path 證明第一版可完整運作。

**Blocked by:** 03 — 搜尋與瀏覽歷史市場觀察；06 — 記錄 Action Decision 與 Trade；07 — 提供 Pattern Review 與 Discipline Principle；09 — 發布美股 Daily Close 證據；12 — 增量同步與內容安全刪除；13 — 完成匯出與帳戶刪除

**Status:** ready-for-agent

**排序決定（grill 2026-07-27）：** legacy 退役採逐步進行，不等本 issue。以下工作提前到後續能力建置之前完成，避免 Access Grant / 增量同步 / content-free deletion 把 legacy 命名與事件形狀烤進對外 contract：
- DROP legacy `journal.diaries`、`journal.transactions`、`journal.diary_reviews`、`journal.diary_tags`（未上線，不做資料搬遷）。
- scope 由 `diary:read`／`diary:write` 改為人類 session 的 `journal:read`／`journal:write`；Agent 唯讀另立 `journal:agent-read`（token 層即區分人類與 agent）。
- `DiaryDeleted.v1` 直接換成 content-free 的 `RecordDeleted.v1`（record ID、record type、version、operation、event time），同時砍掉 reminder-service 的消費端（reminder-service 本就在移除清單）。
- `RecordDeleted.v1` 的 `record_type` 僅涵蓋 grant closure 內六型別：`market_observation`、`observation_update`、`expectation`、`expectation_review`、`action_decision`、`trade`。
本 issue 保留的仍是最終切換與 release golden path 驗證。

**提前處理的 review 修正（grill 2026-07-27，不等本 issue 收尾）：**
- **前端導覽重構提前**：主導覽現為 `today / diary / calendar / discipline / alerts` + More 內的 `watchlist / price-alerts / rotation / partners / articles`（見 `frontend/src/App.tsx`），與 PRD 定義的 Today／Review／Watchlist／Calendar／Tools／Settings 不符。提前把主導覽換成六項、刪除 `latePages.tsx` 內 doomed 路由（`/price-alerts`、`/rotation`、`/partners`、`/articles`、`/diary`）。Review 目前指向舊 `MonthlyReviewPage`，需改指向新的 Expectation Review + Pattern Review。
- **Edge 路由與服務同批拆除**：`gateway/TradeDiary.EdgeApi/Program.cs` 仍掛 `PerformanceEndpoints`、`ReminderEndpoints`、`ResearchEndpoints`、`PartnerEndpoints`。服務刪除與 Edge 路由拆除必須在同一 PR，避免「服務已刪、Edge 仍掛死路由」的中間狀態。
- **DESIGN.md 同步更新**：§10 Page patterns 仍是 Diary／Alerts／research 舊模型；更新對齊六項導覽，並把 System Never Judges 落到 §12 Content voice（Outcome 提示措辭必須是「你的 condition 是 X，Daily Close 是 Y」，不得是「已失效」之類判斷語）。

- [ ] 主導覽為 Today、Review、Watchlist、Calendar、Tools、Settings
- [ ] 移除或封閉舊 Diary、Trade Draft、Partner Compare、Educational Articles、standalone Research Timeline、standalone Diary Reminder 與舊 Performance surfaces
- [ ] 刪除只服務永久移除能力的 backend services、routes、contracts、migrations wiring 與 frontend code
- [ ] 保留 Identity、Journal、Market Data、Tool 與 Edge 第一版 active boundaries
- [ ] 保留匿名 calculators、Calculator Presets 與 Calculation Snapshots
- [ ] Average Cost 與 Profit/Loss 維持 standalone，Action Decision Draft 仍 deferred
- [ ] English 與 Traditional Chinese core workflows 使用現有 typed i18n architecture
- [ ] mobile-first core flows 符合 PRODUCT accessibility requirements
- [ ] full-stack Edge release smoke 串起 Human、Observation、Expectation、Review、Watchlist、Daily Close、Agent User、Access Grant、incremental deletion 與 removed routes
- [ ] migration verification 覆蓋 ownership、one-token invariant、grant constraints、symbol history、raw／adjusted published views 與 content-free event constraints
- [ ] CI、frontend tests、service integration tests、release smoke 與 migration safety checks 全部通過
