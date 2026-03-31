/**
 * UI Interop Module
 * 提供 Blazor 呼叫外部 UI 庫 (如 SweetAlert2) 的能力。
 */

window.swalInterop = {
    /**
     * 呼叫 SweetAlert2 彈窗
     * @param {object} options - Swal 選項
     */
    fire: async (options) => {
        const result = await Swal.fire(options);
        return result;
    },
    /**
     * 呼叫 SweetAlert2 確認彈窗，回傳是否確認 (boolean)
     * @param {object} options - Swal 選項
     * @returns {boolean}
     */
    confirm: async (options) => {
        const result = await Swal.fire(options);
        return result.isConfirmed;
    },
    /**
     * 呼叫 SweetAlert2 Toast 形式通知
     * @param {string} icon - icon 類型 (success, error, warning, info)
     * @param {string} title - 標題
     * @param {string} text - 內容文字
     * @param {number} timer - 顯示時間 (ms)
     */
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

