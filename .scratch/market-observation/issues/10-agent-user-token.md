# 10 — Provision Agent User 與單一 API Token

**What to build:** 讓人類使用者建立可獨立擁有市場觀察的 Agent User，並以一組可輪替、可撤銷的 API Token 驗證身分。

**Blocked by:** None — can start immediately

**Status:** ready-for-agent

- [ ] 人類使用者可在 Settings provision 自己管理的 Agent User
- [ ] Agent User 具有獨立 identity 與 ownership，不與管理者共用資料擁有權
- [ ] 每個 Agent User 同時只能有一個 active API Token
- [ ] 產生新 Token 時立即撤銷舊 Token
- [ ] Token 預設不自動過期，並可由管理者撤銷
- [ ] Settings 顯示 Token created、last used 與 last successful request time
- [ ] 只有 managing human User 可管理該 Agent User 與 Token
- [ ] Agent User 可透過公開 exchange flow 使用 Token 驗證
- [ ] 覆蓋 rotation、revocation、ownership、replay 與錯誤回應
