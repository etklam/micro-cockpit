# 07 — 提供 Pattern Review 與 Discipline Principle

**What to build:** 讓使用者從已完成的 Expectation Reviews 看見重複的推理問題與優勢，並手動維護一條當前要遵守的 Discipline Principle。

**Blocked by:** 05 — 完成 Expectation Review

**Status:** completed

- [x] Review 支援 weekly、monthly 與 custom date range
- [x] Pattern Review 顯示 reviewed Expectations 數量
- [x] 每個 Reasoning Issue／Strength 顯示 count、denominator 與 evidence links
- [x] 系統不自動建立 Confirmed Pattern 或宣稱某標籤是使用者的問題
- [x] 使用者可手動 create、disable 與 archive Discipline Principles
- [x] 使用者可選定至多一條 Discipline Principle 顯示在 Today
- [x] 選定另一條 Principle 時不自動 archive 原 Principle
- [x] 覆蓋 aggregation boundaries、empty ranges、ownership 與 Principle selection

**決定（grill 2026-07-27）：**
- 聚合鍵沿用 issue 05：系統預設 label 用常數 key、自訂 label 用 UID。改自訂 label 名稱不影響歷史聚合（鍵是 UID）。
- Pattern Review 允許對使用者自己的資料做**排序與趨勢**呈現（例如「出現最多次的 Reasoning Issue」「較上月的次數變化」），因為那是使用者資料的客觀關係。**不得**加入系統結論（不宣稱某 label 是問題、不建議改進），遵 System Never Judges（CONTEXT.md／ADR-0007）。
