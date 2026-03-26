/**
 * quill-interop.js — Blazor ↔ Quill 編輯器 JS Interop
 * 參照 https://quilljs.com/docs/modules/toolbar
 */

// 字體是否已註冊
let fontRegistered = false;

// 工具列設定（明確指定 whitelist，不依賴空陣列）
const toolbarOptions = [
    [{ font: ['dfkai-sb', 'times-new-roman'] }, { size: ['small', false, 'large'] }],
    [{ color: [] }, { align: [] }],
    ['bold', 'underline', 'strike'],
    [{ list: 'ordered' }, { list: 'bullet' }, { indent: '-1' }, { indent: '+1' }],
    ['image', 'clean']
];

window.QuillInterop = {
    instances: {},

    /** 初始化 Quill 編輯器 */
    init(containerId, dotNetRef) {
        if (this.instances[containerId]) return;

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
                dotNetRef.invokeMethodAsync('OnWordCountChanged', text.length);
                const html = quill.root.innerHTML;
                dotNetRef.invokeMethodAsync('OnContentChanged', html === '<p><br></p>' ? '' : html);
            }, 500);
        });

        this.instances[containerId] = { quill, dotNetRef };
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

    /** 取得純文字字數 */
    getWordCount(containerId) {
        return this.instances[containerId]?.quill.getText().trim().length ?? 0;
    },

    /** 銷毀實例 */
    dispose(containerId) {
        delete this.instances[containerId];
    }
};
