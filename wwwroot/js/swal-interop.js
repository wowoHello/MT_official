// SweetAlert2 互動輔助：供 Blazor 以 C# 取得使用者確認結果。
window.swalConfirm = async (options) => {
    const result = await Swal.fire(options);
    return result.isConfirmed === true;
};

// 右上角 Toast（無遮罩、不阻斷操作）
// 用途：儲存成功、刪除成功等輕量通知
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
