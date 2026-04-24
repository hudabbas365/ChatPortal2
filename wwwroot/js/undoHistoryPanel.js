// Phase 34-A8: Undo history flyout panel.
// Shows the labelled entries in `canvasManager._undoStack`; click an entry
// to revert the canvas to exactly that state (via `revertToHistory`).
(function (global) {
    'use strict';

    const PANEL_ID = 'undo-history-panel';
    const BTN_ID = 'btn-undo-history';

    function fmtTime(ts) {
        if (!ts) return '';
        const d = new Date(ts);
        const pad = (n) => String(n).padStart(2, '0');
        return pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
    }

    function ensurePanel() {
        let panel = document.getElementById(PANEL_ID);
        if (panel) return panel;
        panel = document.createElement('div');
        panel.id = PANEL_ID;
        panel.className = 'undo-history-panel';
        panel.innerHTML =
            '<div class="uhp-header">' +
                '<i class="bi bi-clock-history me-1"></i>' +
                '<span class="uhp-title">History</span>' +
                '<button class="uhp-close" title="Close"><i class="bi bi-x-lg"></i></button>' +
            '</div>' +
            '<div class="uhp-body"></div>' +
            '<div class="uhp-footer text-muted">Click any step to revert. Up to 30 steps kept.</div>';
        document.body.appendChild(panel);
        panel.querySelector('.uhp-close').addEventListener('click', hide);
        // Dismiss on outside click
        document.addEventListener('mousedown', (e) => {
            if (!panel.classList.contains('open')) return;
            if (panel.contains(e.target)) return;
            const btn = document.getElementById(BTN_ID);
            if (btn && btn.contains(e.target)) return;
            hide();
        });
        return panel;
    }

    function render() {
        const panel = ensurePanel();
        const body = panel.querySelector('.uhp-body');
        const cm = global.canvasManager;
        const stack = (cm && cm._undoStack) || [];
        if (stack.length === 0) {
            body.innerHTML = '<div class="uhp-empty">No history yet. Actions like add, delete, or reset will appear here.</div>';
            return;
        }
        // Newest at top.
        const items = stack.slice().reverse().map((entry, i) => {
            const realIdx = stack.length - 1 - i;
            const label = (entry && entry.label) || 'Change';
            const ts = fmtTime(entry && entry.timestamp);
            const safe = String(label).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
            return (
                '<button class="uhp-item" data-index="' + realIdx + '" title="Revert to this state">' +
                    '<span class="uhp-item-label">' + safe + '</span>' +
                    '<span class="uhp-item-time">' + ts + '</span>' +
                '</button>'
            );
        }).join('');
        body.innerHTML = items;
        body.querySelectorAll('.uhp-item').forEach(el => {
            el.addEventListener('click', () => {
                const idx = parseInt(el.dataset.index, 10);
                if (isNaN(idx)) return;
                if (global.canvasManager && typeof global.canvasManager.revertToHistory === 'function') {
                    global.canvasManager.revertToHistory(idx);
                }
            });
        });
    }

    function show() {
        const panel = ensurePanel();
        render();
        panel.classList.add('open');
    }
    function hide() {
        const panel = document.getElementById(PANEL_ID);
        if (panel) panel.classList.remove('open');
    }
    function toggle() {
        const panel = ensurePanel();
        if (panel.classList.contains('open')) hide(); else show();
    }

    function init() {
        const btn = document.getElementById(BTN_ID);
        if (btn) btn.addEventListener('click', (e) => { e.stopPropagation(); toggle(); });
        document.addEventListener('undo:stack-changed', () => {
            // Only re-render when visible; cheap guard.
            const panel = document.getElementById(PANEL_ID);
            if (panel && panel.classList.contains('open')) render();
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    global.undoHistoryPanel = { show, hide, toggle, render };
}(window));
