---
name: 驗證碼實作細節
description: CaptchaService 純 SVG 實作（非 Canvas/JS），6 位字元、干擾線、噪點
type: project
---

## 實作方式

`CaptchaService` 使用 **純 C# 產生 SVG**，不依賴 JavaScript 或 Canvas。

- 字符集：`ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789`（排除易混淆字元 I、O、l、0、1）
- 長度：6 位
- 輸出：Base64 SVG，直接綁定至 `<img src="@captchaImage">` 標籤
- 尺寸：140×42 px，背景 `#f8fafc`

## 干擾元素

- 干擾線：6 條，隨機顏色 RGB、透明度 0.5
- 噪點：40 個圓點，隨機位置與顏色，透明度 0.7
- 文字旋轉：每個字元隨機 -15° 到 +15°，y 位置隨機偏移 ±3px

## 前端行為

- `RefreshCaptcha()` 呼叫 `CaptchaService.GenerateCaptcha()`（純 Server 端，無 JS Interop）
- 開發模式下 `loginModel.CaptchaInput` 自動填入，刷新也跟著填
- 驗證碼比對使用 `StringComparison.OrdinalIgnoreCase`（不分大小寫）
- UI 提示文字：「不分大小寫」
- Input 欄位有 `uppercase` class（視覺統一顯示）

## Why

歷史原型使用 Canvas+JS，遷移 Blazor 後改為純 C# SVG 實作，消除 JS Interop 依賴，Server-side render 安全。
