# 05 — 完成 Expectation Review

**What to build:** 讓 Expectation 擁有者分開評估 Outcome 與保留的推理品質，並以可彙整的 Reasoning Issue／Strength 解釋判斷。

**Blocked by:** 04 — 建立與追蹤 Expectation

**Status:** ready-for-agent

- [ ] 只有 Expectation 擁有者可建立或修改正式 Expectation Review
- [ ] Outcome 支援 `confirmed / partially_confirmed / invalidated / indeterminate`
- [ ] reasoning quality 支援 `sound / mixed / weak`
- [ ] partially confirmed 與 indeterminate 必須填寫簡短理由
- [ ] 提供 PRD 定義的六個預設 Reasoning Issues 與六個 Reasoning Strengths
- [ ] 使用者可建立自己的 Reasoning Issue／Strength labels
- [ ] Daily Close 或其他證據只能輔助判斷，不自動決定 Outcome
- [ ] 完成 review 後 Expectation readiness 變為 `reviewed`
- [ ] 覆蓋 validation、owner isolation、deleted source 與雙語回顧流程
