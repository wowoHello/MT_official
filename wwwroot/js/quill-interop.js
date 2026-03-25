/**
 * quill-interop.js — Blazor ↔ Quill 編輯器 JS Interop
 * 功能：初始化 Quill、取值/設值、中文標點插入、字數統計、底部滑入面板控制
 */

// 全域 Quill 實例
window.QuillInterop = {
    instances: {},

    /** 初始化 Quill 編輯器 */
    init: function (containerId, dotNetRef) {
        if (this.instances[containerId]) {
            return;
        }

        // 註冊自訂字體
        const Font = Quill.import('attributors/class/font');
        Font.whitelist = ['serif', 'monospace', 'dfkai-sb', 'times-new-roman'];
        Quill.register(Font, true);

        const quill = new Quill('#' + containerId, {
            theme: 'snow',
            placeholder: '在此輸入內容...',
            modules: {
                toolbar: {
                    container: [
                        [{ size: ['small', false, 'large'] }, { header: [2, 3] }, { font: Font.whitelist }],
                        [{ color: [] }, { background: [] }, { align: [] }],
                        ['bold', 'underline', 'strike', 'link'],
                        [{ list: 'ordered' }, { list: 'bullet' }, { indent: '-1' }, { indent: '+1' }],
                        [{ script: 'sub' }, { script: 'super' }],
                        ['image', 'clean']
                    ],
                    handlers: {
                        image: function () {
                            const input = document.createElement('input');
                            input.setAttribute('type', 'file');
                            input.setAttribute('accept', 'image/png, image/jpeg, image/gif, image/webp');
                            input.addEventListener('change', () => {
                                const file = input.files?.[0];
                                if (!file) return;
                                if (file.size > 5 * 1024 * 1024) {
                                    alert('圖片大小不可超過 5MB');
                                    return;
                                }
                                const reader = new FileReader();
                                reader.onload = (e) => {
                                    const range = quill.getSelection(true);
                                    quill.insertEmbed(range.index, 'image', e.target.result);
                                    quill.setSelection(range.index + 1);
                                };
                                reader.readAsDataURL(file);
                            });
                            input.click();
                        }
                    }
                }
            }
        });

        // 字數統計回呼
        quill.on('text-change', function () {
            const text = quill.getText().trim();
            const count = text.length;
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnWordCountChanged', count);
            }
        });

        this.instances[containerId] = { quill, dotNetRef };
    },

    /** 取得 HTML 內容 */
    getHtml: function (containerId) {
        const inst = this.instances[containerId];
        if (!inst) return '';
        const html = inst.quill.root.innerHTML;
        return html === '<p><br></p>' ? '' : html;
    },

    /** 設定 HTML 內容 */
    setHtml: function (containerId, html) {
        const inst = this.instances[containerId];
        if (!inst) return;
        if (!html || html === '<p><br></p>') {
            inst.quill.setText('');
        } else {
            inst.quill.root.innerHTML = html;
        }
    },

    /** 插入中文標點（支援成對標點自動定位游標） */
    insertPunctuation: function (containerId, char, isPair) {
        const inst = this.instances[containerId];
        if (!inst) return;
        const quill = inst.quill;
        const range = quill.getSelection(true);
        if (!range) {
            quill.focus();
            return;
        }
        quill.insertText(range.index, char);
        if (isPair) {
            // 游標移到成對標點中間
            quill.setSelection(range.index + 1, 0);
        } else {
            quill.setSelection(range.index + char.length, 0);
        }
    },

    /** 聚焦編輯器 */
    focus: function (containerId) {
        const inst = this.instances[containerId];
        if (!inst) return;
        inst.quill.focus();
    },

    /** 取得純文字字數 */
    getWordCount: function (containerId) {
        const inst = this.instances[containerId];
        if (!inst) return 0;
        return inst.quill.getText().trim().length;
    },

    /** 銷毀實例 */
    dispose: function (containerId) {
        const inst = this.instances[containerId];
        if (inst) {
            delete this.instances[containerId];
        }
    }
};
