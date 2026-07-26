# 01 — 建立 Journal Day 與 Quick Observation

**What to build:** 讓手機使用者能設定自己的 Journal Day，並以最低摩擦在 Today 建立當日第一則 Quick Observation。新 Market Observation 暫時與舊 Diary 並存，讓後續功能可以逐步遷移而不破壞既有系統。

**Blocked by:** None — can start immediately

**Status:** completed

- [x] 使用者可設定 timezone 與 Journal Day rollover，預設為當地時間 `00:00`
- [x] Quick Observation 只要求文字，成功後建立當日 Market Observation 與帶時間的 Observation Update
- [x] 同一 Journal Day 的後續 Quick Observation 追加至同一 Market Observation
- [x] 沒有內容的 Journal Day 不建立空白資料
- [x] Today 以手機優先流程顯示當日更新與新增入口
- [x] Observation Update 可由擁有者直接修改，回溯編輯時顯示不阻擋儲存的 Honesty Reminder
- [x] 覆蓋 Journal Day 邊界、ownership、建立／追加與手機畫面行為
