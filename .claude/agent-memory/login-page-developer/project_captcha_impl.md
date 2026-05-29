---
name: 驗證碼實作細節
description: CaptchaService 純 SVG 實作（非 Canvas/JS），6 位字元、干擾線、噪點（2026-05-29 現況快照）
type: project
---

## 實作方式

`CaptchaService` 使用**純 C# 產生 SVG**，不依賴 JavaScript 或 Canvas。

- 介面：`ICaptchaService`，回傳 `(string Text, string ImageBase64)` tuple
- 字符集：`ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789`（排除易混淆字元 I、O、l、0、1）
- 長度：6 位
- 輸出：Base64 SVG，格式 `data:image/svg+xml;base64,{base64}`，直接綁定至 `<img src="@captchaImage">` 標籤
- SVG 尺寸：140×42 px，背景色 `#f8fafc`

## 隨機性來源

全部使用 `RandomNumberGenerator.GetInt32()`（密碼學安全隨機），包含：
- 字符生成：`RandomNumberGenerator.GetInt32(Chars.Length)`
- 干擾線座標與顏色
- 噪點座標與顏色
- 文字旋轉角度與 y 位置偏移

**Why（第一波 #3）：** 原先用 `new Random()` 是可預測偽隨機，改為密碼學安全隨機。同時修復 RGB bug（`_random.Next(255)` 只生 0-254，改為 `GetInt32(256)` 正確 0-255）。

## 干擾元素

- 干擾線：6 條，隨機起點終點，隨機 `rgb(r,g,b)` 顏色，stroke-width=1，opacity=0.5
- 噪點：40 個圓形（r=1），隨機位置，隨機顏色，opacity=0.7
- 文字：字型 `Courier New, monospace`，font-weight bold，font-size 24，填色 `#374151`
  - 每個字元 x 間距 20px（從 x=15 開始），y=28 加 `-3~+3` 隨機偏移
  - 旋轉角度 `-15~+15` 度，以字元自身為旋轉中心

## 前端行為

- `Login.razor` 的 `GenerateNewCaptcha()` 呼叫 `CaptchaService.GenerateCaptcha()`（純 Server 端，無 JS Interop）
- `RefreshCaptcha()`：重新產生 + 清空 `loginModel.CaptchaInput`（開發模式下自動填入新驗證碼）
- 比對使用 `StringComparison.OrdinalIgnoreCase`（不分大小寫）
- UI 標籤文字：「不分大小寫」
- Input 欄位有 `uppercase` CSS class（視覺統一大寫顯示）
- 驗證碼顯示尺寸：`h-[42px] w-[140px]`，點擊觸發 `RefreshCaptcha()`
