# 05 — Instrument identity 與 Daily Close publication 深化

**What to build:** 在 Market Data 內建立兩個 deep module：(1) Instrument identity — symbol normalization、creation/lookup、active-symbol replacement、inactive aliases、symbol conflict、symbol-history transition；(2) Daily Close publication — provider-run creation、running-state validation、staged bar validation、run 內 idempotent row replacement、completion、success publication、failed-run invisibility。HTTP handler 降為 adapter。對應 PRD slice 5。

**Status:** ready-for-agent

**Priority order:** slice 5 of 5

**Blocked by:** 04 — Frontend domain modules

**Target:** `services/market-data-service/src/TradeDiary.MarketData/` — 目前 Instrument continuity 與 completed-session Daily Close publication 交錯在 route 與 persistence，主要靠一條大 integration path 驗證。

- [ ] 一個 deep **Instrument identity** module（Market Data owned）：symbol normalization、Instrument create/lookup、active-symbol replacement、inactive alias、cross-Instrument conflict、symbol-history transition（與 current-symbol change 同一 transaction）
- [ ] 一個 deep **Daily Close publication** module（Market Data owned）：provider-run creation、running-state validation、staged bar validation、run 內 idempotent row replacement、completion、atomic success publication、failed-run invisibility
- [ ] HTTP handler 為 adapter；Journal 的 Daily Close enrichment 為 consumer adapter，只依賴 versioned published read
- [ ] 保留既有 public read contract：Instrument directory、lookup、bars、Daily Close、provider health

**Test（real-PostgreSQL Market Data integration seam，拆 focused group）：**
- [ ] symbol replacement、inactive alias、cross-Instrument conflict、rollback、safe retry
- [ ] running / succeeded / failed provider-run transition
- [ ] 同一 run 內 duplicate row 與 batch failure
- [ ] staged 或 failed row 不出現在 published read
- [ ] 成功 completion 原子發布 raw + adjusted close
- [ ] 保留一條 end-to-end：Journal 接上 published Daily Close evidence

**Acceptance (PRD stories 69–84, 89):** symbol 變動仍保 Instrument identity；僅一個 current active symbol；inactive alias 不出現在 directory；跨 Instrument assign 被原子拒絕；symbol-history transition 與 current-symbol 同步；provider run 僅 running 時接受 staged row；completed run 不可變；invalid bar 發布前被拒；duplicate row 同一 run 內 deterministic；成功 run 原子發布、失敗 run 不留 staged evidence；transition 集中強制；Daily Close 發布成功前 unavailable；raw/adjusted 分開；unsupported/missing 顯式表示不偽造為零；Journal consumer 只依賴 published read。

**Constraint:** 不修改歷史 migration；只有當 invariant 無法用既有 schema 強制時才加 forward-only migration。
