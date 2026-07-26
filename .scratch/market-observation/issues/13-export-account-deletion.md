# 13 — 完成匯出與帳戶刪除

**What to build:** 讓使用者可帶走完整第一版資料，並在刪除紀錄或帳戶時真正移除 User-owned personal content，而不破壞其他服務的 immutable replay history。

**Blocked by:** 07 — 提供 Pattern Review 與 Discipline Principle；08 — 建立 Watchlist 與 Watchlist Note；12 — 增量同步與內容安全刪除

**Status:** ready-for-agent

- [ ] Settings 可產生包含所有第一版 User-owned records 與 relationships 的 structured JSON export
- [ ] export 不包含其他 User 或 Agent User 未擁有且未授權匯出的資料
- [ ] 第一版不提供 full import 或 merge workflow
- [ ] 使用者可永久刪除自己的 Journal records，且刪除不留下 personal content snapshot
- [ ] account deletion 移除該 User 擁有的 Journal content、Agent Users、Tokens 與 grants
- [ ] account deletion 不替其他 User 刪除其 independently owned records，但移除 source content access
- [ ] downstream services 只保留 content-free tombstone 或清理後 reference
- [ ] export、record deletion 與 account deletion 都維持 schema ownership 與 no-cross-schema-write ADR
- [ ] 覆蓋 export completeness、authorization、cascade semantics 與 content absence
