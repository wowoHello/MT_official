/**
 * quill-interop.js — Blazor ↔ Quill 編輯器 JS Interop
 * 參照 https://quilljs.com/docs/modules/toolbar
 *
 * 效能策略：Quill 實例常駐，首次展開才初始化，後續開關僅填入/取回內容
 */

// 字體是否已註冊
let fontRegistered = false;

// 工具列設定（明確指定 whitelist，不依賴空陣列）
const toolbarOptions = [
    [{ font: ['times-new-roman', 'dfkai-sb'] }, { size: ['small', false, 'large'] }],
    [{ color: [] }, { align: [] }],
    ['bold', 'underline', 'strike'],
    [{ list: 'ordered' }, { list: 'bullet' }, { indent: '-1' }, { indent: '+1' }],
    ['image', 'clean']
];

window.QuillInterop = {
    instances: {},

    /** 內部：建立 Quill 實例（僅首次呼叫） */
    _create(containerId, dotNetRef) {
        // 註冊自訂字體（只執行一次）
        if (!fontRegistered) {
            const Font = Quill.import('formats/font');
            Font.whitelist = ['dfkai-sb', 'times-new-roman'];
            Quill.register(Font, true);
            fontRegistered = true;
        }

        const quill = new Quill('#' + containerId, {
            theme: 'snow',
            placeholder: '在此輸入內容...',
            modules: { toolbar: toolbarOptions }
        });

        // 圖片上傳 handler（上傳至伺服器，編輯器只存路徑）
        quill.getModule('toolbar').addHandler('image', () => {
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
    show(containerId, dotNetRef, html, excludePunct) {
        let inst = this.instances[containerId];
        if (!inst) {
            this._create(containerId, dotNetRef);
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
