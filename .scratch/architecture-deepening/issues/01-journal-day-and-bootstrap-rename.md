# 01 — Journal Day 正確性與 Bootstrap contract rename

**What to build:** 建立 Edge 與 Journal 共用的權威 Journal Day 模組（ADR-0001 禁止 shared kernel，故為刻意重新實作而非引用），並將 Bootstrap contract 欄位 `currentLocalDate` 改名為 `currentJournalDay`（跨 contract、generated client、frontend）。對應 PRD slice 1（最高優先）。

**Status:** ready-for-human

**Priority order:** slice 1 of 5（見 `../PRD.md` Implementation Decisions §173）

- [x] Bootstrap/Calendar 不再只用 timezone 推導日期，改用 rollover-aware 解析（與 Journal write 同一條演算法）
- [x] 重新實作 JournalDay.Resolve（DST gap→首個有效 local minute；DST fold→較早發生 = Max offset）
- [x] 無效的 persisted timezone/rollover 視為 controlled failure（不再 silently fallback UTC），Bootstrap 回 400
- [x] contract `currentLocalDate`→`currentJournalDay`（無相容 alias，產品未上線）
- [x] compose-edge-openapi.mjs、generated edge.ts、frontend 呼叫端（App.tsx、pages.tsx、測試）同步
- [x] Calendar 預設月份與選取日由 `currentJournalDay` 推導
- [x] 同一行為矩陣同時跑在 Journal 與 Edge，cross-adapter 不一致會被抓到
- [x] settings 變更不重算歷史 Market Observation 的 Journal Day

**Acceptance (PRD stories 1–16, 89):** Today/Calendar 顯示的日期＝Quick Observation 寫入用的 Journal Day；non-midnight rollover 全站一致；DST gap/fold 行為一致；local midnight 仍為預設；corrupt prefs 回 controlled error；contract 僅此一處依 PRD 改名。

## Comments

2026-07-30 — 實作完成於 working tree（未 commit）。gates：solution build 0 error；Edge 51 / Identity 27 / Journal 24 / MarketData 1 / Tool 1 全綠；`api:verify`（45 paths/63 ops）綠；frontend lint/test(61)/build 綠；`verify-architecture.sh`（4 services/1 event）綠；`validate-openapi.py`（166 ops parity）綠。11 檔變更待 commit（commit/push 為人工決策）。
