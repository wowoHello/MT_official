```dbml
// CWT 命題工作平臺 — 統一資料庫架構 (DBML for dbdiagram.io)
// 版本: v2.0 (TINYINT 數值化優化，與正式 SQL 同步)
// 轉換日期: 2026-03-27

// -----------------------------------------------------------------------------
// 1. 基礎表 (無外鍵依賴)
// -----------------------------------------------------------------------------

Table MT_Roles {
  Id int [pk, increment, note: '唯一識別碼']
  Code tinyint [not null, unique, note: '角色代碼 (0:ADMIN, 1:STAFF, 2:TEACHER, 3:REVIEWER)']
  Name nvarchar(50) [not null, unique, note: '角色顯示名稱']
  Category tinyint [not null, note: '角色分類 (0:內部, 1:外部)']
  Description nvarchar(500) [note: '角色功能描述']
  IsDefault bit [not null, default: 0, note: '是否為系統預設角色']
  CreatedAt datetime2 [not null, default: `SYSDATETIME()`, note: '記錄建立時間']
  UpdatedAt datetime2 [not null, default: `SYSDATETIME()`, note: '最後修改時間']

  note: '定義系統中的使用者職務與權限分類'
}

Table MT_QuestionTypes {
  Id int [pk, increment, note: '唯一識別碼']
  Code tinyint [not null, unique, note: '題型代碼數值 (1:單選, 2:複選, 3:題組...)']
  Name nvarchar(50) [not null, note: '題型名稱']
  Icon nvarchar(50) [note: '顯示圖示']
  SortOrder int [not null, default: 0, note: '排序次序']

  note: '定義 7 種試題類型'
}

Table MT_Modules {
  Id int [pk, increment, note: '唯一識別碼']
  ModuleKey nvarchar(50) [not null, unique, note: '模組識別代碼']
  Name nvarchar(50) [not null, note: '模組名稱']
  Icon nvarchar(50) [note: '選單圖示']
  PageUrl nvarchar(100) [note: '頁面路由路徑']
  SortOrder int [not null, default: 0]
  IsActive bit [not null, default: 1]

  note: '定義系統的主功能模組樹'
}

// -----------------------------------------------------------------------------
// 2. 核心使用者表
// -----------------------------------------------------------------------------

Table MT_Users {
  Id int [pk, increment, note: '唯一識別碼']
  Username nvarchar(100) [not null, unique, note: '登入帳號']
  DisplayName nvarchar(50) [not null, note: '使用者姓名']
  Email nvarchar(200) [note: '電子信箱']
  PasswordHash nvarchar(500) [not null, note: '密碼雜湊值']
  RoleId int [not null, ref: > MT_Roles.Id, note: '所屬角色 ID']
  Status tinyint [not null, default: 1, note: '帳號狀態 (0:停用, 1:啟用, 2:鎖定)']
  CompanyTitle nvarchar(100) [note: '內部人員職稱']
  Note nvarchar(500) [note: '管理備註']
  IsFirstLogin bit [not null, default: 1, note: '是否首次登入']
  RememberToken nvarchar(500) [note: '記住我 Token']
  LastLoginAt datetime2 [note: '最後登入時間']
  CreatedAt datetime2 [not null, default: `SYSDATETIME()`, note: '帳號建立時間']
  UpdatedAt datetime2 [not null, default: `SYSDATETIME()`, note: '資料更新時間']

  indexes {
    RoleId
    Status
    Email
  }
  note: '儲存所有系統使用者，包含內部職員與外部教師'
}

// -----------------------------------------------------------------------------
// 3. 依賴 MT_Users 的延伸表
// -----------------------------------------------------------------------------

Table MT_RolePermissions {
  Id int [pk, increment, note: '唯一識別碼']
  RoleId int [not null, ref: > MT_Roles.Id, note: '關聯角色 ID']
  ModuleId int [not null, ref: > MT_Modules.Id, note: '模組 ID']
  IsEnabled bit [not null, default: 0, note: '是否啟用該模組']
  AnnouncementPerm tinyint [not null, default: 1, note: '公告權限 (0:無, 1:檢視, 2:編輯)']
  CreatedAt datetime2 [not null, default: `SYSDATETIME()`]
  UpdatedAt datetime2 [not null, default: `SYSDATETIME()`]

  indexes {
    (RoleId, ModuleId) [unique]
  }
  note: '定義各角色對不同功能模組的存取權限'
}

Table MT_PasswordResetTokens {
  Id int [pk, increment, note: '唯一識別碼']
  UserId int [not null, ref: > MT_Users.Id, note: '使用者 ID']
  Token nvarchar(500) [not null, unique, note: '重設識別碼']
  ExpiresAt datetime2 [not null, note: '截止時間']
  IsUsed bit [not null, default: 0, note: '是否已使用']
  CreatedAt datetime2 [not null, default: `SYSDATETIME()`]

  note: '管理忘記密碼功能的驗證連結'
}

Table MT_LoginLogs {
  Id int [pk, increment, note: '唯一識別碼']
  UserId int [ref: > MT_Users.Id, note: '使用者 ID']
  Username nvarchar(100) [not null, note: '登入帳號']
  IsSuccess bit [not null, note: '是否成功']
  IpAddress nvarchar(50) [note: '客戶端 IP']
  UserAgent nvarchar(500) [note: '瀏覽器環境']
  FailReason nvarchar(200) [note: '失敗原因']
  CreatedAt datetime2 [not null, default: `SYSDATETIME()`]

  note: '記錄所有登入嘗試，用於安全稽核'
}

Table MT_Teachers {
  Id int [pk, increment, note: '唯一識別碼']
  UserId int [not null, unique, ref: - MT_Users.Id, note: '關聯帳號 ID (1:1)']
  TeacherCode nvarchar(10) [not null, unique, note: '教師編號 (如 T1001)']
  Gender tinyint [note: '性別 (0:未知, 1:男, 2:女)']
  Phone nvarchar(20) [note: '聯絡電話']
  IdNumber nvarchar(200) [note: '身分證 (加密)']
  School nvarchar(100) [not null, note: '任教學校']
  Department nvarchar(50) [note: '系所']
  Title nvarchar(20) [note: '職稱']
  Expertise nvarchar(200) [note: '專長']
  TeachingYears int [note: '教學年資']
  Education tinyint [note: '學歷 (1:學士, 2:碩士, 3:博士)']
  Note nvarchar(500) [note: '人才庫備註']
  CreatedAt datetime2 [not null, default: `SYSDATETIME()`, note: '匯入時間']
  UpdatedAt datetime2 [not null, default: `SYSDATETIME()`, note: '更新時間']

  note: '教師人才庫，儲存命題與審題人員的詳細背景'
}

Table MT_AuditLogs {
  Id int [pk, increment, note: '唯一識別碼']
  UserId int [ref: > MT_Users.Id, note: '執行人 ID']
  Action tinyint [not null, note: '操作類型 (0:建立, 1:修改, 2:刪除, 3:登入, 4:登出)']
  TargetType tinyint [not null, note: '目標表類型 (0:Users, 1:Roles, 2:Projects, 3:Questions, 4:Announcements, 5:Teachers, 6:Reviews)']
  TargetId int [note: '目標資料主鍵']
  OldValue nvarchar(MAX) [note: '原始資料 (JSON)']
  NewValue nvarchar(MAX) [note: '新資料 (JSON)']
  IpAddress nvarchar(50) [note: '來源 IP']
  CreatedAt datetime2 [not null, default: `SYSDATETIME()`]

  note: '系統層級稽核記錄，追蹤關鍵資料變動'
}

Table MT_UserGuideFiles {
  Id int [pk, increment, note: '唯一識別碼']
  FileName nvarchar(200) [not null, note: '檔名']
  FilePath nvarchar(500) [not null, note: '路徑']
  FileSize bigint [not null, note: '大小']
  UploadedBy int [not null, ref: > MT_Users.Id, note: '上傳者 ID']
  IsActive bit [not null, default: 1, note: '可用狀態']

  note: '管理使用者操作手冊與相關 PDF 文件資源'
}

// -----------------------------------------------------------------------------
// 4. 專案管理群組
// -----------------------------------------------------------------------------

Table MT_Projects {
  Id int [pk, increment, note: '唯一識別碼']
  ProjectCode nvarchar(20) [not null, unique, note: '專案代碼']
  Name nvarchar(100) [not null, note: '專案名稱']
  Year int [not null, note: '專案年度']
  Semester tinyint [note: '學期 (1:上學期, 2:下學期, 3:暑假)']
  School nvarchar(100) [note: '合作學校']
  Status tinyint [not null, default: 0, note: '專案狀態 (0:準備中, 1:進行中, 2:已結案)']
  StartDate date [not null, note: '計畫開始日']
  EndDate date [not null, note: '計畫結束日']
  ClosedAt datetime2 [note: '實際結案時間']
  CreatedBy int [ref: > MT_Users.Id, note: '建立人 ID']
  Description nvarchar(500) [note: '專案描述']
  CreatedAt datetime2 [not null, default: `SYSDATETIME()`]
  UpdatedAt datetime2 [not null, default: `SYSDATETIME()`]

  note: '管理命題梯次 (Project/Epoch) 的生命週期'
}

Table MT_ProjectPhases {
  Id int [pk, increment, note: '唯一識別碼']
  ProjectId int [not null, ref: > MT_Projects.Id, note: '專案 ID']
  PhaseCode tinyint [not null, note: '階段代碼 (1:命題, 2:一審, 3:二審...)']
  PhaseName nvarchar(50) [not null, note: '階段名稱']
  StartDate date [not null, note: '階段開始日']
  EndDate date [not null, note: '階段截止日']
  SortOrder int [not null, note: '排序順序']
  CreatedAt datetime2 [not null, default: `SYSDATETIME()`]
  UpdatedAt datetime2 [not null, default: `SYSDATETIME()`]

  indexes {
    (ProjectId, PhaseCode) [unique]
  }
  note: '定義專案內部的各個階段 (命題、三審等) 時間區間'
}

Table MT_ProjectTargets {
  Id int [pk, increment, note: '唯一識別碼']
  ProjectId int [not null, ref: > MT_Projects.Id, note: '專案 ID']
  QuestionTypeId int [not null, ref: > MT_QuestionTypes.Id, note: '題型 ID']
  Level tinyint [not null, note: '等級 (1:基礎, 2:進階, 3:挑戰)']
  TargetCount int [not null, default: 0, note: '目標命題數']

  note: '各專案對各題型、等級的命題數量總體需求'
}

Table MT_ProjectMembers {
  Id int [pk, increment, note: '唯一識別碼']
  ProjectId int [not null, ref: > MT_Projects.Id, note: '專案 ID']
  UserId int [not null, ref: > MT_Users.Id, note: '使用者 ID']
  JoinedAt datetime2 [not null, default: `SYSDATETIME()`, note: '加入專案時間']

  indexes {
    (ProjectId, UserId) [unique]
  }
  note: '關聯專案與參與的人員'
}

Table MT_ProjectMemberRoles {
  Id int [pk, increment, note: '唯一識別碼']
  ProjectMemberId int [not null, ref: > MT_ProjectMembers.Id, note: '成員 ID']
  RoleCode tinyint [not null, note: '在該專案的角色任務 (對應 MT_Roles 的 Code)']

  note: '支援一人在同一專案中具備多重任務身分'
}

Table MT_MemberQuotas {
  Id int [pk, increment, note: '唯一識別碼']
  ProjectMemberId int [not null, ref: > MT_ProjectMembers.Id, note: '成員 ID']
  QuestionTypeId int [not null, ref: > MT_QuestionTypes.Id, note: '題型 ID']
  Level tinyint [not null, note: '等級 (1:基礎, 2:進階, 3:挑戰)']
  QuotaCount int [not null, default: 0, note: '指派數']

  note: '指派給特定人員的命題配額數量'
}

Table MT_Announcements {
  Id int [pk, increment, note: '唯一識別碼']
  Category tinyint [not null, note: '公告分類 (1:系統, 2:專案)']
  Status tinyint [not null, default: 0, note: '發佈狀態 (0:草稿, 1:發佈, 2:封存)']
  ProjectId int [ref: > MT_Projects.Id, note: '專案 ID (空為全域)']
  PublishDate date [not null, note: '發佈日期']
  UnpublishDate date [note: '截止日期']
  IsPinned bit [not null, default: 0, note: '是否置頂']
  Title nvarchar(200) [not null, note: '標題']
  Content nvarchar(MAX) [not null, note: '內容 (HTML)']
  AuthorId int [not null, ref: > MT_Users.Id, note: '發佈人 ID']
  CreatedAt datetime2 [not null, default: `SYSDATETIME()`]
  UpdatedAt datetime2 [not null, default: `SYSDATETIME()`]

  note: '系統公告與最新消息管理'
}

// -----------------------------------------------------------------------------
// 5. 題目與命題群組 (核心業務表)
// -----------------------------------------------------------------------------

Table MT_Questions {
  Id int [pk, increment, note: '唯一識別碼']
  ProjectId int [not null, ref: > MT_Projects.Id, note: '專案 ID']
  QuestionTypeId int [not null, ref: > MT_QuestionTypes.Id, note: '題型 ID']
  QuestionCode nvarchar(30) [not null, unique, note: '試題系統編號']
  CreatorId int [not null, ref: > MT_Users.Id, note: '命題人 ID']
  Status tinyint [not null, default: 0, note: '試題狀態數值 (0~13，共14種流轉狀態)']
  Level tinyint [note: '等級 (1:基礎, 2:進階)']
  Difficulty tinyint [note: '難度感 (1:易, 2:中, 3:難)']
  Stem nvarchar(MAX) [note: '題幹 (HTML)']
  Analysis nvarchar(MAX) [note: '試題解析 (HTML)']
  CorrectAnswer nvarchar(10) [note: '正確答案']
  OptionA nvarchar(MAX) [note: '選項 A (HTML/圖)']
  OptionB nvarchar(MAX) [note: '選項 B (HTML/圖)']
  OptionC nvarchar(MAX) [note: '選項 C (HTML/圖)']
  OptionD nvarchar(MAX) [note: '選項 D (HTML/圖)']
  ArticleTitle nvarchar(200) [note: '長文/題組標題']
  ArticleContent nvarchar(MAX) [note: '文章/語音內容 (HTML)']
  AudioUrl nvarchar(500) [note: '音檔位址']
  GradingNote nvarchar(MAX) [note: '批閱說明']
  IsDeleted bit [not null, default: 0, note: '是否刪除']
  DeletedAt datetime2 [note: '刪除時間']
  CreatedAt datetime2 [not null, default: `SYSDATETIME()`, note: '命題時間']
  UpdatedAt datetime2 [not null, default: `SYSDATETIME()`, note: '最後更新時間']

  indexes {
    ProjectId
    Status
    CreatorId
  }
  note: '核心試題資料表，儲存所有類型的試題內容'
}

Table MT_QuestionAttributes {
  Id int [pk, increment, note: '唯一識別碼']
  QuestionId int [not null, ref: > MT_Questions.Id, note: '試題 ID']
  AttributeKey tinyint [not null, note: '屬性鍵 (0:主類, 1:次類, 2:文體, 3:素材來源)']
  AttributeValue nvarchar(200) [not null, note: '屬性值']

  note: '儲存試題的動態屬性（如主次類、文體、素材等）'
}

Table MT_SubQuestions {
  Id int [pk, increment, note: '唯一識別碼']
  ParentQuestionId int [not null, ref: > MT_Questions.Id, note: '母題 ID']
  SortOrder int [not null, default: 1, note: '排序']
  Stem nvarchar(MAX) [note: '子題題幹']
  CorrectAnswer nvarchar(10) [note: '子題正確答案']
  OptionA nvarchar(MAX) [note: '子題選項 A']
  OptionB nvarchar(MAX) [note: '子題選項 B']
  OptionC nvarchar(MAX) [note: '子題選項 C']
  OptionD nvarchar(MAX) [note: '子題選項 D']
  Analysis nvarchar(MAX) [note: '子題解析']
  CoreAbility nvarchar(100) [note: '核心能力']
  Indicator nvarchar(100) [note: '細目指標']
  FixedDifficulty tinyint [note: '固定難度 (1:易, 2:中, 3:難)']

  note: '題組型試題的子題目定義'
}

Table MT_QuestionHistoryLogs {
  Id int [pk, increment, note: '唯一識別碼']
  QuestionId int [not null, ref: > MT_Questions.Id, note: '試題 ID']
  UserId int [not null, ref: > MT_Users.Id, note: '操作人 ID']
  Action tinyint [not null, note: '操作動作數值 (1:建立, 2:編輯, 3:送審, 4:退回...)']
  Comment nvarchar(MAX) [note: '審查意見/備註']
  OldStatus tinyint [note: '原狀態碼數值']
  NewStatus tinyint [note: '新狀態碼數值']
  CreatedAt datetime2 [not null, default: `SYSDATETIME()`]

  note: '記錄試題的完整流轉歷程與各方審核意見'
}

Table MT_RevisionReplies {
  Id int [pk, increment, note: '唯一識別碼']
  QuestionId int [not null, ref: > MT_Questions.Id, note: '試題 ID']
  UserId int [not null, ref: > MT_Users.Id, note: '回覆人 ID']
  Stage tinyint [not null, note: '修題階段 (1:一審修題, 2:二審修題...)']
  Content nvarchar(MAX) [not null, note: '修題說明']
  CreatedAt datetime2 [not null, default: `SYSDATETIME()`]

  note: '命題教師對審核意見的回覆與修改說明'
}

// -----------------------------------------------------------------------------
// 6. 審題制度群組
// -----------------------------------------------------------------------------

Table MT_ReviewAssignments {
  Id int [pk, increment, note: '唯一識別碼']
  QuestionId int [not null, ref: > MT_Questions.Id, note: '試題 ID']
  ProjectId int [not null, ref: > MT_Projects.Id, note: '專案 ID']
  ReviewerId int [not null, ref: > MT_Users.Id, note: '審題人 ID']
  ReviewStage tinyint [not null, note: '審題階段 (1:一審, 2:二審, 3:總召)']
  ReviewStatus tinyint [not null, default: 0, note: '任務狀態 (0:待審, 1:審核中, 2:已完成)']
  Decision tinyint [note: '最終決策 (1:通過, 2:修正, 3:退回)']
  Comment nvarchar(MAX) [note: '審查意見']
  DecidedAt datetime2 [note: '決策時間']
  CreatedAt datetime2 [not null, default: `SYSDATETIME()`]

  indexes {
    (QuestionId, ReviewerId, ReviewStage) [unique]
  }
  note: '管理命題中的三審指派任務'
}

Table MT_ReviewReturnCounts {
  Id int [pk, increment, note: '唯一識別碼']
  QuestionId int [not null, ref: > MT_Questions.Id, note: '試題 ID']
  FinalReviewerId int [not null, ref: > MT_Users.Id, note: '總召 ID']
  ReturnCount int [not null, default: 0, note: '已退回次數']
  CanEditByReviewer bit [not null, default: 0, note: '解鎖總召自行修題']

  note: '追蹤試題在總召階段的退回次數，強制截止限制'
}

Table MT_SimilarityChecks {
  Id int [pk, increment]
  SourceQuestionId int [not null, ref: > MT_Questions.Id, note: '來源題目 ID']
  ComparedQuestionId int [not null, ref: > MT_Questions.Id, note: '比對目標 ID']
  SimilarityScore decimal(5,2) [not null, note: '相似度分數']
  Determination tinyint [not null, note: '查重結果判定 (1:安全, 2:相似度高, 3:確認重複)']
  CheckedBy int [ref: > MT_Users.Id]
  CheckedAt datetime2 [not null, default: `SYSDATETIME()`]

  note: '儲存試題相似度查重報告'
}

// -----------------------------------------------------------------------------
// 7. 非唯一索引
// -----------------------------------------------------------------------------
// CREATE INDEX IX_MT_Users_RoleId ON dbo.MT_Users(RoleId);
// CREATE INDEX IX_MT_Users_Status ON dbo.MT_Users(Status);
// CREATE INDEX IX_MT_Users_Email ON dbo.MT_Users(Email);
// CREATE INDEX IX_MT_Questions_ProjectId ON dbo.MT_Questions(ProjectId);
// CREATE INDEX IX_MT_Questions_Status ON dbo.MT_Questions(Status);
// CREATE INDEX IX_MT_Questions_CreatorId ON dbo.MT_Questions(CreatorId);
```
