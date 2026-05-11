/**
 * audio-upload.js — 聽力題音檔上傳
 *
 * 為何走 JS：Blazor Server 預設 SignalR 訊息上限 32KB，10MB 音檔
 * 透過 InputFile + OpenReadStream 不僅慢還會被切片限制；改用瀏覽器原生
 * fetch 直送 /api/upload-audio，效能與穩定度遠優於 Hub。
 */
window.AudioUpload = {
    /**
     * 開檔案選擇器、驗證、上傳；成功回傳 URL，使用者取消回傳 null，失敗 reject。
     * @returns {Promise<string|null>}
     */
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
                reject(new Error('音檔大小不可超過 10MB'));
                return;
            }

            const fd = new FormData();
            fd.append('file', file);
            try {
                const res = await fetch('/api/upload-audio', { method: 'POST', body: fd });
                const data = await res.json();
                if (!res.ok) {
                    reject(new Error(data.error || '音檔上傳失敗'));
                    return;
                }
                resolve(data.url);
            } catch {
                reject(new Error('音檔上傳失敗，請稍後再試'));
            }
        };

        // 部分新版瀏覽器支援 cancel 事件，舊版若使用者取消會讓 Promise 懸掛但無害
        input.oncancel = () => resolve(null);
        input.click();
    })
};
