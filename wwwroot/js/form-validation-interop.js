// ============================================================
//  form-validation-interop.js
//  命題表單欄位驗證的 UI 輔助：滾動到第一個錯誤欄位
//  使用方式：JS.InvokeVoidAsync("scrollToFirstInvalid", fieldKey)
//  fieldKey 對應 Razor 欄位最外層 div 的 data-field 屬性。
// ============================================================
window.scrollToFirstInvalid = (fieldKey) => {
    if (!fieldKey) return false;
    // 用 CSS escape 避免 fieldKey 含特殊字元時 selector 失效
    const safe = (window.CSS && CSS.escape) ? CSS.escape(fieldKey) : fieldKey;
    const el = document.querySelector(`[data-field="${safe}"]`);
    if (!el) return false;
    // 使用 smooth + center，讓使用者能立即看到該欄位的紅框包覆
    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    return true;
};
