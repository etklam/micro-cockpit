# 09 — 發布美股 Daily Close 證據

**What to build:** 讓使用者在 Market Observation 與 Expectation Review 中取得相關美股 Instrument 的 completed-session price evidence，同時維持外部 ingestion 邊界與可控 provider 使用量。

**Blocked by:** 04 — 建立與追蹤 Expectation；08 — 建立 Watchlist 與 Watchlist Note

**Status:** completed

- [x] Tracked Instrument 包含 active Expectation、Watchlist membership 或最近 30 日 Observation Update 的 Instrument
- [x] 對外提供外部 ingestion job 可取得的目前 tracked US Instrument set
- [x] 保留外部 provider-run ingestion contract；Market Data 不直接呼叫 provider 或保存 provider credentials
- [x] 每個 completed US session 可發布 raw close 與 adjusted close
- [x] provider run 完成前資料不可見，成功完成後才透過 published contract 可讀
- [x] 缺失資料具有明確 unavailable 狀態且不阻擋 Expectation Review
- [x] Product page load 不觸發 provider fetch
- [x] Observation／Review 可顯示對應 Daily Close evidence，其他市場明確顯示 unsupported
- [x] 覆蓋 ingestion publication、tracking expiry、corporate-action values 與 release smoke
