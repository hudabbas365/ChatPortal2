/* Phase 34b — Named Snapshot Manager (A12)
 * User-triggered save/load/restore of named canvas snapshots.
 * Endpoints: /api/reports/{guid}/snapshots (GET/POST), /revisions/{id}/restore, /revisions/{id} DELETE.
 */
(function (global) {
    'use strict';

    const PANEL_ID = 'snapshot-manager-panel';
    const state = { visible: false, loading: false, items: [] };

    function reportGuid() { return global._currentReportGuid || null; }
    function escapeHtml(s) { return String(s || '').replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c])); }
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
        p.className = 'snapshot-manager-panel';
        p.innerHTML =
            '<div class="rhp-header">' +
            '  <h6 class="mb-0"><i class="bi bi-bookmark-star me-2"></i>Snapshots</h6>' +
            '  <button class="rhp-close" title="Close"><i class="bi bi-x-lg"></i></button>' +
            '</div>' +
            '<div class="rhp-subheader text-muted small">Named save points you can return to later.</div>' +
            '<div class="p-2">' +
            '  <div class="input-group input-group-sm">' +
            '    <input type="text" class="form-control" id="snap-new-name" placeholder="Snapshot name…" maxlength="120">' +
            '    <button class="btn btn-primary" id="snap-save-btn" type="button"><i class="bi bi-plus-lg me-1"></i>Save</button>' +
            '  </div>' +
            '</div>' +
            '<div class="rhp-list" id="snap-list"></div>';
        document.body.appendChild(p);
        p.querySelector('.rhp-close').addEventListener('click', hide);
        p.querySelector('#snap-save-btn').addEventListener('click', saveCurrent);
        p.querySelector('#snap-new-name').addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); saveCurrent(); } });
        return p;
    }

    function currentCanvasJson() {
        try {
            if (!global.canvasManager || !canvasManager.pages) return null;
            canvasManager.pages[canvasManager.activePageIndex].charts = canvasManager.charts;
            return JSON.stringify({ pages: canvasManager.pages, activePageIndex: canvasManager.activePageIndex });
        } catch { return null; }
    }

    function render() {
        const panel = ensurePanel();
        const list = panel.querySelector('#snap-list');
        if (state.loading) {
            list.innerHTML = '<div class="rhp-empty"><i class="bi bi-hourglass-split me-1"></i>Loading…</div>';
            return;
        }
        if (!state.items.length) {
            list.innerHTML = '<div class="rhp-empty"><i class="bi bi-bookmark me-1"></i>No snapshots yet. Name one above to save the current canvas.</div>';
            return;
        }
        list.innerHTML = state.items.map(r =>
            '<div class="rhp-item" data-id="' + r.id + '">' +
            '  <div class="rhp-item-main">' +
            '    <div class="rhp-item-title"><i class="bi bi-bookmark-fill me-1 text-warning"></i>' + escapeHtml(r.name || 'Untitled') + '</div>' +
            '    <div class="rhp-item-meta">' + timeAgo(r.createdAt) + (r.createdBy ? (' · ' + escapeHtml(r.createdBy)) : '') + '</div>' +
            '  </div>' +
            '  <div class="d-flex gap-1">' +
            '    <button class="btn btn-xs btn-outline-primary snap-restore" title="Restore"><i class="bi bi-arrow-counterclockwise"></i></button>' +
            '    <button class="btn btn-xs btn-outline-danger snap-delete" title="Delete"><i class="bi bi-trash"></i></button>' +
            '  </div>' +
            '</div>'
        ).join('');
        list.querySelectorAll('.snap-restore').forEach(b => b.addEventListener('click', function () {
            restore(parseInt(this.closest('.rhp-item').dataset.id, 10));
        }));
        list.querySelectorAll('.snap-delete').forEach(b => b.addEventListener('click', function () {
            remove(parseInt(this.closest('.rhp-item').dataset.id, 10));
        }));
    }

    async function load() {
        const g = reportGuid();
        if (!g) { state.items = []; render(); return; }
        state.loading = true; render();
        try {
            const resp = await fetch('/api/reports/' + encodeURIComponent(g) + '/revisions?kind=Snapshot');
            state.items = resp.ok ? await resp.json() : [];
        } catch { state.items = []; }
        state.loading = false;
        render();
    }

    async function saveCurrent() {
        const g = reportGuid();
        if (!g) { alert('Save the report first before creating a snapshot.'); return; }
        const input = document.getElementById('snap-new-name');
        const name = (input?.value || '').trim();
        if (!name) { input?.focus(); return; }
        const canvasJson = currentCanvasJson();
        try {
            const resp = await fetch('/api/reports/' + encodeURIComponent(g) + '/snapshots', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name, canvasJson })
            });
            if (!resp.ok) { alert('Failed to save snapshot.'); return; }
            if (input) input.value = '';
            await load();
        } catch { alert('Failed to save snapshot.'); }
    }

    async function restore(revId) {
        const g = reportGuid();
        if (!g) return;
        if (!confirm('Restore this snapshot? The current canvas will be auto-saved before replacing it.')) return;
        try {
            const resp = await fetch('/api/reports/' + encodeURIComponent(g) + '/revisions/' + revId + '/restore', { method: 'POST' });
            if (!resp.ok) { alert('Failed to restore snapshot.'); return; }
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
        } catch { alert('Failed to restore snapshot.'); }
    }

    async function remove(revId) {
        const g = reportGuid();
        if (!g) return;
        if (!confirm('Delete this snapshot permanently?')) return;
        try {
            const resp = await fetch('/api/reports/' + encodeURIComponent(g) + '/revisions/' + revId, { method: 'DELETE' });
            if (!resp.ok) { alert('Failed to delete snapshot.'); return; }
            await load();
        } catch { alert('Failed to delete snapshot.'); }
    }

    function show() { state.visible = true; ensurePanel().classList.add('open'); load(); setTimeout(() => document.getElementById('snap-new-name')?.focus(), 80); }
    function hide() { state.visible = false; const p = document.getElementById(PANEL_ID); if (p) p.classList.remove('open'); }
    function toggle() { state.visible ? hide() : show(); }

    global.snapshotManager = { show, hide, toggle, load, saveCurrent };

    function init() {
        const btn = document.getElementById('btn-snapshots');
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
