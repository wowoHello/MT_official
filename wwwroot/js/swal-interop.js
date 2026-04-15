// SweetAlert2 互動輔助：供 Blazor 以 C# 取得使用者確認結果。
window.swalConfirm = async (options) => {
    const result = await Swal.fire(options);
    return result.isConfirmed === true;
};
