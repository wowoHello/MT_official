/**
 * textarea-helper.js — 純 textarea 游標位置插入助手
 * 用於審題意見等只需要純文字輸入但仍要支援罐頭訊息插入的場景
 */
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
