---
name: 首頁功能模組卡片機制
description: ModuleCards 由 MainLayout CascadingValue 傳入，Home.razor 只負責渲染，不查 DB
type: project
---

## 資料流

`MainLayout` 查詢使用者權限 → 組建 `List<UserModuleCard>` → 透過 `CascadingValue(Name="ModuleCards")` 傳入所有子頁面 → `Home.razor` 過濾 `IsEnabled==true` 的項目渲染卡片。

## 渲染邏輯三種狀態

1. `ModuleCards is null` → 顯示 Spinner（尚未載入）
2. `enabledModules.Count == 0` → 顯示 EmptyState（「目前還沒指派的梯次唷！」）
3. 正常渲染 → 動態 Grid，依 index 計算 `delay = index * 50`ms 的 fadeIn 動畫

## Grid 佈局

`grid-cols-1 sm:grid-cols-2 xl:grid-cols-3` ，卡片最小高度 170px。

## 卡片動畫

無 `isReady` 狀態旗標，直接在卡片 div 以 inline style `animation: fadeIn 0.4s ease-out @(delay)ms both` 實現逐卡 fadeIn，delay = index * 50ms（動畫 keyframe 定義在 `wwwroot/css/input.css`）。

## UserModuleCard 型別位置

定義於 `Models/ProjectModels.cs`（非 HomeModel.cs），包含 `Name`、`Description`、`Icon`、`PageUrl`、`BgColorClass`、`ColorClass`、`IsEnabled` 欄位。

**Why:** 權限判斷集中在 MainLayout，Home 只呈現結果，避免重複查詢 DB。
**How to apply:** 不要在 Home.razor 的 @code 裡自行查詢模組清單，應等待 CascadingParameter 傳入。
