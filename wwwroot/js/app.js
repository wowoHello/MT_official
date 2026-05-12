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
 *   6. 字體縮放控制器             — FontController
 */


// ============================================================
//  1. SweetAlert2 對話框
//     兩套 API 並存（因歷史呼叫端不同，保留以避免破壞既有頁面）：
//       - swalInterop.fire / .confirm / .fireToast：泛用 swal 包裝
//       - swalConfirm / swalToast：較新、命名更直白的快捷
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
                const res = await fetch('/api/upload', { method: 'POST', body: fd });
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
                const res = await fetch('/api/upload-audio', { method: 'POST', body: fd });
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
//  6. 字體縮放控制器
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
