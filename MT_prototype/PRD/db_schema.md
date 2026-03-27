```dbml
// CWT 命題工作平臺 — 統一資料庫架構 (DBML for dbdiagram.io)
// 版本: v1.1 (包含完整中文註解)
// 轉換日期: 2026-03-20

// -----------------------------------------------------------------------------
// 帳號與權限群組
// -----------------------------------------------------------------------------

Table Roles {
  Id int [pk, increment, note: '唯一識別碼']
  Code nvarchar(50) [not null, unique, note: '角色代碼 (ADMIN, TEACHER, etc)']
  Name nvarchar(50) [not null, unique, note: '角色顯示名稱']
  Category nvarchar(20) [not null, note: '角色分類 (internal: 內部, external: 外部)']
  Description nvarchar(500) [note: '角色功能描述']
  IsDefault bit [not null, default: 0, note: '是否為系統預設角色']
  CreatedAt datetime2 [not null, default: `now()`, note: '記錄建立時間']
  UpdatedAt datetime2 [not null, default: `now()`, note: '最後修改時間']

  note: '定義系統中的使用者職務與權限分類'
}

Table Users {
  Id int [pk, increment, note: '唯一識別碼']
  Username nvarchar(100) [not null, unique, note: '登入帳號']
  DisplayName nvarchar(50) [not null, note: '使用者姓名']
  Email nvarchar(200) [note: '電子信箱']
  PasswordHash nvarchar(500) [not null, note: '密碼雜湊值']
  RoleId int [not null, ref: > Roles.Id, note: '所屬角色 ID']
  Status nvarchar(20) [not null, default: 'active', note: '帳號狀態 (active/inactive)']
  CompanyTitle nvarchar(100) [note: '內部人員職稱']
  Note nvarchar(500) [note: '管理備註']
  IsFirstLogin bit [not null, default: 1, note: '是否首次登入']
  RememberToken nvarchar(500) [note: '記住我 Token']
  LastLoginAt datetime2 [note: '最後登入時間']
  CreatedAt datetime2 [not null, default: `now()`, note: '帳號建立時間']
  UpdatedAt datetime2 [not null, default: `now()`, note: '資料更新時間']

  indexes {
    RoleId
    Status
    Email
  }
  note: '儲存所有系統使用者，包含內部職員與外部教師'
}

Table RolePermissions {
  Id int [pk, increment, note: '唯一識別碼']
  RoleId int [not null, ref: > Roles.Id, note: '關聯角色 ID']
  ModuleKey nvarchar(50) [not null, note: '模組識別碼']
  IsEnabled bit [not null, default: 0, note: '是否啟用該模組']
  AnnouncementPerm nvarchar(10) [default: 'view', note: '公告權限 (view/edit)']
  CreatedAt datetime2 [not null, default: `now()`]
  UpdatedAt datetime2 [not null, default: `now()`]

  indexes {
    (RoleId, ModuleKey) [unique]
  }
  note: '定義各角色對不同功能模組的存取權限'
}

Table PasswordResetTokens {
  Id int [pk, increment, note: '唯一識別碼']
  UserId int [not null, ref: > Users.Id, note: '使用者 ID']
  Token nvarchar(500) [not null, unique, note: '重設識別碼']
  ExpiresAt datetime2 [not null, note: '截止時間']
  IsUsed bit [not null, default: 0, note: '是否已使用']
  CreatedAt datetime2 [not null, default: `now()`]
  note: '管理忘記密碼功能的驗證連結'
}

Table LoginLogs {
  Id int [pk, increment, note: '唯一識別碼']
  UserId int [ref: > Users.Id, note: '使用者 ID']
  Username nvarchar(100) [not null, note: '登入帳號']
  IsSuccess bit [not null, note: '是否成功']
  IpAddress nvarchar(50) [note: '客戶端 IP']
  UserAgent nvarchar(500) [note: '瀏覽器環境']
  FailReason nvarchar(200) [note: '失敗原因']
  CreatedAt datetime2 [not null, default: `now()`]
  note: '記錄所有登入嘗試，用於安全稽核'
}

// -----------------------------------------------------------------------------
// 教師人才庫
// -----------------------------------------------------------------------------

Table Teachers {
  Id int [pk, increment, note: '唯一識別碼']
  UserId int [not null, unique, ref: - Users.Id, note: '關聯帳號 ID (1:1)']
  TeacherCode nvarchar(10) [not null, unique, note: '教師編號 (如 T1001)']
  Gender nvarchar(5) [note: '性別']
  Phone nvarchar(20) [note: '聯絡電話']
  IdNumber nvarchar(200) [note: '身分證 (加密)']
  School nvarchar(100) [not null, note: '任教學校']
  Department nvarchar(50) [note: '系所']
  Title nvarchar(20) [note: '職稱']
  Expertise nvarchar(200) [note: '專長']
  TeachingYears int [note: '教學年資']
  Education nvarchar(10) [note: '學歷']
  Note nvarchar(500) [note: '人才庫備註']
  CreatedAt datetime2 [not null, default: `now()`, note: '匯入時間']
  UpdatedAt datetime2 [not null, default: `now()`, note: '更新時間']
  note: '教師人才庫，儲存命題與審題人員的詳細背景'
}

// -----------------------------------------------------------------------------
// 專案管理群組
// -----------------------------------------------------------------------------

Table Projects {
  Id int [pk, increment, note: '唯一識別碼']
  ProjectCode nvarchar(20) [not null, unique, note: '專案代碼']
  Name nvarchar(100) [not null, note: '專案名稱']
  Year int [not null, note: '專案年度']
  Semester nvarchar(20) [note: '學期']
  School nvarchar(100) [note: '合作學校']
  Status nvarchar(20) [not null, default: 'preparing', note: '專案狀態 (preparing/active/closed)']
  StartDate date [not null, note: '計畫開始日']
  EndDate date [not null, note: '計畫結束日']
  ClosedAt datetime2 [note: '實際結案時間']
  CreatedBy int [ref: > Users.Id, note: '建立人 ID']
  Description nvarchar(500) [note: '專案描述']
  CreatedAt datetime2 [not null, default: `now()`]
  UpdatedAt datetime2 [not null, default: `now()`]
  note: '管理命題梯次 (Project/Epoch) 的生命週期'
}

Table ProjectPhases {
  Id int [pk, increment, note: '唯一識別碼']
  ProjectId int [not null, ref: > Projects.Id, note: '專案 ID']
  PhaseCode nvarchar(30) [not null, note: '階段代碼']
  PhaseName nvarchar(50) [not null, note: '階段名稱']
  StartDate date [not null, note: '階段開始日']
  EndDate date [not null, note: '階段截止日']
  SortOrder int [not null, note: '排序順序']
  CreatedAt datetime2 [not null, default: `now()`]
  UpdatedAt datetime2 [not null, default: `now()`]

  indexes {
    (ProjectId, PhaseCode) [unique]
  }
  note: '定義專案內部的各個階段 (命題、三審等) 時間區間'
}

Table ProjectTargets {
  Id int [pk, increment, note: '唯一識別碼']
  ProjectId int [not null, ref: > Projects.Id, note: '專案 ID']
  QuestionTypeId int [not null, ref: > QuestionTypes.Id, note: '題型 ID']
  Level nvarchar(20) [not null, note: '等級']
  TargetCount int [not null, default: 0, note: '目標命題數']
  note: '各專案對各題型、等級的命題數量總體需求'
}

Table ProjectMembers {
  Id int [pk, increment, note: '唯一識別碼']
  ProjectId int [not null, ref: > Projects.Id, note: '專案 ID']
  UserId int [not null, ref: > Users.Id, note: '使用者 ID']
  JoinedAt    datetime2 [not null, default: `now()`, note: '加入專案時間']

  indexes {
    (ProjectId, UserId) [unique]
  }
  note: '關聯專案與參與的人員'
}

Table ProjectMemberRoles {
  Id int [pk, increment, note: '唯一識別碼']
  ProjectMemberId int [not null, ref: > ProjectMembers.Id, note: '成員 ID']
  RoleCode nvarchar(30) [not null, note: '在該專案的角色任務']
  note: '支援一人在同一專案中具備多重任務身分'
}

Table MemberQuotas {
  Id int [pk, increment, note: '唯一識別碼']
  ProjectMemberId int [not null, ref: > ProjectMembers.Id, note: '成員 ID']
  QuestionTypeId int [not null, ref: > QuestionTypes.Id, note: '題型 ID']
  Level nvarchar(20) [not null, note: '等級']
  QuotaCount int [not null, default: 0, note: '指派數']
  note: '指派給特定人員的命題配額數量'
}

// -----------------------------------------------------------------------------
// 題目與命題群組
// -----------------------------------------------------------------------------

Table QuestionTypes {
  Id int [pk, increment, note: '唯一識別碼']
  Code nvarchar(30) [not null, unique, note: '題型代碼']
  Name nvarchar(50) [not null, note: '題型名稱']
  Icon nvarchar(50) [note: '顯示圖示']
  SortOrder int [not null, default: 0, note: '排序次序']
  note: '定義 7 種試題類型'
}

Table Questions {
  Id int [pk, increment, note: '唯一識別碼']
  ProjectId int [not null, ref: > Projects.Id, note: '專案 ID']
  QuestionTypeId int [not null, ref: > QuestionTypes.Id, note: '題型 ID']
  QuestionCode nvarchar(30) [not null, unique, note: '試題系統編號']
  CreatorId int [not null, ref: > Users.Id, note: '命題人 ID']
  Status nvarchar(30) [not null, default: 'draft', note: '試題狀態 (共 14 種)']
  Level nvarchar(20) [note: '等級']
  Difficulty nvarchar(10) [note: '難度感']
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
  CreatedAt datetime2 [not null, default: `now()`, note: '命題時間']
  UpdatedAt datetime2 [not null, default: `now()`, note: '最後更新時間']

  indexes {
    ProjectId
    Status
    CreatorId
  }
  note: '核心試題資料表，儲存所有類型的試題內容'
}

Table QuestionAttributes {
  Id int [pk, increment, note: '唯一識別碼']
  QuestionId int [not null, ref: > Questions.Id, note: '試題 ID']
  AttributeKey nvarchar(50) [not null, note: '屬性鍵']
  AttributeValue nvarchar(200) [not null, note: '屬性值']
  note: '儲存試題的動態屬性（如主次類、文體、素材等）'
}

Table SubQuestions {
  Id int [pk, increment, note: '唯一識別碼']
  ParentQuestionId int [not null, ref: > Questions.Id, note: '母題 ID']
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
  FixedDifficulty nvarchar(20) [note: '固定難度']
  note: '題組型試題的子題目定義'
}

Table QuestionHistoryLogs {
  Id int [pk, increment, note: '唯一識別碼']
  QuestionId int [not null, ref: > Questions.Id, note: '試題 ID']
  UserId int [not null, ref: > Users.Id, note: '操作人 ID']
  Action nvarchar(50) [not null, note: '操作動作']
  Comment nvarchar(MAX) [note: '審查意見/備註']
  OldStatus nvarchar(30) [note: '原狀態碼']
  NewStatus nvarchar(30) [note: '新狀態碼']
  CreatedAt datetime2 [not null, default: `now()`]
  note: '記錄試題的完整流轉歷程與各方審核意見'
}

Table RevisionReplies {
  Id int [pk, increment, note: '唯一識別碼']
  QuestionId int [not null, ref: > Questions.Id, note: '試題 ID']
  UserId int [not null, ref: > Users.Id, note: '回覆人 ID']
  Stage nvarchar(20) [not null, note: '修題階段']
  Content nvarchar(MAX) [not null, note: '修題說明']
  CreatedAt datetime2 [not null, default: `now()`]
  note: '命題教師對審核意見的回覆與修改說明'
}

// -----------------------------------------------------------------------------
// 審題制度群組
// -----------------------------------------------------------------------------

Table ReviewAssignments {
  Id int [pk, increment, note: '唯一識別碼']
  QuestionId int [not null, ref: > Questions.Id, note: '試題 ID']
  ProjectId int [not null, ref: > Projects.Id, note: '專案 ID']
  ReviewerId int [not null, ref: > Users.Id, note: '審題人 ID']
  ReviewStage nvarchar(10) [not null, note: '審題階段']
  ReviewStatus nvarchar(10) [not null, default: 'pending', note: '任務狀態']
  Decision nvarchar(10) [note: '最終決策 (採用/退回等)']
  Comment nvarchar(MAX) [note: '審查意見']
  DecidedAt datetime2 [note: '決策時間']
  CreatedAt datetime2 [not null, default: `now()`]

  indexes {
    (QuestionId, ReviewerId, ReviewStage) [unique]
  }
  note: '管理命題中的三審指派任務'
}

Table ReviewReturnCounts {
  Id int [pk, increment, note: '唯一識別碼']
  QuestionId int [not null, ref: > Questions.Id, note: '試題 ID']
  FinalReviewerId int [not null, ref: > Users.Id, note: '總召 ID']
  ReturnCount int [not null, default: 0, note: '已退回次數']
  CanEditByReviewer bit [not null, default: 0, note: '解鎖總召自行修題']
  note: '追蹤試題在總召階段的退回次數，強制截止限制'
}

Table SimilarityChecks {
  Id int [pk, increment]
  SourceQuestionId int [not null, ref: > Questions.Id, note: '來源題目 ID']
  ComparedQuestionId int [not null, ref: > Questions.Id, note: '比對目標 ID']
  SimilarityScore decimal(5,2) [not null, note: '相似度分數']
  Determination nvarchar(20) [not null, note: '查重結果判定']
  CheckedBy int [ref: > Users.Id]
  CheckedAt datetime2 [not null, default: `now()`]
  note: '儲存 AI 或人工執行的試題相似度查重報告'
}

// -----------------------------------------------------------------------------
// 公告與手冊群組
// -----------------------------------------------------------------------------

Table Announcements {
  Id int [pk, increment, note: '唯一識別碼']
  Category nvarchar(20) [not null, note: '公告分類']
  Status nvarchar(20) [not null, default: 'draft', note: '發佈狀態']
  ProjectId int [ref: > Projects.Id, note: '專案 ID (空為全域)']
  PublishDate date [not null, note: '發佈日期']
  UnpublishDate date [note: '截止日期']
  IsPinned bit [not null, default: 0, note: '是否置頂']
  Title nvarchar(200) [not null, note: '標題']
  Content nvarchar(MAX) [not null, note: '內容 (HTML)']
  AuthorId int [not null, ref: > Users.Id, note: '發佈人 ID']
  CreatedAt datetime2 [not null, default: `now()`]
  UpdatedAt datetime2 [not null, default: `now()` ]
  note: '系統公告與最新消息管理'
}

Table UserGuideFiles {
  Id int [pk, increment, note: '唯一識別碼']
  FileName nvarchar(200) [not null, note: '檔名']
  FilePath nvarchar(500) [not null, note: '路徑']
  FileSize bigint [not null, note: '大小']
  UploadedBy int [not null, ref: > Users.Id, note: '上傳者 ID']
  IsActive bit [not null, default: 1, note: '可用狀態']
  note: '管理使用者操作手冊與相關 PDF 文件資源'
}

// -----------------------------------------------------------------------------
// 系統群組
// -----------------------------------------------------------------------------

Table Modules {
  Id int [pk, increment, note: '唯一識別碼']
  ModuleKey nvarchar(50) [not null, unique, note: '模組識別代碼']
  Name nvarchar(50) [not null, note: '模組各組稱']
  Icon nvarchar(50) [note: '選單圖示']
  PageUrl nvarchar(100) [note: '頁面路由路徑']
  SortOrder int [not null, default: 0]
  IsActive bit [not null, default: 1]
  note: '定義系統的主功能模組樹'
}

Table AuditLogs {
  Id int [pk, increment, note: '唯一識別碼']
  UserId int [ref: > Users.Id, note: '執行人 ID']
  Action nvarchar(100) [not null, note: '操作描述']
  TargetType nvarchar(50) [not null, note: '目標表類型']
  TargetId int [note: '目標資料主鍵']
  OldValue nvarchar(MAX) [note: '原始資料 (JSON)']
  NewValue nvarchar(MAX) [note: '新資料 (JSON)']
  IpAddress nvarchar(50) [note: '來源 IP']
  CreatedAt datetime2 [not null, default: `now()`]
  note: '系統層級稽核記錄，追蹤關鍵資料變動'
}
```