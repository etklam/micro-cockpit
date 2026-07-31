# PRD — 效能 / 品質 / 體驗審查（Performance / Quality / UX Review）

**Origin:** 2026-08-01 唯讀審查：「review 一下這個 project 的性能及品質，有那些用戶體驗可改善地方」。
**Scope:** 前端（React 19 / Vite 8 / TanStack Query）、後端（journal-service + market-data-service）、Edge、DB index。**不含程式碼變更**，僅產出可執行的 ticket。

## 產品原則對齊

所有 ticket 皆遵守：「平靜、精準、節制」、System Never Judges、不為 dopamine 而設計、行動裝置優先、WCAG 2.2 AA、EN + 繁中一等公民。**沒有任何一項會移除驗證／除錯／資料保護／安全性／無障礙。**

## Ticket 總覽（8 項發現合併為 5 ticket）

| # | Ticket | 軸 | 嚴重度 | Triage | 源自 |
|---|--------|----|--------|--------|------|
| 01 | Frontend quality & consistency（error boundary + tz 問候 + loading 統一） | 品質+UX | 🔴 高 | completed | 舊 01+02+03 |
| 02 | Responsive save path（後端 fan-out 並行 + 前端樂觀更新） | 效能+UX | 🟡 中高 | completed | 舊 04+05 |
| 03 | Route-level code-splitting | 效能 | 🟢 中 | completed | 舊 06 |
| 04 | Split oversized components（pages.tsx / settings.tsx） | 品質 | 🟢 低中 | completed | 舊 07 |
| 05 | Observation search indexing（keyword + jsonb 篩選） | 效能 | ⚪ 低* | completed | 舊 08 |

\* 05 目前規模 OK；等到 profiling 命中才做。

**Blocking edges:** 全 sparse——任一 ticket 皆可立即開始。03 與 01 的 error boundary 有 soft edge（code-splitting 的 lazy chunk 載入失敗應能被 boundary 接住），建議兩者協調但不硬性 block。

## 建議執行順序

1. **先做 01**：P0 小改動、品牌對齊「可靠／精準」。
2. **02**：後端 fan-out 並行化 + 前端樂觀更新對「儲存」體驗有乘數效果，建議同一 agent 一氣呵成。
3. **03**：路由級 code-splitting（與 01 的 boundary 協調 fallback）。
4. **04**：大元件重構（人類把關，可分頁分 PR）。
5. **05**：以 forward-only migration 先完成可部署的索引與 query rewrite；PostgreSQL 可用時再補 production-scale `EXPLAIN ANALYZE`。

## 關鍵證據索引（審查時收集）

- **無 Error Boundary**：`grep -rn "componentDidCatch|getDerivedStateFromError|ErrorBoundary" frontend/src` → 無結果。
- **問候語**：`frontend/src/pages.tsx:274` `const hour = new Date().getHours()`（裝置時）；同檔 `:153` 已用 `Intl.DateTimeFormat` + `timeZone` 算時間，模式可重用。
- **Loading 不一致**：skeleton `pages.tsx:679`、`731`；純文字 `:804`、`:833`、`:940`。
- **後端 N+1**：`services/journal-service/src/TradeDiary.Journal/ObservationInstruments.cs:39-51`（foreach + await）；端點迴圈 `ObservationEndpoints.cs:34-36`、`109-111`。market-data 無批次端點（`services/market-data-service/src/TradeDiary.MarketData/Program.cs:133`）。
- **無樂觀更新**：所有 mutation 僅 `onSuccess` invalidation，如 `queries.ts:154-160`、`161-167`。
- **無 code-splitting**：`App.tsx:35-56` 全 eager；`grep "lazy(|<Suspense"` → 無；`dist/index-*.js` 490KB raw / 137KB gz；`vite.config.ts` 無 `manualChunks`。
- **大元件**：`pages.tsx` 991 行 / 59 `useState`；`screens/settings.tsx` 506 行。
- **搜尋**：前綴萬用 ILIKE `ObservationQuery.cs:74-78`；逐列 jsonb EXISTS `:79-93`。
