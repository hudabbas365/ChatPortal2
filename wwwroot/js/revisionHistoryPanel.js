/* Phase 34b — Revision History Panel (B18)
 * Server-persisted auto-revisions of a report's canvas state.
 * Lists /api/reports/{guid}/revisions (kind=Auto) and restores to a chosen one.
 */
(function (global) {
    'use strict';

    const PANEL_ID = 'revision-history-panel';
    const state = { visible: false, loading: false, items: [] };

    function reportGuid() { return global._currentReportGuid || null; }

    function timeAgo(iso) {
        try {
            const d = new Date(iso);
            const s = Math.floor((Date.now() - d.getTime()) / 1000);
            if (s < 60) return s + 's ago';
            if (s < 3600) return Math.floor(s / 60) + 'm ago';
            if (s < 86400) return Math.floor(s / 3600) + 'h ago';
            return d.toLocaleString();
        } catch { return ''; }
    }

    function ensurePanel() {
        let p = document.getElementById(PANEL_ID);
        if (p) return p;
        p = document.createElement('div');
        p.id = PANEL_ID;
        p.className = 'revision-history-panel';
        p.innerHTML =
            '<div class="rhp-header">' +
            '  <h6 class="mb-0"><i class="bi bi-clock-history me-2"></i>Revision History</h6>' +
            '  <button class="rhp-close" title="Close"><i class="bi bi-x-lg"></i></button>' +
            '</div>' +
            '<div class="rhp-subheader text-muted small">Auto-saved whenever the report is saved. Up to 20 kept.</div>' +
            '<div class="rhp-list" id="rhp-list"></div>';
        document.body.appendChild(p);
        p.querySelector('.rhp-close').addEventListener('click', hide);
        return p;
    }

    function render() {
        const panel = ensurePanel();
        const list = panel.querySelector('#rhp-list');
        if (state.loading) {
            list.innerHTML = '<div class="rhp-empty"><i class="bi bi-hourglass-split me-1"></i>Loading…</div>';
            return;
        }
        if (!state.items.length) {
            list.innerHTML = '<div class="rhp-empty"><i class="bi bi-inbox me-1"></i>No revisions yet. Save the report to start tracking history.</div>';
            return;
        }
        list.innerHTML = state.items.map((r, i) =>
            '<div class="rhp-item" data-id="' + r.id + '">' +
            '  <div class="rhp-item-main">' +
            '    <div class="rhp-item-title">' + (r.name ? escapeHtml(r.name) : ('Auto-save #' + (state.items.length - i))) + '</div>' +
            '    <div class="rhp-item-meta">' + timeAgo(r.createdAt) + (r.createdBy ? (' · ' + escapeHtml(r.createdBy)) : '') + '</div>' +
            '  </div>' +
            '  <button class="btn btn-xs btn-outline-primary rhp-restore" title="Restore this revision"><i class="bi bi-arrow-counterclockwise me-1"></i>Restore</button>' +
            '</div>'
        ).join('');
        list.querySelectorAll('.rhp-restore').forEach(btn => {
            btn.addEventListener('click', function () {
                const id = parseInt(this.closest('.rhp-item').dataset.id, 10);
                restore(id);
            });
        });
    }

    function escapeHtml(s) { return String(s || '').replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c])); }

    async function load() {
        const g = reportGuid();
        if (!g) { state.items = []; render(); return; }
        state.loading = true; render();
        try {
            const resp = await fetch('/api/reports/' + encodeURIComponent(g) + '/revisions?kind=Auto');
            state.items = resp.ok ? await resp.json() : [];
        } catch { state.items = []; }
        state.loading = false;
        render();
    }

    async function restore(revId) {
        const g = reportGuid();
        if (!g) return;
        var __ok = await (window.cpConfirm ? window.cpConfirm({ title: 'Restore revision', message: 'Restore this revision?', subMessage: 'The current canvas will be auto-saved before being replaced.', confirmText: 'Restore', variant: 'primary', icon: 'bi-arrow-counterclockwise' }) : Promise.resolve(confirm('Restore this revision?'))); if (!__ok) return;
        try {
            const resp = await fetch('/api/reports/' + encodeURIComponent(g) + '/revisions/' + revId + '/restore', { method: 'POST' });
            if (!resp.ok) { alert('Failed to restore revision.'); return; }
            const data = await resp.json();
            if (data.canvasJson && global.canvasManager) {
                try {
                    const parsed = JSON.parse(data.canvasJson);
                    if (parsed && parsed.pages) {
                        canvasManager.pages = parsed.pages;
                        canvasManager.activePageIndex = parsed.activePageIndex || 0;
                        canvasManager.charts = (canvasManager.pages[canvasManager.activePageIndex] || {}).charts || [];
                        canvasManager.renderAll && canvasManager.renderAll();
                        if (typeof canvasManager.renderPageTabs === 'function') canvasManager.renderPageTabs();
                    }
                } catch (e) { console.warn('Restore parse failed', e); }
            }
            hide();
            await load();
        } catch { alert('Failed to restore revision.'); }
    }

    function show() { state.visible = true; ensurePanel().classList.add('open'); load(); }
    function hide() { state.visible = false; const p = document.getElementById(PANEL_ID); if (p) p.classList.remove('open'); }
    function toggle() { state.visible ? hide() : show(); }

    global.revisionHistoryPanel = { show, hide, toggle, load };

    function init() {
        const btn = document.getElementById('btn-revisions');
        if (btn) btn.addEventListener('click', (e) => { e.stopPropagation(); toggle(); });
        document.addEventListener('mousedown', (e) => {
            if (!state.visible) return;
            const p = document.getElementById(PANEL_ID);
            if (p && !p.contains(e.target) && !(btn && btn.contains(e.target))) hide();
        });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})(window);
