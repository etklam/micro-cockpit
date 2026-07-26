# 11 — 授權 Agent User 讀取 Journal 紀錄

**What to build:** 讓人類使用者把明確範圍內的 Journal records 以唯讀方式授權給自己 provision 的 Agent User，同時讓 Agent User 管理自己的第一版紀錄。

**Blocked by:** 03 — 搜尋與瀏覽歷史市場觀察；05 — 完成 Expectation Review；06 — 記錄 Action Decision 與 Trade；08 — 建立 Watchlist 與 Watchlist Note；10 — Provision Agent User 與單一 API Token

**Status:** ready-for-agent

- [ ] 人類使用者只能授權給自己 provision 的 Agent User
- [ ] Access Grant 僅允許讀取 human-owned records，不允許 Agent User 修改它們
- [ ] grant 可設定 subject、date range、optional expiry 與 revoke
- [ ] fixed grant 固定建立當下完整 closure IDs；included IDs 的後續修改可見，新 child records 不自動加入
- [ ] ongoing grant 持續納入未來符合 scope 的 Market Observations 與 child records
- [ ] Market Observation closure 只包含其 Updates、Expectations、Reviews、Action Decisions 與 Trades
- [ ] Agent query 支援 cursor pagination 與 date、subject、Instrument、Tag、review readiness、author filters
- [ ] Agent User 可 CRUD 自己的 Market Observations、Updates、Expectations、Reviews、Action Decisions、Trades 與 Watchlists
- [ ] revocation 或 expiry 後立即拒絕未來讀取
- [ ] UI 清楚說明 revoke 無法刪除 Agent 已在系統外保存的副本
- [ ] 覆蓋 fixed／ongoing closure、scope leakage、expiry、revocation 與 owner isolation
