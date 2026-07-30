# 02 — Access Grant selection 與 closure 深化

**What to build:** 將分散在 6 處的 grant-eligibility SQL 集中到一個 deep selection module；closure materialization（fixed/ongoing）集中到一個 deep closure module；full-sync / incremental-sync / Human-Agent comparison 維持為三個 concrete adapter。對應 PRD slice 2（Journal Day 之後最高安全/隱私優先）。

**Status:** ready-for-agent

**Priority order:** slice 2 of 5

**Blocked by:** 01 — Journal Day 與 Bootstrap rename（先讓 release path 綠再開此 slice）

**Duplication seam（已偵察）：** 相同的 eligibility predicate（active-grant + owner isolation + date range + fixed/ongoing subject 語意 + expiry/revocation）分別 encoding 在：
- `AccessGrantEndpoints.cs` — `ReadGrantedPageAsync`(154–256, full sync `QueryAsync`)、`ReadChangesAsync`(311–446, incremental `IncrementalAsync`)、`ReadClosureAsync`(448–530, closure materialization)、`CaptureFixedClosureAsync`(532–573, fixed closure capture at grant creation)
- `ComparisonEndpoints.cs` — `HasGrantAsync`(57–83)、`ReadOwnerAsync`(85–178, `constrainToGrant` bool 切換 grant predicate)

- [ ] 一個 deep Access Grant **selection** module：owns active-grant check、owner relationships、date selection、subject 與 Instrument selection、fixed-vs-ongoing mode 語意
- [ ] 一個 deep Access Grant **closure** module：owns 哪些 Market Observation child records 被 materialize
- [ ] 三個 adapter（full-sync / incremental-sync / comparison）不改變行為，改 route through 共用 seam
- [ ] filter parsing、cursor parsing、transport error translation 留在 adapter 層（除非多 adapter 共用）
- [ ] 保留 cursor format / expiration、fixed closure capture at creation、continuous evaluation for ongoing、content-free deleted changes
- [ ] 以 focused eligibility matrix 取代兩個 giant scenario：owner、Agent User、date、subject、Instrument、fixed mode、ongoing mode、expiry、revocation、deletion
- [ ] 同一組 eligibility case 跑過三個 adapter，證明 no filter 可 widen the grant
- [ ] deleted changes 只含 record identity、type、operation、time，無個人內容

**Acceptance (PRD stories 17–39, 90):** expired/revoked 在每個 read mode 被拒；date/subject/Instrument scope 三個 adapter 一致；owner isolation 在 materialize 前套用；fixed grant 保留 captured closure 且 captured ID 的編輯可見、後建 child 不自動加入；ongoing grant 納入後續 matching record；comparison 唯讀、owner 分離、unavailable vs empty 區分清楚。

**Constraint:** ADR-0002 — selection module 不得做 cross-schema read/write/FK/trigger；ADR-0004 — Access Grant 仍 Journal-owned（scope 用 Journal concepts，不可移到 Identity）。
