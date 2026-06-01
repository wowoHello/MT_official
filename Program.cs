using MT.Components;
using MT.Endpoints;
using MT.Hubs;
using MT.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// =========================================================
// UI 與即時互動
// =========================================================
// Blazor Server 互動式元件
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// SignalR：提供應用程式層級即時同步
builder.Services.AddSignalR();

// =========================================================
// 驗證與授權
// =========================================================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // 未登入時導回登入頁
        options.LoginPath = "/login";

        // 登出後回登入頁
        options.LogoutPath = "/login";

        // Session Cookie：IsPersistent=false 由 AuthService 設定，關瀏覽器即失效需重登
        // ExpireTimeSpan 24 小時作安全網（萬一 cookie 外洩可限縮危害），有活動就滑動延長
        options.ExpireTimeSpan = TimeSpan.FromDays(1);
        options.SlidingExpiration = true;

        // 不需要 ReturnUrl 跳回機制（登入成功一律由 /auth/login 端點導去首頁）
        // 覆寫預設行為，剝掉 query string，讓網址保持乾淨 /login
        options.Events.OnRedirectToLogin = context =>
        {
            var loginPath = context.Request.PathBase + context.Options.LoginPath;
            context.Response.Redirect(loginPath);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

// 讓 Razor 元件可透過 CascadingParameter 取得 AuthenticationState
builder.Services.AddCascadingAuthenticationState();

// 供需要存取 HttpContext 的服務或流程使用
builder.Services.AddHttpContextAccessor();

// =========================================================
// 應用程式服務
// =========================================================
// 登入頁 Canvas 驗證碼產生與比對（Login.razor）
builder.Services.AddScoped<ICaptchaService, CaptchaService>();

// Dapper 資料庫連線工廠，所有 Service 透過此取得 SqlConnection
builder.Services.AddScoped<IDatabaseService, DatabaseService>();

// 登入驗證、Cookie 寫入、密碼雜湊、稽核紀錄（Login.razor、/auth/login、/auth/logout）
builder.Services.AddScoped<IAuthService, AuthService>();

// 忘記密碼寄送驗證信（由 PasswordResetService 內部呼叫）
builder.Services.AddScoped<IEmailService, EmailService>();

// 忘記密碼 Token 管理、首次登入強制改密碼（Login.razor、ResetPassword.razor、FirstLoginPassword.razor）
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();

// 命題專案梯次 CRUD、可見梯次查詢（Projects.razor、MainLayout.razor）
builder.Services.AddScoped<IProjectService, ProjectService>();

// 帳號管理、角色權限 CRUD、功能模組權限查詢（Roles.razor、MainLayout.razor）
builder.Services.AddScoped<IRoleService, RoleService>();

// 系統公告 CRUD、自動下架、統計卡片（Announcements.razor、Home.razor）
builder.Services.AddScoped<IAnnouncementService, AnnouncementService>();

// 教師人才庫 CRUD、命題/審題歷程查詢、梯次指派（Teachers.razor）
builder.Services.AddScoped<ITeacherService, TeacherService>();

// 命題任務 CRUD、配額進度、當前階段偵測（CwtList.razor）
builder.Services.AddScoped<IQuestionService, QuestionService>();

// 審題任務列表、Modal 資料、意見儲存、決策提交（Reviews.razor）
builder.Services.AddScoped<IReviewService, ReviewService>();

// 審題劃記評語（inline annotation）— 由 ReviewModal / RevisionSlideOver 共用
builder.Services.AddScoped<IAnnotationService, AnnotationService>();

// 命題總覽列表彙整與詳情（Overview.razor，內部依賴 IQuestionService）
builder.Services.AddScoped<IOverviewService, OverviewService>();

// 首頁公告載入與急件警示（Home.razor）
builder.Services.AddScoped<IHomeService, HomeService>();

// 命題儀表板 KPI 統計（Dashboard.razor）
builder.Services.AddScoped<IDashboardService, DashboardService>();

// 系統活動記錄（登入/人員/專案/公告）統一查詢服務（SystemLogs.razor）
builder.Services.AddScoped<ISystemLogService, SystemLogService>();

// 使用說明手冊（以頁面為區分）上傳/預覽（Announcements 管理、Home/Login 預覽）
builder.Services.AddScoped<IUserGuideService, UserGuideService>();

// 階段轉換協調器：CwtList / Reviews / OverviewService 共用入口，60 秒去重
// Singleton + IMemoryCache 跨 user 跨 tab 共享狀態，避免雜訊 SQL
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IPhaseTransitionCoordinator, PhaseTransitionCoordinator>();

// 7 種題型字典：啟動時載入記憶體，全站 SQL 不再 JOIN MT_QuestionTypes（靜態小表)
builder.Services.AddSingleton<IQuestionTypeCatalog, QuestionTypeCatalog>();

// 使用者「effective 角色 + 模組權限」短 TTL Cache（30 秒）
// 服務的場景：MainLayout / Home / Announcements 編輯 / 任何權限快速判斷
builder.Services.AddScoped<IMembershipService, MembershipService>();

// 聘書 Canvas 繪製 + zip 打包下載
builder.Services.AddScoped<IAppointmentService, AppointmentService>();

// 相似度分析：4-gram Jaccard 計算引擎 + MT_SimilarityChecks 讀寫
// 服務的場景：CwtList 命題送審自動寫入 / Modal【🔍 比對相似題】鈕 /
//            SimilarityAnalysis.razor 管理員批次掃描 / Dashboard KPI 卡
builder.Services.AddScoped<ISimilarityService, SimilarityService>();

// 審後修訂：管理員（計畫主持人/總召集人/系統管理員）在三審結束後再編輯題目並調整決策
// 服務的場景：Overview.razor 詳情頁 + 列表「修訂」按鈕 / RevisionHistory.razor 列表
builder.Services.AddScoped<IRevisionService, RevisionService>();

var app = builder.Build();

// 預先載入題型字典（fail-fast：DB 連不上就讓站台不啟動，避免帶空資料上線）
await app.Services.GetRequiredService<IQuestionTypeCatalog>().ReloadAsync();

// =========================================================
// HTTP Pipeline
// =========================================================
// 從設定檔讀取 PathBase（本機不設 = "/"，IIS 子應用程式設 "/MT"）
var pathBase = app.Configuration["PathBase"];
if (!string.IsNullOrEmpty(pathBase))
{
    app.UsePathBase(pathBase);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// 找不到頁面時導到自訂 NotFound 頁
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// =========================================================
// UI / 靜態資源 / 即時通道
// =========================================================
// UseStaticFiles：服務 wwwroot/ 下「runtime 寫入」的檔案
//   - wwwroot/uploads/       Quill 編輯器附圖、聽力音檔（POST /api/upload 寫入後立即可讀）
//   - wwwroot/files/         聘書 jpg（POST /api/appointment-cert/upload 寫入後立即可讀）
// MapStaticAssets 只認發佈期就存在的檔案（含 fingerprint / Brotli 優化），
// 不會服務 runtime 上傳檔；兩者需並存。
app.UseStaticFiles();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// 命題專案梯次即時同步 Hub
app.MapHub<ProjectsHub>("/hubs/projects");

// =========================================================
// HTTP Endpoints（詳見 Endpoints/ 各檔案）
// =========================================================
app.MapAuthEndpoints();              // /auth/login、/auth/logout
app.MapUploadEndpoints();            // /api/upload（Quill）、/api/upload-audio（聽力）
app.MapAppointmentCertEndpoints();   // /api/appointment-cert/upload、.../zip/{projectId}

app.Run();
