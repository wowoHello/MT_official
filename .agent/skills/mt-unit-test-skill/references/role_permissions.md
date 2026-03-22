# 命題系統角色權限參考表

本文件定義了「命題系統」中三種主要身分的權限範圍與預期可見的 UI 模組。

## 1. 系統管理員 (Admin)
- **帳號**: `admin_sys`
- **權限代碼**: `admin`
- **預期可見模組**:
    - `dashboard`: 命題儀表板
    - `projects`: 命題專案管理
    - `overview`: 命題總覽
    - `tasks`: 我的命題任務
    - `reviews`: 審題清單
    - `teachers`: 教師管理系統
    - `roles`: 角色與權限管理
    - `announcements_edit`: 系統公告(管理)

## 2. 命題教師 (Teacher)
- **帳號**: `teacher_demo`
- **權限代碼**: `teacher`
- **預期可見模組**:
    - `tasks`: 我的命題任務
    - `reviews`: 審題清單
    - `announcements_view`: 系統公告(檢視)

## 3. 審題委員 (Reviewer)
- **帳號**: `expert_reviewer`
- **權限代碼**: `reviewer`
- **預期可見模組**:
    - `reviews`: 審題清單
    - `announcements_view`: 系統公告(檢視)

---

## 測試驗證邏輯
1. **登入驗證**: 模擬輸入帳號密碼後，檢查是否成功導向至 `firstpage.html`。
2. **區塊驗證**: 在首頁或測試頁面中，檢查 `moduleGrid` 內渲染的卡片數量與 ID 是否與上述清單相符。
3. **負面測試**: 確保「命題教師」與「審題委員」無法看到管理類模組（如 `roles`, `teachers`）。
