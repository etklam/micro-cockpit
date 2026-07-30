# 03 — Agent User lifecycle 深化

**What to build:** 在 Identity 內建立一個 deep Agent User lifecycle module，集中 provisioning、manager attribution、listing、Token creation/rotation/revocation、exchange validation、usage tracking、export projection、deletion participation。HTTP 與 service-key handler 降為 adapter。對應 PRD slice 3。

**Status:** ready-for-agent

**Priority order:** slice 3 of 5

**Blocked by:** 02 — Access Grant selection 與 closure

**Target:** `services/identity-service/src/TradeDiary.Identity/` — 目前 lifecycle（provisioning、attribution、rotation、exchange、revocation、usage、export、deletion）以 route-local transaction sequences 散落在 743-line `Program.cs`。ADR-0004 / ADR-0007 已規範但未集中成一個 testable domain interface。

- [ ] 一個 deep Agent User lifecycle module，owned by Identity
- [ ] HTTP handler 與 service-key handler 為 adapter，不擁有 lifecycle transaction
- [ ] lifecycle transaction 留在 Identity-owned schema 內
- [ ] 每個 Agent User 至多一個 active API Token，rotation 為 atomic revoke-before-replace
- [ ] Token 以 hash 儲存，raw Token 僅一次性揭露；export 不含 raw Token 或 hash
- [ ] 保留 human-managed vs platform-operated Agent User 區分（為未來 platform-operated Agent 預留 ownership，對應 [[agent-is-judgement-outlet]] 記憶）
- [ ] 不把 Access Grant check 移進 Identity，不教 Identity 認識 Observation Subject / Instrument / Journal Day scope

**Test matrix（取代每條 route 一次）：**
- [ ] provisioning as human manager
- [ ] platform-operated Agent User 的 representation
- [ ] manager isolation
- [ ] creation/rotation 下的 one-active-Token
- [ ] revoked / malformed / expired / inactive 的 exchange failure
- [ ] usage timestamps
- [ ] export redaction
- [ ] account deletion cleanup
- [ ] 保留 HTTP adapter test（status 與 contract translation）

**Acceptance (PRD stories 40–54):** identity 與 credential 建立不會 partial disagree；rotation 無 overlap；revoke 即時生效；exchange 僅授與 granted scopes；last-used/last-successful-request 一致；export 含 managed Agent metadata 但無 secret；deletion 依既有順序移除 credential 與 identity；platform-operated 仍可表達。

**Constraint:** System Never Judges（[[system-never-judges]]）— Agent-authored judgement 永遠歸屬其 Agent User。
