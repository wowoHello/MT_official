/**
 * quill-interop.js — Blazor ↔ Quill 編輯器 JS Interop
 * 參照 https://quilljs.com/docs/modules/toolbar
 *
 * 效能策略：Quill 實例常駐，首次展開才初始化，後續開關僅填入/取回內容
 */

// 全域註冊狀態（字體 / 自訂格式 / 自訂 icon 只能跑一次）
let globalsRegistered = false;

// 工具列設定（明確指定 whitelist，不依賴空陣列）
// double-underline 為自訂 ClassAttributor（見 _create 內註冊），輸出 <span class="ql-double-underline">
const toolbarOptions = [
    [{ font: ['times-new-roman', 'dfkai-sb'] }, { size: ['small', false, 'large'] }],
    [{ color: [] }, { align: [] }],
    ['bold', 'underline', 'double-underline', 'strike'],
    [{ list: 'ordered' }, { list: 'bullet' }, { indent: '-1' }, { indent: '+1' }],
    ['clean']
];

// 精簡工具列（審題意見等短評輸入；無字體/大小/色彩/對齊/縮排/圖片）
const toolbarOptionsSimple = [
    ['bold', 'underline', 'strike'],
    [{ list: 'ordered' }, { list: 'bullet' }],
    ['clean']
];

// 含圖片工具列（公告編輯用；InlineQuillEditor 預設 ToolbarMode="full" 走這條）
const toolbarOptionsWithImage = [
    [{ font: ['times-new-roman', 'dfkai-sb'] }, { size: ['small', false, 'large'] }],
    [{ color: [] }, { align: [] }],
    ['bold', 'underline', 'double-underline', 'strike'],
    [{ list: 'ordered' }, { list: 'bullet' }, { indent: '-1' }, { indent: '+1' }],
    ['image', 'clean']
];

function pickToolbar(mode) {
    if (mode === 'simple') return toolbarOptionsSimple;
    if (mode === 'full')   return toolbarOptionsWithImage;
    return toolbarOptions;
}

window.QuillInterop = {
    instances: {},

    /** 內部：建立 Quill 實例（僅首次呼叫） */
    _create(containerId, dotNetRef, toolbarMode) {
        // 註冊全域：字體 whitelist、自訂 double-underline 格式、自訂 toolbar icon（只執行一次）
        if (!globalsRegistered) {
            // 字體 whitelist
            const Font = Quill.import('formats/font');
            Font.whitelist = ['dfkai-sb', 'times-new-roman'];
            Quill.register(Font, true);

            // 自訂格式：雙底線（ClassAttributor，輸出 <span class="ql-double-underline">）
            const Parchment = Quill.import('parchment');
            const DoubleUnderline = new Parchment.ClassAttributor('double-underline', 'ql-double-underline', {
                scope: Parchment.Scope.INLINE
            });
            Quill.register(DoubleUnderline, true);

            // 自訂 toolbar icon：仿 Quill 內建 underline icon，把單條底線改為雙條
            const icons = Quill.import('ui/icons');
            icons['double-underline'] =
                '<svg viewBox="0 0 18 18">' +
                  '<path class="ql-stroke" d="M5,3V9a4.012,4.012,0,0,0,4,4H9a4.012,4.012,0,0,0,4-4V3"></path>' +
                  '<rect class="ql-fill" height="1" rx="0.5" ry="0.5" width="12" x="3" y="14"></rect>' +
                  '<rect class="ql-fill" height="1" rx="0.5" ry="0.5" width="12" x="3" y="16"></rect>' +
                '</svg>';

            globalsRegistered = true;
        }

        const isSimple = toolbarMode === 'simple';
        const quill = new Quill('#' + containerId, {
            theme: 'snow',
            placeholder: '在此輸入內容...',
            modules: { toolbar: pickToolbar(toolbarMode) }
        });

        // 圖片上傳 + 單/雙底線互斥（僅在完整工具列下註冊；simple 模式無圖片與雙底線按鈕）
        if (!isSimple) {
            const toolbar = quill.getModule('toolbar');

            toolbar.addHandler('image', () => {
                const input = document.createElement('input');
                input.type = 'file';
                input.accept = 'image/png, image/jpeg, image/gif, image/webp';
                input.onchange = async () => {
                    const file = input.files?.[0];
                    if (!file) return;
                    if (file.size > 5 * 1024 * 1024) {
                        alert('圖片大小不可超過 5MB');
                        return;
                    }
                    const formData = new FormData();
                    formData.append('file', file);
                    try {
                        const res = await fetch('/api/upload', { method: 'POST', body: formData });
                        const data = await res.json();
                        if (!res.ok) { alert(data.error || '上傳失敗'); return; }
                        const range = quill.getSelection(true);
                        quill.insertEmbed(range.index, 'image', data.url);
                        quill.setSelection(range.index + 1);
                    } catch { alert('圖片上傳失敗，請稍後再試'); }
                };
                input.click();
            });

            // 單底線：開啟時先關閉雙底線（同段文字僅能擇一種底線樣式）
            toolbar.addHandler('underline', function () {
                const range = this.quill.getSelection(true);
                if (!range) return;
                const willEnable = !this.quill.getFormat(range)['underline'];
                if (willEnable) this.quill.format('double-underline', false);
                this.quill.format('underline', willEnable);
            });

            // 雙底線：開啟時先關閉單底線
            toolbar.addHandler('double-underline', function () {
                const range = this.quill.getSelection(true);
                if (!range) return;
                const willEnable = !this.quill.getFormat(range)['double-underline'];
                if (willEnable) this.quill.format('underline', false);
                this.quill.format('double-underline', willEnable);
            });
        }

        // 即時同步：字數 + HTML 內容（500ms 防抖）
        let debounceTimer;
        quill.on('text-change', () => {
            clearTimeout(debounceTimer);
            debounceTimer = setTimeout(() => {
                if (!dotNetRef) return;
                const text = quill.getText().trim();
                const inst = QuillInterop.instances[containerId];
                const count = inst?.excludePunct ? text.replace(/[\p{P}\s]/gu, '').length : text.length;
                dotNetRef.invokeMethodAsync('OnWordCountChanged', count);
                const html = quill.root.innerHTML;
                dotNetRef.invokeMethodAsync('OnContentChanged', html === '<p><br></p>' ? '' : html);
            }, 500);
        });

        this.instances[containerId] = { quill, dotNetRef, excludePunct: false };
        return quill;
    },

    /** 合併呼叫：初始化（若尚未）+ 填入內容 + 聚焦，單次 interop 完成 */
    show(containerId, dotNetRef, html, excludePunct, toolbarMode) {
        let inst = this.instances[containerId];
        if (!inst) {
            this._create(containerId, dotNetRef, toolbarMode);
            inst = this.instances[containerId];
        } else {
            // 更新 dotNetRef（元件可能重新建立過）
            inst.dotNetRef = dotNetRef;
        }
        const quill = inst.quill;
        if (!html || html === '<p><br></p>') {
            quill.setText('');
        } else {
            const delta = quill.clipboard.convert({ html });
            quill.setContents(delta, 'silent');
        }
        // 同步開關狀態並手動計算字數（silent 不觸發 text-change）
        inst.excludePunct = !!excludePunct;
        const text = quill.getText().trim();
        const count = inst.excludePunct ? text.replace(/[\p{P}\s]/gu, '').length : text.length;
        inst.dotNetRef?.invokeMethodAsync('OnWordCountChanged', count);
        quill.focus();
    },

    /** 取得 HTML 內容 */
    getHtml(containerId) {
        const inst = this.instances[containerId];
        if (!inst) return '';
        const html = inst.quill.root.innerHTML;
        return html === '<p><br></p>' ? '' : html;
    },

    /** 設定 HTML 內容 */
    setHtml(containerId, html) {
        const inst = this.instances[containerId];
        if (!inst) return;
        if (!html || html === '<p><br></p>') {
            inst.quill.setText('');
        } else {
            const delta = inst.quill.clipboard.convert({ html });
            inst.quill.setContents(delta, 'silent');
        }
    },

    /** 插入中文標點（成對標點游標自動移到中間） */
    insertPunctuation(containerId, char, isPair) {
        const inst = this.instances[containerId];
        if (!inst) return;
        const quill = inst.quill;
        const range = quill.getSelection(true);
        if (!range) { quill.focus(); return; }
        quill.insertText(range.index, char);
        quill.setSelection(range.index + (isPair ? 1 : char.length), 0);
    },

    /** 在游標位置插入純文字（罐頭訊息用，自動換行避免黏在前文後） */
    insertText(containerId, text) {
        const inst = this.instances[containerId];
        if (!inst || !text) return;
        const quill = inst.quill;
        let range = quill.getSelection(true);
        if (!range) {
            // 沒有 focus 時插到結尾
            quill.focus();
            range = { index: quill.getLength() - 1, length: 0 };
        }
        const before = range.index > 0 ? quill.getText(range.index - 1, 1) : '\n';
        const prefix = (before === '\n' || before === ' ') ? '' : '\n';
        const payload = prefix + text;
        quill.insertText(range.index, payload, 'user');
        quill.setSelection(range.index + payload.length, 0);
    },

    /** 切換編輯權限（已決策時應唯讀；toolbar 同步隱藏） */
    enable(containerId, enabled) {
        const inst = this.instances[containerId];
        if (!inst) return;
        inst.quill.enable(!!enabled);
        const toolbar = inst.quill.getModule('toolbar')?.container;
        if (toolbar) toolbar.style.display = enabled ? '' : 'none';
    },

    /** 聚焦編輯器 */
    focus(containerId) {
        this.instances[containerId]?.quill.focus();
    },

    /** 切換標點空白排除開關，立即重新計算字數 */
    setExcludePunct(containerId, exclude) {
        const inst = this.instances[containerId];
        if (!inst) return;
        inst.excludePunct = !!exclude;
        const text = inst.quill.getText().trim();
        const count = inst.excludePunct ? text.replace(/[\p{P}\s]/gu, '').length : text.length;
        inst.dotNetRef?.invokeMethodAsync('OnWordCountChanged', count);
    },

    /** 取得純文字字數 */
    getWordCount(containerId) {
        const inst = this.instances[containerId];
        if (!inst) return 0;
        const text = inst.quill.getText().trim();
        return inst.excludePunct ? text.replace(/[\p{P}\s]/gu, '').length : text.length;
    },

    /** 銷毀實例（僅元件 Dispose 時呼叫） */
    dispose(containerId) {
        delete this.instances[containerId];
    }
};
