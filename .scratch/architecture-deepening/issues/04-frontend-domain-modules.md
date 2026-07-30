# 04 — Frontend deep domain modules

**What to build:** 把 frontend page 與 generated client 之間的廣泛 pass-through 換成 deep domain module，涵蓋 Market Observation、Expectation/Review、Agent User/Access Grant 三條 flow。generated client 維持為 transport adapter。對應 PRD slice 4。

**Status:** ready-for-agent

**Priority order:** slice 4 of 5

**Blocked by:** 03 — Agent User lifecycle

**Target:** `frontend/src/` — 目前 transport alias、query key、mutation invalidation rule、payload mapping、大型 page 實作分散；caller 必須學太多 generated client 與 cache 細節。

- [ ] Market Observation + Observation Update domain module
- [ ] Expectation + Expectation Review domain module
- [ ] Agent User + Access Grant domain module
- [ ] 每個 module owns：generated-client adaptation、query keys、not-found normalization、query enablement、mutation-driven cache consequences
- [ ] page module 只留 rendering、accessibility、route state、ephemeral interaction state；不擁有 generated operation name 或 cross-page cache 知識
- [ ] generated client 仍為 transport adapter，繼續從 Edge OpenAPI 重新產生
- [ ] 不引入新 state-management / form / runtime dependency
- [ ] pass-through alias 只在 deletion test **失敗**時刪除；保留有 error normalization / idempotency / cache / domain mapping 行為者

**Test（Vitest + Testing Library + MSW + TanStack Query）：**
- [ ] domain query state、not-found normalization、enablement、mutation consequence 在 module interface 測
- [ ] Market Observation mutation refresh Today 與 affected Calendar
- [ ] Expectation/Review mutation refresh Today、Review、Calendar、Pattern Review（如適用）
- [ ] Agent User/Access Grant mutation 只 refresh 自己的 management data
- [ ] 保留 screen test：render 行為、鍵盤互動、semantic label、error state、accessibility
- [ ] 持續驗證 generated client fresh

**Acceptance (PRD stories 55–68, 88):** page 不再學 generated operation 細節；存 Expectation refresh Today/Review/Calendar 一致；reviewed state 不 stale；改 Access Grant 只 refresh management state 不影響無關 cache；淺層 one-line alias 移除使導航變短；新 seam 僅在多 caller 或多 concrete adapter 處出現（[[system-never-judges]] 不受影響）。

**Deletion test（PRD §264）：** 若移除一個 module 等於把複雜度逼回多個 caller → 不是 pass-through，保留；若移除只是少一層轉發 → 刪。
