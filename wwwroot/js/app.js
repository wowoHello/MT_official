/**
 * app.js — CWT 命題工作平臺 共用 JS Interop
 * ────────────────────────────────────────────────────────────────
 * 集中管理所有非 Quill 的瀏覽器端 helper，避免 App.razor 一長串 script tag。
 * Quill 體系（quill-interop.js）與 ApexCharts 體系（apex-interop.js）
 * 因為與第三方函式庫耦合且體積較大，仍維持獨立檔案。
 *
 * 目錄：
 *   1. SweetAlert2 對話框        — swalInterop / swalConfirm / swalToast
 *   2. 登入與身分                 — loginInterop
 *   3. 表單驗證 UI                — scrollToFirstInvalid
 *   4. 純文字輸入助手             — TextareaHelper
 *   5. 檔案上傳                   — ImageUpload / AudioUpload
 *   6. 聘書 Canvas 繪製           — AppointmentCert
 *   7. 字體縮放控制器             — FontController
 */


// ============================================================
//  1. SweetAlert2 對話框
//     兩套 API 並存，呼叫端各取所需：
//       - swalInterop.fire / .confirm / .fireToast：泛用 swal 包裝（MainLayout、多數頁面）
//       - swalConfirm / swalToast：扁平命名，Roles、Teachers、Announcements 使用
// ============================================================

window.swalInterop = {
    /** 直接呼叫 Swal.fire 並回傳完整 result 物件 */
    fire: async (options) => {
        const result = await Swal.fire(options);
        return result;
    },
    /** 確認彈窗：回傳是否按下「確認」 */
    confirm: async (options) => {
        const result = await Swal.fire(options);
        return result.isConfirmed;
    },
    /** 全螢幕 Toast 風格通知 */
    fireToast: async (icon, title, text, timer) => {
        await Swal.fire({
            icon: icon,
            title: title,
            text: text,
            timer: timer || 2000,
            showConfirmButton: false,
            backdrop: 'rgba(0,0,0,0.4)'
        });
    }
};

/** 確認彈窗（回傳布林）— 與 swalInterop.confirm 等效，命名更直白 */
window.swalConfirm = async (options) => {
    const result = await Swal.fire(options);
    return result.isConfirmed === true;
};

/** 右上角 Toast（無遮罩、不阻斷操作）— 用於儲存成功、刪除成功等輕量通知 */
window.swalToast = (icon, title, timer) => {
    Swal.fire({
        toast: true,
        position: 'top-end',
        icon: icon,
        title: title,
        timer: timer || 2000,
        timerProgressBar: true,
        showConfirmButton: false,
        didOpen: (el) => {
            el.addEventListener('mouseenter', Swal.stopTimer);
            el.addEventListener('mouseleave', Swal.resumeTimer);
        }
    });
};


// ============================================================
//  1b. MT Toast — 莫蘭迪風格輕量通知（取代 swal 蓋板）
//      右上滑入、自動 3.5s 消失、hover 暫停、可堆疊、無遮罩、不偷焦點
//      呼叫：mtToast.show({ type, title, subtitle, duration })
//      type: 'success' | 'info' | 'warning' | 'error'
//      duration: 毫秒；0 = 不自動關閉（須手動按 ×）
// ============================================================

window.mtToast = (() => {
    let containerEl = null;
    let stylesInjected = false;

    // 莫蘭迪語意配色
    const PALETTE = {
        success: '#8EAB94',  // 鼠尾草綠
        info:    '#6B8EAD',  // 灰藍
        warning: '#D98A6C',  // 溫暖赤陶
        error:   '#B45454'   // 低飽和深紅
    };

    // 內嵌 SVG 圖示（Heroicons outline 風）
    const ICONS = {
        success: '<svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 10.5l3 3 7-7"/></svg>',
        info:    '<svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="10" cy="10" r="7.5" fill="none"/><path d="M10 6.5v.01M10 9v5"/></svg>',
        warning: '<svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10 3l8 14H2L10 3zM10 8v3M10 13.5v.01"/></svg>',
        error:   '<svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="10" cy="10" r="7.5" fill="none"/><path d="M7 7l6 6M13 7l-6 6"/></svg>'
    };

    /** 一次性注入 CSS（首次 show 時觸發） */
    const injectStyles = () => {
        if (stylesInjected) return;
        stylesInjected = true;
        const style = document.createElement('style');
        style.textContent = `
            .mt-toast-container { position: fixed; top: 1rem; right: 1rem; z-index: 9999;
                display: flex; flex-direction: column; gap: .5rem; pointer-events: none; }
            .mt-toast { pointer-events: auto; display: flex; width: 22rem; max-width: calc(100vw - 2rem);
                background: #FBF9F6; border: 1px solid rgba(231,229,228,.6); border-radius: .75rem;
                box-shadow: 0 10px 25px -5px rgba(55,65,81,.12), 0 4px 10px -3px rgba(55,65,81,.08);
                overflow: hidden; transform: translateX(20px); opacity: 0;
                transition: transform 250ms cubic-bezier(.22,1,.36,1), opacity 250ms ease-out; }
            .mt-toast.mt-toast-show { transform: translateX(0); opacity: 1; }
            .mt-toast.mt-toast-hide { transform: translateX(8px); opacity: 0;
                transition: transform 180ms ease-in, opacity 180ms ease-in; }
            .mt-toast-bar { width: 4px; align-self: stretch; flex-shrink: 0; }
            .mt-toast-body { flex: 1; padding: .75rem 1rem; display: flex; align-items: flex-start;
                gap: .75rem; min-width: 0; }
            .mt-toast-icon { width: 1.25rem; height: 1.25rem; margin-top: .125rem; flex-shrink: 0; }
            .mt-toast-text { flex: 1; min-width: 0; }
            .mt-toast-title { font-size: .875rem; font-weight: 600; color: #374151; line-height: 1.375; }
            .mt-toast-subtitle { font-size: .75rem; color: #6B7280; line-height: 1.5;
                margin-top: .125rem; word-break: break-word; }
            .mt-toast-close { flex-shrink: 0; width: 1.75rem; height: 1.75rem;
                margin: -.375rem -.375rem -.375rem 0; padding: 0; background: transparent; border: 0;
                border-radius: .375rem; cursor: pointer; color: #9CA3AF; display: inline-flex;
                align-items: center; justify-content: center;
                transition: background 150ms ease-out, color 150ms ease-out; }
            .mt-toast-close:hover { background: rgba(120,113,108,.08); color: #374151; }
            .mt-toast-close:focus-visible { outline: 2px solid #6B8EAD; outline-offset: 1px; }
            @media (prefers-reduced-motion: reduce) {
                .mt-toast, .mt-toast.mt-toast-hide { transition: opacity 100ms ease-out !important;
                    transform: none !important; }
            }
        `;
        document.head.appendChild(style);
    };

    /** 取得（或建立）右上角的 toast 容器 */
    const ensureContainer = () => {
        if (containerEl && document.body.contains(containerEl)) return containerEl;
        containerEl = document.createElement('div');
        containerEl.className = 'mt-toast-container';
        containerEl.setAttribute('aria-live', 'polite');   // 不偷焦點，僅報讀
        containerEl.setAttribute('aria-atomic', 'false');
        document.body.appendChild(containerEl);
        return containerEl;
    };

    /** XSS 防護：把 < > & " ' 轉義 */
    const escapeHtml = (str) => str == null ? '' : String(str).replace(/[&<>"']/g, ch => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[ch]));

    return {
        show(opts) {
            opts = opts || {};
            injectStyles();
            const container = ensureContainer();

            const type     = PALETTE[opts.type] ? opts.type : 'info';
            const color    = PALETTE[type];
            const iconSvg  = ICONS[type];
            const duration = (opts.duration != null) ? Number(opts.duration) : 3500;
            const title    = escapeHtml(opts.title || '');
            const subtitle = opts.subtitle ? escapeHtml(opts.subtitle) : '';

            const toast = document.createElement('div');
            toast.className = 'mt-toast';
            toast.setAttribute('role', 'status');
            toast.innerHTML = `
                <div class="mt-toast-bar" style="background:${color}"></div>
                <div class="mt-toast-body">
                    <span class="mt-toast-icon" style="color:${color}" aria-hidden="true">${iconSvg}</span>
                    <div class="mt-toast-text">
                        <div class="mt-toast-title">${title}</div>
                        ${subtitle ? `<div class="mt-toast-subtitle">${subtitle}</div>` : ''}
                    </div>
                    <button type="button" class="mt-toast-close" aria-label="關閉通知">
                        <svg width="14" height="14" viewBox="0 0 14 14" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round"><path d="M2.5 2.5L11.5 11.5M11.5 2.5L2.5 11.5"/></svg>
                    </button>
                </div>
            `;
            container.appendChild(toast);

            // 雙 RAF 確保 transition 觸發（避免初始 class 與 show class 在同一 frame 套用而失效）
            requestAnimationFrame(() => {
                requestAnimationFrame(() => toast.classList.add('mt-toast-show'));
            });

            let dismissTimer = null;
            const dismiss = () => {
                if (toast.dataset.dismissed) return;
                toast.dataset.dismissed = '1';
                clearTimeout(dismissTimer);
                toast.classList.remove('mt-toast-show');
                toast.classList.add('mt-toast-hide');
                setTimeout(() => toast.remove(), 220);
            };

            const startTimer = () => {
                if (duration > 0) dismissTimer = setTimeout(dismiss, duration);
            };
            startTimer();

            // hover 暫停倒數（年長者讀字慢時更友善）
            toast.addEventListener('mouseenter', () => clearTimeout(dismissTimer));
            toast.addEventListener('mouseleave', () => { if (!toast.dataset.dismissed) startTimer(); });
            toast.querySelector('.mt-toast-close').addEventListener('click', dismiss);
        }
    };
})();


// ============================================================
//  2. 登入與身分
// ============================================================

window.loginInterop = {
    /** 取得瀏覽器 User-Agent（記錄登入裝置時用） */
    getUserAgent: () => navigator.userAgent || ""
};


// ============================================================
//  3. 表單驗證 UI
//     將第一個錯誤欄位捲入視野；fieldKey 對應該欄位最外層 div 的 data-field 屬性
// ============================================================

window.scrollToFirstInvalid = (fieldKey) => {
    if (!fieldKey) return false;
    // 用 CSS escape 避免 fieldKey 含特殊字元時 selector 失效
    const safe = (window.CSS && CSS.escape) ? CSS.escape(fieldKey) : fieldKey;
    const el = document.querySelector(`[data-field="${safe}"]`);
    if (!el) return false;
    // smooth + center：使用者立即看到該欄位的紅框包覆
    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    return true;
};


// ============================================================
//  4. 純文字輸入助手
//     用於審題意見等只需純文字輸入但仍要支援罐頭訊息插入的場景
// ============================================================

window.TextareaHelper = {
    /**
     * 在指定 textarea 的游標位置插入文字（取代當前選取範圍若有），
     * 回傳插入後的新值，交由 Blazor 端自行決定如何同步狀態。
     */
    insertAtCursor(textareaId, text) {
        const ta = document.getElementById(textareaId);
        if (!ta || !text) return null;

        const start = ta.selectionStart ?? ta.value.length;
        const end   = ta.selectionEnd   ?? ta.value.length;
        const before = ta.value.slice(0, start);
        const after  = ta.value.slice(end);

        // 自動換行：若插入點不是行首且不是空白，前面加 \n 避免黏在前文
        const needsLeadingBreak = before.length > 0 && !before.endsWith('\n');
        const insertText = needsLeadingBreak ? '\n' + text : text;

        const nextValue = before + insertText + after;
        ta.value = nextValue;
        const newPos = start + insertText.length;
        ta.setSelectionRange(newPos, newPos);
        ta.focus();

        return nextValue;
    }
};


// ============================================================
//  5. 檔案上傳
//     兩個 helper 同模式（Promise + fetch），避開 SignalR 訊息上限。
//     成功 resolve(url)、使用者取消 resolve(null)、失敗 reject(訊息字串)。
//     失敗特意 reject 純字串（非 Error 物件），Blazor JSException.Message 才不會
//     混入 JS stack trace，UI 顯示乾淨。
// ============================================================

/** 命題表單欄位附圖上傳（5MB / PNG/JPEG/GIF/WebP） */
window.ImageUpload = {
    pick: () => new Promise((resolve, reject) => {
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = 'image/png,image/jpeg,image/gif,image/webp';

        input.onchange = async () => {
            const file = input.files?.[0];
            if (!file) {
                resolve(null);
                return;
            }
            if (file.size > 5 * 1024 * 1024) {
                reject('圖片大小不可超過 5MB');
                return;
            }

            const fd = new FormData();
            fd.append('file', file);
            try {
                // 相對路徑（無開頭 '/'）以支援 IIS 子應用程式（PathBase=/MT），由 <base href> 自動補前綴
                const res = await fetch('api/upload', { method: 'POST', body: fd });
                const data = await res.json();
                if (!res.ok) {
                    reject(data.error || '圖片上傳失敗');
                    return;
                }
                resolve(data.url);
            } catch {
                reject('圖片上傳失敗，請稍後再試');
            }
        };

        input.oncancel = () => resolve(null);
        input.click();
    })
};

/** 聽力題音檔上傳（10MB / MP3/WAV/OGG/M4A） */
window.AudioUpload = {
    pick: () => new Promise((resolve, reject) => {
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = '.mp3,.wav,.ogg,.m4a,audio/mpeg,audio/wav,audio/ogg,audio/mp4,audio/x-m4a';

        input.onchange = async () => {
            const file = input.files?.[0];
            if (!file) {
                resolve(null);
                return;
            }
            if (file.size > 10 * 1024 * 1024) {
                reject('音檔大小不可超過 10MB');
                return;
            }

            const fd = new FormData();
            fd.append('file', file);
            try {
                // 相對路徑（無開頭 '/'）以支援 IIS 子應用程式（PathBase=/MT），由 <base href> 自動補前綴
                const res = await fetch('api/upload-audio', { method: 'POST', body: fd });
                const data = await res.json();
                if (!res.ok) {
                    reject(data.error || '音檔上傳失敗');
                    return;
                }
                resolve(data.url);
            } catch {
                reject('音檔上傳失敗，請稍後再試');
            }
        };

        input.oncancel = () => resolve(null);
        input.click();
    })
};


// ============================================================
//  6. 聘書 Canvas 繪製
//     由 Projects / Teachers 儲存後或進頁面時呼叫 drawAndUpload(drafts, pathBase)。
//     - 載入 wwwroot/img/appointment.png 為背景（首次載入後快取 Image 物件）
//     - 對每筆 draft 用 Canvas 疊字 → toBlob → POST /api/appointment-cert/upload
//     - 多筆時顯示 SweetAlert 進度（silent=true 時靜默補畫）
//
//     座標常數（FIELD_POSITIONS）為第一版「合理估計值」，請依實際 appointment.png
//     效果用 user 提供的測量結果調整。
// ============================================================

window.AppointmentCert = (() => {
    // 8 個欄位座標（標楷體；座標為 px，原點左上）
    // appointment.png 實際尺寸 1055×1491；座標需依實圖效果再校
    const FIELD_POSITIONS = {
        certNumber: { x: 680, y: 700,  font: '28px 標楷體', align: 'center' }, // 右上小框內置中
        school:     { x: 208, y: 832,  font: '50px 標楷體', align: 'left'   }, // 茲敦聘下方
        nameTitle:  { x: 590, y: 832,  font: '50px 標楷體', align: 'left'   }, // 學校右側
        roleName:   { x: 685, y: 898,  font: '44px 標楷體', align: 'left'   }, // 「中文能力測驗中心」後
        period:     { x: 340, y: 965, font: '36px 標楷體', align: 'left'   }, // 「聘期自」後（縮小避免超寬）
        yearROC:    { x: 510, y: 1380, font: '44px 標楷體', align: 'center' }, // 年
        month:      { x: 650, y: 1380, font: '44px 標楷體', align: 'center' }, // 月
        day:        { x: 780, y: 1380, font: '44px 標楷體', align: 'center' }  // 日
    };

    let cachedImage = null;

    const loadImage = (src) => new Promise((resolve, reject) => {
        if (cachedImage) return resolve(cachedImage);
        const img = new Image();
        img.onload = () => { cachedImage = img; resolve(img); };
        img.onerror = () => reject(new Error('appointment.png 載入失敗'));
        img.src = src;
    });

    const drawField = (ctx, text, pos) => {
        if (text === null || text === undefined || text === '') return;
        ctx.font = pos.font;
        ctx.textAlign = pos.align;
        ctx.fillStyle = 'black';
        ctx.fillText(String(text), pos.x, pos.y);
    };

    /**
     * 分散對齊繪製：第一字頂左邊（pos.x）、最後字頂右邊（pos.x + totalWidth）、
     * 中間字元等距均分。常見於行政文件對齊到欄寬。
     *
     * 若文字過長（即使 step-down 到 minSize 仍超出 totalWidth），會接受 overflow 但不壓字。
     */
    const drawFieldJustified = (ctx, text, pos, totalWidth, minSize) => {
        if (text === null || text === undefined || text === '') return;
        const text2 = String(text);
        const chars = [...text2]; // 用 spread 支援 surrogate pair（保險）
        if (chars.length <= 1) return drawField(ctx, text2, pos);

        const fontMatch = pos.font.match(/(\d+)px\s+(.+)/);
        if (!fontMatch) return drawField(ctx, text2, pos);

        let size = parseInt(fontMatch[1], 10);
        const family = fontMatch[2];
        const minS = minSize || 24;

        // 先 step-down 字級嘗試 fit；觸底仍超出時接受 overflow（不橫向壓字）
        ctx.font = `${size}px ${family}`;
        while (size > minS) {
            const total = chars.reduce((sum, c) => sum + ctx.measureText(c).width, 0);
            if (total <= totalWidth) break;
            size -= 2;
            ctx.font = `${size}px ${family}`;
        }

        // 最終字級下重量各字寬度
        const widths = chars.map(c => ctx.measureText(c).width);

        // CJK 排版慣例：等距「字元中心」+ 字元置中於自己中心點
        // 第一字中心位於 pos.x + widths[0]/2、末字中心位於 pos.x + totalWidth - widths[last]/2，
        // 中間字元中心線性內插。半形（數字）與全形（漢字）混排時視覺節奏仍對齊。
        const firstCenter = pos.x + widths[0] / 2;
        const lastCenter = pos.x + totalWidth - widths[chars.length - 1] / 2;
        const cellSpacing = (lastCenter - firstCenter) / (chars.length - 1);

        ctx.textAlign = 'left';
        ctx.fillStyle = 'black';
        for (let i = 0; i < chars.length; i++) {
            const center = firstCenter + i * cellSpacing;
            ctx.fillText(chars[i], center - widths[i] / 2, pos.y);
        }
    };

    /**
     * 自動縮字級繪製：先用 pos.font 量字寬，若超過 maxWidth 就 step-down 2px 直到 fit 或觸底 minSize。
     * 不會壓扁字體（不用 ctx.fillText 的第 4 參數 maxWidth，避免中文橫向擠壓變醜）。
     */
    const drawFieldAutoFit = (ctx, text, pos, maxWidth, minSize) => {
        if (text === null || text === undefined || text === '') return;
        const text2 = String(text);
        const fontMatch = pos.font.match(/(\d+)px\s+(.+)/);
        if (!fontMatch) return drawField(ctx, text2, pos);

        let size = parseInt(fontMatch[1], 10);
        const family = fontMatch[2];
        ctx.font = `${size}px ${family}`;
        while (ctx.measureText(text2).width > maxWidth && size > minSize) {
            size -= 2;
            ctx.font = `${size}px ${family}`;
        }
        ctx.textAlign = pos.align;
        ctx.fillStyle = 'black';
        ctx.fillText(text2, pos.x, pos.y);
    };

    const drawOne = (img, draft) => new Promise((resolve, reject) => {
        const canvas = document.createElement('canvas');
        canvas.width = img.naturalWidth;
        canvas.height = img.naturalHeight;
        const ctx = canvas.getContext('2d');
        ctx.drawImage(img, 0, 0);

        // ① 字號長度固定，不需 autoFit
        drawField(ctx, draft.certNumberText, FIELD_POSITIONS.certNumber);

        // ② 學校：用 nameTitle.x - school.x - 20 作為可用寬度，過長自動縮字級
        const schoolMaxW = FIELD_POSITIONS.nameTitle.x - FIELD_POSITIONS.school.x - 20;
        drawFieldAutoFit(ctx, draft.school, FIELD_POSITIONS.school, schoolMaxW, 28);

        // ③ 姓名+職稱：右側到畫布邊緣前留 30px 邊距
        const nameTitleMaxW = canvas.width - FIELD_POSITIONS.nameTitle.x - 30;
        drawFieldAutoFit(ctx, `${draft.displayName} ${draft.title}`.trim(), FIELD_POSITIONS.nameTitle, nameTitleMaxW, 28);

        // ④ 身份：右側到畫布邊緣前留 30px 邊距（極端「教育部國民及學前教育署中部辦公室專員」也能容身）
        const roleMaxW = canvas.width - FIELD_POSITIONS.roleName.x - 30;
        drawFieldAutoFit(ctx, `${draft.roleName}，`, FIELD_POSITIONS.roleName, roleMaxW, 28);

        // ⑤ 聘期：左對齊、字元自然緊鄰；過長時自動縮字級
        const periodMaxW = canvas.width - FIELD_POSITIONS.period.x - 30;
        drawFieldAutoFit(ctx, draft.periodText, FIELD_POSITIONS.period, periodMaxW, 28);

        // ⑥⑦⑧ 日期數字最多 3 位，不需 autoFit
        drawField(ctx, draft.issuedYearROC, FIELD_POSITIONS.yearROC);
        drawField(ctx, draft.issuedMonth, FIELD_POSITIONS.month);
        drawField(ctx, draft.issuedDay, FIELD_POSITIONS.day);

        canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error('canvas.toBlob 失敗')), 'image/jpeg', 0.92);
    });

    return {
        /**
         * @param {Array} drafts        AppointmentDraftDto 陣列（PascalCase 屬性由 Blazor 序列化成 camelCase）
         * @param {string} _pathBase    保留參數位但不再使用（改靠 base href 自動解析相對路徑，支援本機與 IIS 子應用程式）
         * @param {boolean} silent      true = 不顯示 SweetAlert 進度（OnInitializedAsync 補畫場景）
         */
        drawAndUpload: async (drafts, _pathBase, silent) => {
            if (!Array.isArray(drafts) || drafts.length === 0) return { ok: true, count: 0 };

            // 用相對路徑 'img/...' / 'api/...'（無開頭 '/'），由 <base href="..."> 自動補上 PathBase。
            // 開頭 '/' 是 absolute path 會走 host root，IIS 子應用程式（PathBase=/MT）下會跳過 /MT 前綴導致 404。
            const img = await loadImage('img/appointment.png');
            const total = drafts.length;

            if (!silent && window.Swal) {
                Swal.fire({
                    title: '正在簽發聘書',
                    html: `<span id="cert-progress">0 / ${total}</span>`,
                    allowOutsideClick: false,
                    showConfirmButton: false,
                    didOpen: () => Swal.showLoading()
                });
            }

            let done = 0;
            let failed = 0;
            for (const d of drafts) {
                try {
                    const blob = await drawOne(img, d);
                    const fd = new FormData();
                    fd.append('certId', String(d.certId));
                    fd.append('file', blob, d.targetFileName);

                    const res = await fetch('api/appointment-cert/upload', {
                        method: 'POST',
                        body: fd,
                        credentials: 'include'
                    });
                    if (!res.ok) failed++;
                } catch {
                    failed++;
                }
                done++;
                const el = document.getElementById('cert-progress');
                if (el) el.textContent = `${done} / ${total}`;
            }

            if (!silent && window.Swal) Swal.close();
            return { ok: failed === 0, count: done, failed };
        }
    };
})();


// ============================================================
//  7. 字體縮放控制器
//     縮放套用 + 拖拽 + 邊緣吸附 + 閒置半透明 + 位置記憶
// ============================================================

window.FontController = {
    _idleTimer: null,
    _IDLE_DELAY: 3000,   // 3 秒無操作後變半透明
    _BASE_SCALE: 120,    // 全站預設基準字級（控制器 100% 時的實際倍率）

    /** 初始化 */
    init(scale) {
        this.applyScale(scale);
        this._initDrag();
        this._initIdle();
        this._restorePosition();
    },

    /** 套用字體縮放 */
    applyScale(scale) {
        const effectiveScale = (this._BASE_SCALE * scale) / 100;
        document.documentElement.style.fontSize = `${effectiveScale}%`;
        localStorage.setItem('cwt_font_scale', scale);
    },

    /** 還原上次位置（吸附後的位置） */
    _restorePosition() {
        const el = document.getElementById('cwtFontController');
        if (!el) return;
        const saved = JSON.parse(localStorage.getItem('cwt_font_pos'));
        if (saved?.left) {
            el.style.left = saved.left;
            el.style.top = saved.top;
            el.style.bottom = 'auto';
            el.style.right = 'auto';
        }
    },

    /** 吸附到最近的螢幕邊緣 */
    _snapToEdge(el) {
        const rect = el.getBoundingClientRect();
        const cx = rect.left + rect.width / 2;
        const w = window.innerWidth;
        const h = window.innerHeight;
        // 限制 top 不超出螢幕上下
        let top = Math.max(10, Math.min(rect.top, h - rect.height - 10));
        // 吸附左邊或右邊
        const left = cx < w / 2 ? 10 : w - rect.width - 10;

        el.style.transition = 'left 0.3s ease, top 0.3s ease';
        el.style.left = `${left}px`;
        el.style.top = `${top}px`;
        el.style.bottom = 'auto';
        el.style.right = 'auto';

        localStorage.setItem('cwt_font_pos', JSON.stringify({ left: el.style.left, top: el.style.top }));
        // 清除 transition 避免影響下次拖拽
        setTimeout(() => { el.style.transition = 'none'; }, 300);
    },

    /** 閒置半透明 */
    _initIdle() {
        const el = document.getElementById('cwtFontController');
        if (!el) return;

        const resetIdle = () => {
            el.style.opacity = '1';
            clearTimeout(this._idleTimer);
            this._idleTimer = setTimeout(() => {
                // 檢查是否正在 hover（展開中不變透明）
                if (!el.matches(':hover')) {
                    el.style.opacity = '0.3';
                }
            }, this._IDLE_DELAY);
        };

        // 滑入時恢復不透明
        el.addEventListener('mouseenter', () => {
            clearTimeout(this._idleTimer);
            el.style.opacity = '1';
        });
        // 滑出後開始倒數
        el.addEventListener('mouseleave', resetIdle);

        // 初始啟動倒數
        resetIdle();
    },

    /** 拖拽功能（放開後自動吸附邊緣） */
    _initDrag() {
        const el = document.getElementById('cwtFontController');
        const btn = document.getElementById('fontToggleMain');
        if (!el || !btn) return;

        let isDragging = false, startX, startY, initLeft, initTop;

        const onStart = (e) => {
            isDragging = false;
            const touch = e.touches?.[0];
            startX = touch ? touch.clientX : e.clientX;
            startY = touch ? touch.clientY : e.clientY;
            const rect = el.getBoundingClientRect();
            initLeft = rect.left;
            initTop = rect.top;
            el.style.opacity = '1'; // 拖拽時完全不透明
            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup', onEnd);
            document.addEventListener('touchmove', onMove, { passive: false });
            document.addEventListener('touchend', onEnd);
        };

        const onMove = (e) => {
            const touch = e.touches?.[0];
            const cx = touch ? touch.clientX : e.clientX;
            const cy = touch ? touch.clientY : e.clientY;
            const dx = cx - startX, dy = cy - startY;
            if (Math.abs(dx) > 5 || Math.abs(dy) > 5) {
                isDragging = true;
                el.style.bottom = 'auto';
                el.style.right = 'auto';
                el.style.left = `${initLeft + dx}px`;
                el.style.top = `${initTop + dy}px`;
                el.style.transition = 'none';
            }
        };

        const onEnd = () => {
            if (isDragging) {
                this._snapToEdge(el);
            }
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onEnd);
            document.removeEventListener('touchmove', onMove);
            document.removeEventListener('touchend', onEnd);
        };

        btn.addEventListener('mousedown', onStart);
        btn.addEventListener('touchstart', onStart, { passive: true });
        btn.addEventListener('click', (e) => { if (isDragging) { e.preventDefault(); e.stopPropagation(); } }, true);

        // 視窗縮放時重新吸附
        window.addEventListener('resize', () => this._snapToEdge(el));
    },

    /** 清理 */
    dispose() {
        clearTimeout(this._idleTimer);
    }
};
