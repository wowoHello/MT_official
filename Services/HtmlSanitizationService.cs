using Ganss.Xss;
using MT.Models;

namespace MT.Services;

/// <summary>
/// 伺服器端 HTML 消毒（Stored XSS 防護）。
///
/// 設計：採「白名單」策略，只保留 Quill 編輯器（wwwroot/js/quill-interop.js）三套工具列
/// 實際會產出的標籤 / 屬性 / class / CSS，其餘（&lt;script&gt;、onerror、javascript: 等）一律移除。
/// 由 QuestionService / AnnouncementService / ReviewService / RevisionService 在「寫入 DB 前」呼叫，
/// 確保入庫內容乾淨；渲染端維持原樣輸出。
///
/// Allowlist 對齊清單（toolbar：font/size/color/align、bold/underline/double-underline/strike、
/// ordered/bullet list、indent、image）：
///   標籤  p br strong b u s em span ol ul li img
///   class ql-double-underline / ql-font-* / ql-size-* / ql-align-* / ql-indent-1..8
///   CSS   color（色彩選擇器產出 inline color）
///   圖片  僅同源相對路徑（uploads/...）；外部網域 / data: / javascript: 一律移除
///
/// 設定一次後不再變動，HtmlSanitizer 可重用且執行緒安全 → 以 Singleton 註冊。
/// </summary>
public interface IHtmlSanitizationService
{
    /// <summary>消毒富文本 HTML；null / 純空白原樣回傳（不產生差異）。</summary>
    string? Sanitize(string? html);
}

public sealed class HtmlSanitizationService : IHtmlSanitizationService
{
    private readonly HtmlSanitizer _sanitizer;

    public HtmlSanitizationService()
    {
        _sanitizer = new HtmlSanitizer();

        // 清空預設，改用嚴格白名單（只放 Quill 會吐的東西）
        _sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "strong", "b", "u", "s", "em", "span", "ol", "ul", "li", "img" })
            _sanitizer.AllowedTags.Add(tag);

        _sanitizer.AllowedAttributes.Clear();
        // class：對齊/縮排/字體/字級/雙底線；style：僅 color；img：src/alt/width/height；li：data-list（Quill 2.x 清單）
        foreach (var attr in new[] { "class", "style", "src", "alt", "width", "height", "data-list" })
            _sanitizer.AllowedAttributes.Add(attr);

        _sanitizer.AllowedCssProperties.Clear();
        _sanitizer.AllowedCssProperties.Add("color");

        // class 僅允許 Quill 既定的 ql-* （其餘 class 一律剝除）
        _sanitizer.AllowedClasses.Clear();
        foreach (var cls in new[]
        {
            "ql-double-underline",
            "ql-font-dfkai-sb", "ql-font-times-new-roman",
            "ql-size-small", "ql-size-large",
            "ql-align-center", "ql-align-right", "ql-align-justify",
            "ql-indent-1", "ql-indent-2", "ql-indent-3", "ql-indent-4",
            "ql-indent-5", "ql-indent-6", "ql-indent-7", "ql-indent-8",
        })
            _sanitizer.AllowedClasses.Add(cls);

        // 僅允許 http/https；data: / javascript: / vbscript: 等一律擋
        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");

        _sanitizer.AllowDataAttributes = false;

        // Q4：圖片只准同源相對路徑（uploads/...）。任何「絕對 URL」（含外部 https、data:）一律移除，
        // 防追蹤像素 / SSRF；本站圖片本就走 /api/upload 回相對路徑。
        _sanitizer.FilterUrl += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.OriginalUrl)
                && Uri.TryCreate(e.OriginalUrl, UriKind.Absolute, out Uri? _))
            {
                e.SanitizedUrl = null;
            }
        };
    }

    public string? Sanitize(string? html)
        => string.IsNullOrWhiteSpace(html) ? html : _sanitizer.Sanitize(html);
}

/// <summary>
/// 消毒 <see cref="QuestionFormData"/> 全部富文本欄位（母題 + 三型子題）。
/// 供 QuestionService（命題 CRUD / 修題）與 ReviewService（總召代修題）共用，避免兩處各寫一份。
/// Answer（A/B/C/D）、AudioUrl（網址）非富文本，不消毒。
/// </summary>
public static class QuestionFormSanitizer
{
    public static void Sanitize(QuestionFormData f, IHtmlSanitizationService s)
    {
        f.Stem           = s.Sanitize(f.Stem) ?? "";
        f.ArticleTitle   = s.Sanitize(f.ArticleTitle) ?? "";
        f.ArticleContent = s.Sanitize(f.ArticleContent) ?? "";
        f.Analysis       = s.Sanitize(f.Analysis) ?? "";
        f.GradingNote    = s.Sanitize(f.GradingNote) ?? "";
        for (int i = 0; i < f.Options.Length; i++)
            f.Options[i] = s.Sanitize(f.Options[i]) ?? "";

        foreach (var sub in f.ReadSubQuestions)
        {
            sub.Stem     = s.Sanitize(sub.Stem) ?? "";
            sub.Analysis = s.Sanitize(sub.Analysis) ?? "";
            for (int i = 0; i < sub.Options.Length; i++)
                sub.Options[i] = s.Sanitize(sub.Options[i]) ?? "";
        }
        foreach (var sub in f.ShortSubQuestions)
        {
            sub.Stem     = s.Sanitize(sub.Stem) ?? "";
            sub.Analysis = s.Sanitize(sub.Analysis) ?? "";
        }
        foreach (var sub in f.ListenGroupSubQuestions)
        {
            sub.Stem     = s.Sanitize(sub.Stem) ?? "";
            sub.Analysis = s.Sanitize(sub.Analysis) ?? "";
            for (int i = 0; i < sub.Options.Length; i++)
                sub.Options[i] = s.Sanitize(sub.Options[i]) ?? "";
        }
    }
}
