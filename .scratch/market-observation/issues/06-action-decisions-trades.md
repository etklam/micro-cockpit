# 06 — 記錄 Action Decision 與 Trade

**What to build:** 讓使用者保存事前行動意圖，並在之後比較實際執行是否遵守，而不把 Trade 擴張成 portfolio 或 accounting model。

**Blocked by:** 04 — 建立與追蹤 Expectation

**Status:** completed

- [x] Action Decision 可關聯 Observation Update 或 Expectation
- [x] intent 支援 `trade / continue_observing / avoid_trade`
- [x] Action Decision 保存 recorded time 與使用者當時理由
- [x] execution review 支援 `followed / partially_followed / deviated`
- [x] execution review 與 Outcome、P/L 完全分開
- [x] 使用者可附上簡單 Trade evidence，但不建立 order、fill、position、holdings 或 cost basis
- [x] 回溯修改 Action Decision 時顯示 Honesty Reminder，但不建立版本歷史
- [x] legacy Trade Draft 不會出現在新流程
- [x] 覆蓋 ownership、關聯完整性、validation 與手機操作
