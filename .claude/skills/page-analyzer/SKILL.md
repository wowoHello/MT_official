---
name: page-analyzer
description: 讀取網頁程式碼並自動整理頁面資訊，包含技術棧、架構、資料表及查詢語法，並在 D:\MTrefer\pageFinal_doc 建立 .md 檔案。
---

# Page Analyzer Skill

此 Skill 旨在幫助 Manus 自動分析網頁程式碼庫，並針對每個頁面生成結構化的技術文件。

## 使用場景

當使用者要求「整理網頁程式碼」、「分析頁面架構」或「記錄資料表使用情況」時使用。

## 執行流程

1. **掃描目錄**：列出專案中所有的頁面檔案（如 `.razor`, `.aspx`, `.html`, `.vue`, `.js` 等）。
2. **分析內容**：針對每個頁面，分析其：
   - 使用的工具與技術。
   - 頁面組件架構。
   - 涉及的資料表名稱。
   - 具體的資料庫查詢語法（SQL 或 ORM 呼叫）。
3. **套用模板**：使用 `/home/ubuntu/skills/page-analyzer/templates/page_doc_template.md` 作為內容格式。
4. **輸出檔案**：將結果寫入至 `D:\MTrefer\pageFinal_doc\{PageName}.md`。

## 文件格式規範

- **檔名**：必須與頁面名稱一致，例如 `Home.md`。
- **語言**：使用簡單易懂的繁體中文，避免過度使用艱澀術語。
- **路徑**：務必輸出至 `D:\MTrefer\pageFinal_doc` 資料夾。

## 輸出範例

# Login.md

# 登入 Login

> **文件版本**：v1.0
> **最後更新**：2026-04-30
> **網站介紹**：此頁面最重要的功能為**忘記密碼**的寄送重設郵件與登入角色 Cookie 寫入。
> **頁面設計考量**：使用者多為年長者、須注重功能與效能

---

## 技術棧

- .NET 10 Blazor
- Dapper ORM
- Bootstrap 5

---

## 頁面架構

- 主登入表單
- 忘記密碼彈窗元件
- 驗證碼服務整合

---

## 使用資料表

- `Users` (使用者帳號資料)
- `LoginLogs` (登入紀錄)

---

## 資料表查詢語法一覽

```sql
SELECT * FROM Users WHERE Email = @Email AND Password = @Password
INSERT INTO LoginLogs (UserId, LoginTime) VALUES (@UserId, GETDATE())
```

---

## 其它

- 支援 Google OAuth 備援登入。
