# 14 — 切換至新產品並移除舊模型

**What to build:** 將尚未上線的產品正式切換到 Market Observation 與 self-review 模型，移除不再需要的舊功能與服務，並以一條 release golden path 證明第一版可完整運作。

**Blocked by:** 03 — 搜尋與瀏覽歷史市場觀察；06 — 記錄 Action Decision 與 Trade；07 — 提供 Pattern Review 與 Discipline Principle；09 — 發布美股 Daily Close 證據；12 — 增量同步與內容安全刪除；13 — 完成匯出與帳戶刪除

**Status:** ready-for-agent

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
