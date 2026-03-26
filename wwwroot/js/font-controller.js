/**
 * font-controller.js — 字體縮放控制器
 * 功能：縮放套用 + 拖拽 + 邊緣吸附 + 閒置半透明 + 位置記憶
 */
window.FontController = {
    _idleTimer: null,
    _IDLE_DELAY: 3000, // 3秒無操作後變半透明

    /** 初始化 */
    init(scale) {
        this.applyScale(scale);
        this._initDrag();
        this._initIdle();
        this._restorePosition();
    },

    /** 套用字體縮放 */
    applyScale(scale) {
        document.documentElement.style.fontSize = `${scale}%`;
        localStorage.setItem('cwt_font_scale', scale);
    },

    /** 還原上次位置（吸附後的位置） */
    _restorePosition() {
        const el = document.getElementById('cwtFontController');
        if (!el) return;
        const saved = JSON.parse(localStorage.getItem('cwt_font_pos'));
        if (saved?.left) {
            el.style.left = saved.left;
            el.style.top = saved.top;
            el.style.bottom = 'auto';
            el.style.right = 'auto';
        }
    },

    /** 吸附到最近的螢幕邊緣 */
    _snapToEdge(el) {
        const rect = el.getBoundingClientRect();
        const cx = rect.left + rect.width / 2;
        const w = window.innerWidth;
        const h = window.innerHeight;
        // 限制 top 不超出螢幕上下
        let top = Math.max(10, Math.min(rect.top, h - rect.height - 10));
        // 吸附左邊或右邊
        const left = cx < w / 2 ? 10 : w - rect.width - 10;

        el.style.transition = 'left 0.3s ease, top 0.3s ease';
        el.style.left = `${left}px`;
        el.style.top = `${top}px`;
        el.style.bottom = 'auto';
        el.style.right = 'auto';

        localStorage.setItem('cwt_font_pos', JSON.stringify({ left: el.style.left, top: el.style.top }));
        // 清除 transition 避免影響下次拖拽
        setTimeout(() => { el.style.transition = 'none'; }, 300);
    },

    /** 閒置半透明 */
    _initIdle() {
        const el = document.getElementById('cwtFontController');
        if (!el) return;

        const resetIdle = () => {
            el.style.opacity = '1';
            clearTimeout(this._idleTimer);
            this._idleTimer = setTimeout(() => {
                // 檢查是否正在 hover（展開中不變透明）
                if (!el.matches(':hover')) {
                    el.style.opacity = '0.3';
                }
            }, this._IDLE_DELAY);
        };

        // 滑入時恢復不透明
        el.addEventListener('mouseenter', () => {
            clearTimeout(this._idleTimer);
            el.style.opacity = '1';
        });
        // 滑出後開始倒數
        el.addEventListener('mouseleave', resetIdle);

        // 初始啟動倒數
        resetIdle();
    },

    /** 拖拽功能（放開後自動吸附邊緣） */
    _initDrag() {
        const el = document.getElementById('cwtFontController');
        const btn = document.getElementById('fontToggleMain');
        if (!el || !btn) return;

        let isDragging = false, startX, startY, initLeft, initTop;

        const onStart = (e) => {
            isDragging = false;
            const touch = e.touches?.[0];
            startX = touch ? touch.clientX : e.clientX;
            startY = touch ? touch.clientY : e.clientY;
            const rect = el.getBoundingClientRect();
            initLeft = rect.left;
            initTop = rect.top;
            el.style.opacity = '1'; // 拖拽時完全不透明
            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup', onEnd);
            document.addEventListener('touchmove', onMove, { passive: false });
            document.addEventListener('touchend', onEnd);
        };

        const onMove = (e) => {
            const touch = e.touches?.[0];
            const cx = touch ? touch.clientX : e.clientX;
            const cy = touch ? touch.clientY : e.clientY;
            const dx = cx - startX, dy = cy - startY;
            if (Math.abs(dx) > 5 || Math.abs(dy) > 5) {
                isDragging = true;
                el.style.bottom = 'auto';
                el.style.right = 'auto';
                el.style.left = `${initLeft + dx}px`;
                el.style.top = `${initTop + dy}px`;
                el.style.transition = 'none';
            }
        };

        const onEnd = () => {
            if (isDragging) {
                this._snapToEdge(el);
            }
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onEnd);
            document.removeEventListener('touchmove', onMove);
            document.removeEventListener('touchend', onEnd);
        };

        btn.addEventListener('mousedown', onStart);
        btn.addEventListener('touchstart', onStart, { passive: true });
        btn.addEventListener('click', (e) => { if (isDragging) { e.preventDefault(); e.stopPropagation(); } }, true);

        // 視窗縮放時重新吸附
        window.addEventListener('resize', () => this._snapToEdge(el));
    },

    /** 清理 */
    dispose() {
        clearTimeout(this._idleTimer);
    }
};
