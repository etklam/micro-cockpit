# 15 — Observation / Expectation 雙 owner 對照視圖

**What to build:** 讓使用者把同一 subject／時間範圍下，自己與某個 Agent User 各自擁有的 Market Observation 與 Expectation 並排對照，作為「你 vs AI」對照組的呈現層。

**Blocked by:** 05 — 完成 Expectation Review；10 — Provision Agent User 與單一 API Token；11 — 授權 Agent User 讀取 Journal 紀錄

**Status:** ready-for-agent

**背景（grill 2026-07-27）：** 使用者的核心用途是自己 provision 一個 AI Agent 作為平行觀察者／對照組。issue 10–13 建的是身分／授權／同步**底座**，沒有任何 issue 產出對照的**呈現層**。本 issue 補上，避免底座做完卻缺最想要的畫面。此視圖同時是 System Never Judges 的最佳落點：兩套判斷並排，系統一字不評。

- [ ] 使用者可選一個 subject（或 Instrument）與時間範圍，並排檢視自己與指定 Agent User 的 Observation Update 與 Expectation
- [ ] 對照 Expectation 的 Outcome 與 reasoning quality 時，各 owner 的判斷明確標記所屬（人類 vs Agent），不合併成單一結論
- [ ] 系統只呈現兩套資料的客觀並排與差異（例如 Outcome 是否一致、confidence 差異），不產生系統結論或評分，遵 System Never Judges（CONTEXT.md／ADR-0007）
- [ ] Agent 一側資料透過既有 Access Grant 讀取，唯讀，不可從對照視圖修改任一 owner 的紀錄
- [ ] 缺失資料（一方無對應紀錄、或無 Daily Close 證據）明確顯示 unavailable，不阻擋檢視
- [ ] mobile-first 並排／堆疊佈局，符合 PRODUCT accessibility 要求（非僅以顏色表達差異）
- [ ] 覆蓋 owner 標記正確性、grant 範圍外資料不外洩、空對照與雙語呈現
