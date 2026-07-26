# 04 — 建立與追蹤 Expectation

**What to build:** 讓使用者把部分市場看法轉成有期限、失效條件與信心程度的 Expectation，並清楚知道何時需要回顧。

**Blocked by:** 02 — 結構化 Observation Update

**Status:** completed

- [x] Observation Update 可選擇建立 Expectation，普通觀察不強制建立
- [x] Expectation 包含 expected behavior、deadline、invalidation condition、confidence 與相關 market
- [x] confidence 僅支援 `low / medium / high`
- [x] 支援常用 deadline preset 與 custom date/time
- [x] 只有具正確 market calendar 的市場提供 trading-day preset；其他市場要求 custom date
- [x] review readiness 使用 `active / ready_for_review / reviewed`
- [x] deadline 到達或使用者確認 invalidation 已發生時進入 `ready_for_review`
- [x] 修改已到期 deadline 時顯示不阻擋儲存的 Honesty Reminder
- [x] 覆蓋 deadline、market calendar、early invalidation、ownership 與狀態推導
