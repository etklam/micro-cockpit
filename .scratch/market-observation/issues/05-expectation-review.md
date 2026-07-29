# 05 — 完成 Expectation Review

**What to build:** 讓 Expectation 擁有者分開評估 Outcome 與保留的推理品質，並以可彙整的 Reasoning Issue／Strength 解釋判斷。

**Blocked by:** 04 — 建立與追蹤 Expectation

**Status:** completed

- [x] 只有 Expectation 擁有者可建立或修改正式 Expectation Review
- [x] Outcome 支援 `confirmed / partially_confirmed / invalidated / indeterminate`
- [x] reasoning quality 支援 `sound / mixed / weak`
- [x] partially confirmed 與 indeterminate 必須填寫簡短理由
- [x] 提供 PRD 定義的六個預設 Reasoning Issues 與六個 Reasoning Strengths
- [x] 使用者可建立自己的 Reasoning Issue／Strength labels
- [x] Daily Close 或其他證據只能輔助判斷，不自動決定 Outcome
- [x] 完成 review 後 Expectation readiness 變為 `reviewed`
- [x] 覆蓋 validation、owner isolation、deleted source 與雙語回顧流程

**決定（grill 2026-07-27）：**
- Review 記錄**不存任何 Daily Close 欄位**。Daily Close 僅為 Review 畫面旁附的唯讀證據，缺資料顯示 unavailable 且不阻擋提交（遵 System Never Judges，見 CONTEXT.md 與 ADR-0007）。Review 只存 Outcome、reasoning quality、issue／strength labels 與必要理由。
- reasoning label 聚合鍵：系統六個預設 label 用程式碼常數 key（附 i18n），不落 DB；使用者自訂 label 存 `reasoning_labels` 並以 UID 為穩定鍵。Review 掛 label 的欄位需能同時指向常數 key 或自訂 UID，供 issue 07 Pattern Review 正確 count。
- **已定（grill 2026-07-27）：** 使用者事後改自訂 label 名稱時，歷史 review 的聚合**跟著改名**。聚合鍵是 UID 不是文字，改名只影響顯示，歷史 count／denominator 不變。
- UI 措辭遵 System Never Judges：只能呈現「你的 invalidation condition 是 X，Daily Close 是 Y」這類事實並排，不得出現「已失效／判定為 invalidated」等系統結論式字眼。
