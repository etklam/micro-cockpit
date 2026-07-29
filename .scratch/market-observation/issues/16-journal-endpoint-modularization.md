# 16 — journal-service 路由模組化與共用 human/agent policy

**What to build:** 在 legacy 退役的同時，把 journal-service 單一 2000+ 行的 `Program.cs` 依 aggregate 拆成 endpoint 模組，並把「human-only／agent-read」授權抽成全服務共用組件，避免上帝服務與複製的安全邊界。

**Blocked by:** None — 與 legacy 退役同批進行，先於 issue 05–13 新增大量路由之前

**Status:** completed — 結構重構部分（見下）；scope rename + 共用 policy 已移交 issue 10–13

**背景（grill 2026-07-27）：** `Program.cs` 已 2172 行、單檔掛 30+ 路由，新舊模型混在同一 `/internal` group 與同一 `diaryAccess` policy 下。issue 05–13 還要再加 Review、Action Decision、Pattern Review、Watchlist、Access Grant、增量同步；不先拆，檔案會膨脹到難以維護。另 `content-service` 已有 `account_type != "agent"` 的 humanOnly policy，但目前是單服務內複製——Agent 存取是安全邊界，不應各服務各抄一份。

- [x] journal-service 路由依 aggregate 拆為 endpoint 模組（`ObservationEndpoints`、`ExpectationEndpoints`、`LegacyDiaryEndpoints`），沿用 Edge API 既有的 `XxxEndpoints.Map(...)` 組織方式。`Program.cs` 786 → 65 行，純 composition root
- [x] 純結構重構，對外 API 契約與行為不變（journal 整合測試 34/34 全綠，2026-07-27）
- [x] legacy 路由（diaries／transactions／reviews／quick-note／partner-diaries 共 18 條）全數收進單一 `LegacyDiaryEndpoints.cs`，讓 issue 14 退役等同刪一個檔
- [x] 將 human-only／agent-read 授權抽成跨服務共用組件，取代各服務複製的 policy 定義 — `TradeDiary.Authorization` 已由 Identity、Journal、Tool 與 Edge 共用
- [x] 共用 policy 對齊 scope：Agent 自有紀錄使用 `journal:read`／`journal:write`，grant read 使用獨立 `agent:read`
- [x] active services 改用共用組件並驗證 Agent token 無法透過 grant 修改其他 owner；content-service 已於 cutover 移除

## 執行修正（2026-07-27）

分兩塊出貨，因為只有第一塊是安全的純結構重構：

**已完成（本次，pure structural）：** 路由模組化。三個模組 `Map(RouteGroupBuilder)`，`Program.cs` 只留 setup + group + 三個 `Map` 呼叫。scope／policy 一字未動，行為完全不變，34 個整合測試全綠。

**移交 scope rename + 共用 policy（原以為安全，實為跨服務 + 動資料）：** `diary:read`／`diary:write` → `journal:*` 的改名並非孤立字面量。`identity-service/Program.cs:159` 在建 API key 時白名單驗證 `diary:read`／`diary:write`，且 `identity.api_keys.scopes` 已把這些字串存進 DB；Edge `EdgeConfiguration.cs:33` 的 `diaryAccess` policy 與 `tests/TradeDiary.EdgeApi.Tests` 也硬編這些 scope。改名會牽動 identity 白名單、已簽發 agent key 的存量資料、Edge policy 與測試——是跨服務且動資料的變更。而 `journal:agent-read`（agent 唯讀）本身是語意變更，與 issue 10–13 尚未落地的 agent 作者／reviewer + access grant 模型綁在一起。因此把 rename 與共用 policy 移到 10–13：屆時 agent 讀寫語意才真正拍板，identity scope migration 也可一併設計。此前 `diaryAccess`／`diary:*` 維持原樣。
