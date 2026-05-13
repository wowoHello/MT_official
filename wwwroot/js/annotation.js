/**
 * annotation.js — 審題劃記評語 JS Interop
 * ────────────────────────────────────────────────────────────────
 * 功能：
 *   1. 監聽使用者文字選取，在可劃記容器（data-annot-target）內彈出浮層按鈕
 *   2. 計算 plain-text offset（含 prefix / suffix 容錯資訊）回呼 .NET 端
 *   3. 接收 .NET 端傳入的 annotations[]，在容器內以 <mark> 渲染高亮 + 邊距徽章
 *   4. 點擊 mark / 徽章時觸發 .NET 端事件（顯示評語 popup）
 *
 * 對外 API（window.annotation）：
 *   - init(dotnetRef, options)：初始化（綁定全域 mouseup/click 監聽）
 *   - dispose()：解除所有監聽 + 清掉浮層按鈕
 *   - renderHighlights(annotations)：依現存 annotations 重新渲染高亮
 *   - clearHighlights()：清掉所有高亮（不刪 annotations，純 DOM 復原）
 *
 * 設計取捨：
 *   - plain-text offset：將容器內所有 TextNode 串接後的累積偏移量
 *     重新渲染時用 TreeWalker 還原 offset → 對應的 Range
 *   - 跨 element boundary 用 Range.extractContents() + insertNode wrap，
 *     不用 surroundContents（後者遇到 <strong>等巢狀會 throw）
 */

(function () {
    'use strict';

    const STATE = {
        dotnetRef: null,
        floatBtn: null,
        /** 是否唯讀模式（修題端命題者 = 只能看不能加註）— 由 init() 的 options.readOnly 控制 */
        readOnly: false,
        /** Map<fieldKey, Annot[]> — 最近一次 renderHighlights 的快照，供 MutationObserver 重 wrap 用 */
        snapshot: new Map(),
        /** MutationObserver 實例（全域單例，監聽整個 document 內所有 [data-annot-target] 區） */
        observer: null,
        /** 防止 MutationObserver 觸發自己的回呼（wrap 動作本身會引發 mutation）→ 進入時加 1，跳出減 1 */
        wrapBusy: 0,
        /** 重 wrap 的 debounce timer */
        rewrapTimer: null
    };

    // ============================================================
    //  公共 API
    // ============================================================

    /**
     * 初始化：綁全域 mouseup 監聽，當有有效選取時顯示浮層按鈕（唯讀模式不彈按鈕）。
     * @param {DotNetObjectReference} dotnetRef 由 .NET 端建立的物件參考
     * @param {{readOnly?: boolean}=} options 修題端 readOnly=true 時跳過選取監聽（命題者不能加註）
     */
    function init(dotnetRef, options) {
        STATE.dotnetRef = dotnetRef;
        STATE.readOnly = !!(options && options.readOnly);
        if (!STATE.readOnly) _createFloatButton();
        _startObserver();   // ← 啟動 MutationObserver，監聽 Blazor 重 render 沖掉 <mark> 的事件，自動 re-wrap

        if (!STATE.readOnly) {
            // 修題端不需要選取監聽 —— 命題者不能新增劃記
            document.addEventListener('mouseup', _onMouseUp);
            document.addEventListener('mousedown', _onMouseDownAnywhere, true);
            document.addEventListener('selectionchange', _onSelectionChange);
        }
    }

    function dispose() {
        if (!STATE.readOnly) {
            document.removeEventListener('mouseup', _onMouseUp);
            document.removeEventListener('mousedown', _onMouseDownAnywhere, true);
            document.removeEventListener('selectionchange', _onSelectionChange);
        }
        if (STATE.observer) {
            STATE.observer.disconnect();
            STATE.observer = null;
        }
        if (STATE.rewrapTimer) {
            clearTimeout(STATE.rewrapTimer);
            STATE.rewrapTimer = null;
        }
        if (STATE.floatBtn) {
            STATE.floatBtn.remove();
            STATE.floatBtn = null;
        }
        STATE.snapshot.clear();
        STATE.dotnetRef = null;
    }

    /**
     * 依現存 annotations 渲染高亮（先清掉舊的再畫）。
     * @param {Array<{id:number, fieldKey:string, anchorStart:number, anchorEnd:number,
     *               selectedText:string, prefix:string|null, suffix:string|null,
     *               responseState:number|null}>} annotations
     */
    function renderHighlights(annotations) {
        STATE.wrapBusy++;
        try {
            clearHighlights();
            // 不論清空或 wrap 都要更新 snapshot — 後續 Blazor 重 render 沖掉時要靠這份資料補回去
            STATE.snapshot.clear();
            if (!Array.isArray(annotations) || annotations.length === 0) {
                return;
            }

            // 依 fieldKey 分組 — 之後 MutationObserver 補 wrap 也是依 fieldKey 查容器
            const byField = new Map();
            for (const a of annotations) {
                if (!byField.has(a.fieldKey)) byField.set(a.fieldKey, []);
                byField.get(a.fieldKey).push(a);
            }
            // 全部存進 snapshot — 之後 _rewrapByField 會引用
            for (const [fieldKey, list] of byField) {
                STATE.snapshot.set(fieldKey, list.slice());
            }

            for (const [fieldKey, list] of byField) {
                _wrapFieldKey(fieldKey, list);
            }
        } finally {
            STATE.wrapBusy--;
        }
    }

    /** 共用：依 fieldKey + annotations 在對應容器內 wrap（被 renderHighlights 與 MutationObserver re-wrap 共用）*/
    function _wrapFieldKey(fieldKey, list) {
        const container = document.querySelector(`[data-annot-target="${cssEscape(fieldKey)}"]`);
        if (!container) {
            console.warn(`[annotation] container [data-annot-target="${fieldKey}"] NOT FOUND — skipping ${list.length} annotation(s)`);
            return;
        }
        // 容器內可能殘留舊 mark（被搬過去其他位置）— 先在這個容器內部清乾淨
        container.querySelectorAll('mark.annot').forEach(m => {
            const parent = m.parentNode;
            if (!parent) return;
            while (m.firstChild) parent.insertBefore(m.firstChild, m);
            parent.removeChild(m);
        });
        if (container.normalize) container.normalize();

        // 關鍵：從尾往頭 wrap（anchorStart desc）— 詳細理由見 git blame
        const sorted = list.slice().sort((a, b) => b.anchorStart - a.anchorStart);
        for (const annot of sorted) {
            _wrapRangeAt(container, annot);
        }
    }

    function clearHighlights() {
        // 兩次 pass：querySelectorAll 是「靜態 snapshot」，但移除外層 mark 時，
        // 內層 mark 會被一同搬到 grandparent；若有跨層巢狀殘留，第二次 pass 收尾。
        for (let pass = 0; pass < 2; pass++) {
            const marks = document.querySelectorAll('mark.annot');
            if (marks.length === 0) break;
            marks.forEach(m => {
                const parent = m.parentNode;
                if (!parent) return;
                while (m.firstChild) parent.insertBefore(m.firstChild, m);
                parent.removeChild(m);
            });
        }
        // 全部 unwrap 後，對每個容器做一次 normalize() — 把因為 surroundContents
        // 而拆出的相鄰 TextNode 合併回原本的單一節點，下次 wrap 走 TreeWalker
        // 計算 cumOffset 才會回到「原始 plain-text 偏移」基準
        document.querySelectorAll('[data-annot-target]').forEach(c => c.normalize && c.normalize());
    }

    /**
     * 帶 retry 的 renderHighlights — 解 Blazor OnAfterRender 早於 DOM 寫入完成的 timing race。
     * 內部用 setTimeout 50ms / 最多 N 次重試，等到第一個 fieldKey 對應的 data-annot-target 容器
     * 出現在 DOM 才正式呼叫 renderHighlights。若 annotations 為空直接立即清掉。
     */
    function deferredRender(annotations, maxRetries) {
        if (!Array.isArray(annotations) || annotations.length === 0) {
            renderHighlights([]);
            return;
        }
        const retries = (typeof maxRetries === 'number' && maxRetries > 0) ? maxRetries : 8;
        let tries = 0;
        const probe = () => {
            // 用第一筆 fieldKey 作為「DOM 是否就位」的 probe
            const fieldKey = annotations[0].fieldKey;
            const found = document.querySelector(`[data-annot-target="${cssEscape(fieldKey)}"]`);
            if (found || tries >= retries) {
                if (!found) console.warn(`[annotation] deferredRender giving up after ${retries} retries — container "${fieldKey}" never appeared`);
                renderHighlights(annotations);
                return;
            }
            tries++;
            setTimeout(probe, 50);
        };
        probe();
    }

    window.annotation = { init, dispose, renderHighlights, clearHighlights, deferredRender };

    // ============================================================
    //  MutationObserver — 解 Blazor 重 render 沖掉 <mark> 的根本問題
    //  ────────────────────────────────────────────────────────────
    //  Blazor 對 `@((MarkupString)content)` 採用「字串相同則不動 DOM、字串不同則覆寫 innerHTML」策略。
    //  在子層元件再次 render 時，即使父層 OnAfterRenderAsync 已 wrap 過 <mark>，
    //  子層的 MarkupString diff 都可能把 innerHTML 整段重塞，原本插入的 <mark> 就消失了。
    //
    //  解法：用全域 MutationObserver 監聽 body 內部所有 [data-annot-target] 的 DOM 變動；
    //  當變動發生且容器內 <mark.annot> 數量 < snapshot 應有數量，就自動 re-wrap。
    //
    //  防迴圈：wrap 動作本身會觸發 mutation；用 wrapBusy 計數器當作 reentrancy guard。
    //  防爆衝：用 50ms debounce 合併連續變動。
    // ============================================================

    function _startObserver() {
        if (STATE.observer) return;
        STATE.observer = new MutationObserver(_onMutations);
        STATE.observer.observe(document.body, {
            childList:     true,
            subtree:       true,
            characterData: true
        });
    }

    function _onMutations(records) {
        if (STATE.wrapBusy > 0) return;          // 自己 wrap 引起的，忽略
        if (STATE.snapshot.size === 0) return;   // 沒有要 wrap 的東西

        // 任一變動發生在 [data-annot-target] 子樹內，就排程一次 re-wrap 檢查
        let needCheck = false;
        for (const rec of records) {
            const node = rec.target;
            // 變動可能落在 mark 的內部 / 容器內部 / 容器本身
            if (_findAnnotContainer(node) || _isInsideTrackedContainer(node)) {
                needCheck = true;
                break;
            }
        }
        if (!needCheck) return;

        // debounce — 連續 mutation 合併成一次 re-wrap，避免每 mutation 都 wrap 引起卡頓
        if (STATE.rewrapTimer) clearTimeout(STATE.rewrapTimer);
        STATE.rewrapTimer = setTimeout(_rewrapMissing, 50);
    }

    function _isInsideTrackedContainer(node) {
        // 容器本身被新增到 DOM（例如 ReviewModal 才剛開）也算
        const el = node.nodeType === Node.ELEMENT_NODE ? node : node.parentElement;
        if (!el) return false;
        for (const fieldKey of STATE.snapshot.keys()) {
            if (el.querySelector && el.querySelector(`[data-annot-target="${cssEscape(fieldKey)}"]`)) return true;
        }
        return false;
    }

    /** 檢查每個 fieldKey：若容器內 mark 數量不足 snapshot，重 wrap */
    function _rewrapMissing() {
        STATE.rewrapTimer = null;
        if (STATE.snapshot.size === 0) return;

        STATE.wrapBusy++;
        try {
            for (const [fieldKey, list] of STATE.snapshot) {
                const container = document.querySelector(`[data-annot-target="${cssEscape(fieldKey)}"]`);
                if (!container) continue;
                const have = container.querySelectorAll('mark.annot').length;
                if (have >= list.length) continue;   // 數量足夠，無需重 wrap
                // 偵測到 Blazor 重 render 沖掉了 <mark>，重新 wrap 回去
                _wrapFieldKey(fieldKey, list);
            }
        } finally {
            STATE.wrapBusy--;
        }
    }

    // ============================================================
    //  Selection 偵測
    // ============================================================

    function _onMouseDownAnywhere(e) {
        // 點到浮層按鈕本身、不要消滅 selection（讓 click handler 先讀到當前 selection）
        if (STATE.floatBtn && STATE.floatBtn.contains(e.target)) return;
        _hideFloatButton();
    }

    function _onSelectionChange() {
        // selectionchange 在 selection 清空時也會觸發；要藏按鈕避免殘影
        const sel = window.getSelection();
        if (!sel || sel.isCollapsed || sel.toString().trim().length === 0) {
            _hideFloatButton();
        }
    }

    function _onMouseUp(e) {
        // 浮層按鈕本身的點擊不算「使用者選文字」
        if (STATE.floatBtn && STATE.floatBtn.contains(e.target)) return;

        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return;

        const text = sel.toString();
        if (!text || text.trim().length === 0) return;

        const range = sel.getRangeAt(0);
        const container = _findAnnotContainer(range.commonAncestorContainer);
        if (!container) return;   // 不在可劃記容器內

        // 計算 plain-text offset
        const offsets = _getPlainTextOffsets(container, range);
        if (offsets.start < 0 || offsets.end <= offsets.start) return;

        // 暫存當前選取資訊到浮層按鈕的 dataset，click 時讀取
        const btn = STATE.floatBtn;
        btn.dataset.payload = JSON.stringify({
            fieldKey:     container.getAttribute('data-annot-target'),
            anchorStart:  offsets.start,
            anchorEnd:    offsets.end,
            selectedText: text
        });

        _positionFloatButton(range);
    }

    // ============================================================
    //  浮層按鈕
    // ============================================================

    function _createFloatButton() {
        if (STATE.floatBtn) return;

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'annot-float-btn';
        btn.innerHTML = '<i class="fa-solid fa-pen-to-square"></i> 加註';
        btn.style.cssText = [
            'position:absolute',
            'z-index:9999',
            'display:none',
            'padding:6px 12px',
            'background:#6B8EAD',           // morandi
            'color:white',
            'border:none',
            'border-radius:6px',
            'box-shadow:0 4px 12px rgba(0,0,0,.18)',
            'font-size:12px',
            'font-weight:bold',
            'cursor:pointer'
        ].join(';');

        btn.addEventListener('click', _onFloatBtnClick);
        document.body.appendChild(btn);
        STATE.floatBtn = btn;
    }

    function _positionFloatButton(range) {
        const rect = range.getBoundingClientRect();
        const btn = STATE.floatBtn;
        // 用 pageX/pageY 含 scroll offset，避免捲動後按鈕飄移
        const top  = rect.top + window.scrollY - 38;
        const left = rect.left + window.scrollX + (rect.width / 2) - 32;
        btn.style.top  = `${Math.max(0, top)}px`;
        btn.style.left = `${Math.max(0, left)}px`;
        btn.style.display = 'inline-flex';
    }

    function _hideFloatButton() {
        if (STATE.floatBtn) STATE.floatBtn.style.display = 'none';
    }

    async function _onFloatBtnClick(e) {
        e.preventDefault();
        e.stopPropagation();
        const btn = STATE.floatBtn;
        const payloadStr = btn.dataset.payload;
        _hideFloatButton();
        if (!payloadStr || !STATE.dotnetRef) return;

        let payload;
        try { payload = JSON.parse(payloadStr); }
        catch { return; }

        try {
            await STATE.dotnetRef.invokeMethodAsync('OnAnnotationRequested', payload);
        } catch (err) {
            console.error('[annotation] OnAnnotationRequested failed:', err);
        }
    }

    // ============================================================
    //  Plain-text offset 計算
    // ============================================================

    /** 找從 selection node 向上第一個 data-annot-target 容器 */
    function _findAnnotContainer(node) {
        let cur = node;
        while (cur && cur !== document.body) {
            if (cur.nodeType === Node.ELEMENT_NODE && cur.hasAttribute && cur.hasAttribute('data-annot-target')) {
                return cur;
            }
            cur = cur.parentNode;
        }
        return null;
    }

    /** 取容器內所有 TextNode 串接後的純文字 */
    function _getPlainText(container) {
        const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
        let acc = '';
        while (walker.nextNode()) acc += walker.currentNode.textContent;
        return acc;
    }

    /** 計算 selection 在容器內 plain-text 的累積 offset */
    function _getPlainTextOffsets(container, range) {
        const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
        let cumOffset = 0;
        let start = -1, end = -1;
        while (walker.nextNode()) {
            const node = walker.currentNode;
            const len  = node.textContent.length;
            if (start < 0 && node === range.startContainer) start = cumOffset + range.startOffset;
            if (end   < 0 && node === range.endContainer)   end   = cumOffset + range.endOffset;
            cumOffset += len;
            if (start >= 0 && end >= 0) break;
        }
        return { start, end };
    }

    // ============================================================
    //  高亮渲染：將 [anchorStart, anchorEnd) 區間包進 <mark>
    // ============================================================

    function _wrapRangeAt(container, annot) {
        const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
        let cumOffset = 0;
        let startNode = null, startOffsetInNode = 0;
        let endNode = null,   endOffsetInNode = 0;

        while (walker.nextNode()) {
            const node = walker.currentNode;
            const len  = node.textContent.length;
            const nodeStart = cumOffset;
            const nodeEnd   = cumOffset + len;

            if (!startNode && annot.anchorStart >= nodeStart && annot.anchorStart <= nodeEnd) {
                startNode = node;
                startOffsetInNode = annot.anchorStart - nodeStart;
            }
            if (!endNode && annot.anchorEnd >= nodeStart && annot.anchorEnd <= nodeEnd) {
                endNode = node;
                endOffsetInNode = annot.anchorEnd - nodeStart;
                break;
            }
            cumOffset = nodeEnd;
        }
        if (!startNode || !endNode) {
            // offset 對不上目前 DOM（題目改寫過？）→ 嘗試用 selectedText 比對 fallback
            console.warn(`[annotation] anchor ${annot.anchorStart}-${annot.anchorEnd} not located in DOM, fallback by selectedText`, annot);
            _fallbackWrapByText(container, annot);
            return;
        }

        const range = document.createRange();
        try {
            range.setStart(startNode, startOffsetInNode);
            range.setEnd(endNode, endOffsetInNode);
        } catch {
            return;
        }

        const mark = document.createElement('mark');
        mark.className = _markClassOf(annot);
        mark.dataset.annotId = String(annot.id);
        mark.title = annot.comment || '';

        try {
            // 同一 TextNode 內可直接 surround
            range.surroundContents(mark);
        } catch {
            // 跨 element boundary → extract + insert
            try {
                const frag = range.extractContents();
                mark.appendChild(frag);
                range.insertNode(mark);
            } catch (err) {
                console.warn('[annotation] wrap failed', err);
            }
        }
    }

    /** offset 對不上時的容錯：在 plain-text 內找 selectedText 第一筆位置（粗略，可後續強化用 prefix/suffix） */
    function _fallbackWrapByText(container, annot) {
        if (!annot.selectedText) return;
        const plain = _getPlainText(container);
        const idx = plain.indexOf(annot.selectedText);
        if (idx < 0) return;
        _wrapRangeAt(container, { ...annot, anchorStart: idx, anchorEnd: idx + annot.selectedText.length });
    }

    function _markClassOf(annot) {
        // 顏色語意：未回應 amber / 確認修改 sage / 不修改 terracotta
        const stateClass = annot.responseState === 1 ? 'annot-accepted'
                        : annot.responseState === 2 ? 'annot-rejected'
                        : 'annot-pending';
        return `annot ${stateClass}`;
    }

    // ============================================================
    //  Helper
    // ============================================================

    /** css.escape polyfill（部分舊瀏覽器無 CSS.escape） */
    function cssEscape(value) {
        if (window.CSS && CSS.escape) return CSS.escape(value);
        return String(value).replace(/([!"#$%&'()*+,./:;<=>?@[\]^`{|}~])/g, '\\$1');
    }
})();
