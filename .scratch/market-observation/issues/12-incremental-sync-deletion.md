# 12 — 增量同步與內容安全刪除

**What to build:** 讓 Agent User 可可靠同步授權資料的變更與刪除，同時確保永久保留的 event／inbox history 不含任何 User-entered personal content。

**Blocked by:** 11 — 授權 Agent User 讀取 Journal 紀錄

**Status:** completed

- [x] Incremental Query 以 cursor 或 update time 回傳授權範圍內的 changes
- [x] cursor 有效期為 90 天
- [x] 過期 cursor 回傳 `410 cursor_expired`，並指示執行 fresh scoped synchronization
- [x] included record 刪除後只回傳 record ID、record type 與 deletion time
- [x] deletion marker 在 supported cursor window 內可取得，且永不包含已刪除內容
- [x] immutable outbox events 與 consumer inboxes只允許 record ID、record type、version、operation 與 event time
- [x] event／inbox payload 不含 Observation text、reasoning、review notes、Action Decision、Mental State、Watchlist Note、Discipline Principle、Trade details、URL、quotation 或 attachment content
- [x] consumer 在收到 content-free deletion event 後移除或 tombstone owned references
- [x] Account Delegate writes 可附 optional unverified source label，未附時不宣稱可辨認 delegate
- [x] 覆蓋 edit visibility、delete propagation、cursor expiry、payload inspection 與 full-stack Agent sync
