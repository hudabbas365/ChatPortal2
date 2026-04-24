/* Phase 36 — Column Profile Panel (A11 stats + A9 quality)
 * Adds an info-icon to every .data-field-row and a right-edge flyout
 * showing row count, non-null, distinct, min/max/avg (numeric) and top-N values.
 * After a profile is fetched, a colored null% badge is stamped on the row.
 *
 * Relies on:
 *   - #data-fields-list (created by propertiesPanel.renderDataFields)
 *   - .data-fields-group[data-table] wrappers grouping columns by table
 *   - window.propertiesPanel._schemaTables for dataType lookup
 *   - window.currentDatasourceId for the REST call
 *   - GET /api/datasources/{id}/profile?table=X&column=Y&numeric=bool&topN=N
 */
(function (global) {
    'use strict';

    const PANEL_ID = 'column-profile-panel';
    const cache = new Map(); // key `${dsId}|${table}|${column}` -> result

    function isNumeric(dt) {
        const t = (dt || '').toLowerCase();
        return t.includes('int') || t.includes('decimal') || t.includes('float') ||
               t.includes('numeric') || t.includes('money') || t.includes('double') || t.includes('real');
    }

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));
    }

    function fmtNum(v) {
        if (v == null || v === '') return '—';
        const n = Number(v);
        if (!isFinite(n)) return esc(v);
        if (Number.isInteger(n)) return n.toLocaleString();
        return n.toLocaleString(undefined, { maximumFractionDigits: 2 });
    }

    function lookupColumn(tableName, fieldName) {
        const pp = global.propertiesPanel;
        if (!pp) return null;
        if (pp._schemaTables && pp._schemaTables.length) {
            const tbl = pp._schemaTables.find(t => t.name === tableName) ||
                        pp._schemaTables.find(t => (t.columns || []).some(c => c.name === fieldName));
            if (tbl) {
                const col = (tbl.columns || []).find(c => c.name === fieldName);
                if (col) return { column: col, table: tbl };
            }
        }
        if (pp._schemaColumns && pp._schemaColumns.length) {
            const col = pp._schemaColumns.find(c => c.name === fieldName);
            if (col) return { column: col, table: null };
        }
        return null;
    }

    function ensurePanel() {
        let p = document.getElementById(PANEL_ID);
        if (p) return p;
        p = document.createElement('div');
        p.id = PANEL_ID;
        p.className = 'column-profile-panel';
        p.innerHTML =
            '<div class="cpp-header">' +
            '  <h6 class="mb-0"><i class="bi bi-bar-chart-line me-2"></i><span class="cpp-title">Column Profile</span></h6>' +
            '  <button class="cpp-close" title="Close"><i class="bi bi-x-lg"></i></button>' +
            '</div>' +
            '<div class="cpp-subheader text-muted small"><span class="cpp-subtitle"></span></div>' +
            '<div class="cpp-body"></div>';
        document.body.appendChild(p);
        p.querySelector('.cpp-close').addEventListener('click', hide);
        document.addEventListener('mousedown', (e) => {
            if (!p.classList.contains('visible')) return;
            if (p.contains(e.target)) return;
            if (e.target.closest && e.target.closest('.cpp-info-btn')) return;
            hide();
        });
        return p;
    }

    function show() { ensurePanel().classList.add('visible'); }
    function hide() { const p = document.getElementById(PANEL_ID); if (p) p.classList.remove('visible'); }

    function qualityClass(pct) {
        if (pct == null || isNaN(pct)) return '';
        if (pct < 5) return 'q-good';
        if (pct < 25) return 'q-warn';
        return 'q-bad';
    }

    function stampBadge(row, nullPct) {
        if (!row) return;
        const nameCell = row.querySelector('.data-field-name');
        if (!nameCell) return;
        let badge = nameCell.querySelector('.data-quality-badge');
        if (!badge) {
            badge = document.createElement('span');
            badge.className = 'data-quality-badge';
            nameCell.appendChild(badge);
        }
        const pct = Math.round(nullPct);
        badge.textContent = pct + '% null';
        badge.className = 'data-quality-badge ' + qualityClass(pct);
        badge.title = pct + '% of rows are null';
    }

    function renderBody(panel, table, column, data) {
        const body = panel.querySelector('.cpp-body');
        panel.querySelector('.cpp-title').textContent = column;
        panel.querySelector('.cpp-subtitle').textContent = table ? (table + ' · column profile') : 'column profile';

        if (!data) {
            body.innerHTML = '<div class="p-3 text-muted small">Loading…</div>';
            return;
        }
        if (data.supported === false) {
            body.innerHTML = '<div class="p-3 text-muted small">' + esc(data.reason || 'Profiling is not supported for this datasource.') + '</div>';
            return;
        }
        if (data.success === false) {
            body.innerHTML = '<div class="p-3 text-danger small">' + esc(data.error || 'Profile failed.') + '</div>';
            return;
        }

        const total = Number(data.rowCount || 0);
        const nonNull = Number(data.nonNullCount || 0);
        const nullCount = Math.max(0, total - nonNull);
        const nullPct = total > 0 ? (nullCount * 100 / total) : 0;
        const distinct = Number(data.distinctCount || 0);
        const distinctPct = nonNull > 0 ? (distinct * 100 / nonNull) : 0;

        const topRows = Array.isArray(data.topValues) ? data.topValues : [];
        const maxFreq = topRows.reduce((m, r) => Math.max(m, Number(r.count || 0)), 0) || 1;
        const topHtml = topRows.length
            ? '<div class="cpp-section-title">Top values</div>' +
              '<ul class="cpp-top-list">' +
                topRows.map(r => {
                    const w = Math.round((Number(r.count || 0) * 100) / maxFreq);
                    return '<li>' +
                        '<span class="cpp-top-bar" style="width:' + w + '%"></span>' +
                        '<span class="cpp-top-label" title="' + esc(r.value) + '">' + (r.value == null ? '<em>null</em>' : esc(r.value)) + '</span>' +
                        '<span class="cpp-top-count">' + fmtNum(r.count) + '</span>' +
                    '</li>';
                }).join('') +
              '</ul>'
            : '';

        const numericHtml = data.numeric ?
            '<div class="cpp-stat"><span>Min</span><b>' + fmtNum(data.min) + '</b></div>' +
            '<div class="cpp-stat"><span>Max</span><b>' + fmtNum(data.max) + '</b></div>' +
            '<div class="cpp-stat"><span>Average</span><b>' + fmtNum(data.avg) + '</b></div>' : '';

        body.innerHTML =
            '<div class="cpp-stats">' +
            '  <div class="cpp-stat"><span>Rows</span><b>' + fmtNum(total) + '</b></div>' +
            '  <div class="cpp-stat"><span>Non-null</span><b>' + fmtNum(nonNull) + '</b></div>' +
            '  <div class="cpp-stat"><span>Distinct</span><b>' + fmtNum(distinct) + ' <small class="text-muted">(' + distinctPct.toFixed(1) + '%)</small></b></div>' +
               numericHtml +
            '</div>' +
            '<div class="cpp-null-bar" title="' + nullPct.toFixed(1) + '% null">' +
            '  <div class="cpp-null-fill ' + qualityClass(nullPct) + '" style="width:' + nullPct.toFixed(1) + '%"></div>' +
            '  <span class="cpp-null-label">' + nullPct.toFixed(1) + '% null</span>' +
            '</div>' +
            topHtml;
    }

    async function openProfile(row, btn) {
        const field = row?.dataset?.field;
        if (!field) return;
        const groupEl = row.closest('.data-fields-group');
        const table = groupEl?.dataset?.table || '';
        const dsId = global.currentDatasourceId || global.propertiesPanel?.currentChart?.datasourceId || null;
        if (!dsId) {
            const panel = ensurePanel();
            show();
            renderBody(panel, table, field, { success: false, error: 'No datasource selected.' });
            return;
        }

        const info = lookupColumn(table, field);
        const numeric = isNumeric(info?.column?.dataType || '');
        const key = dsId + '|' + table + '|' + field;

        const panel = ensurePanel();
        show();
        if (cache.has(key)) {
            const cached = cache.get(key);
            renderBody(panel, table, field, cached);
            return;
        }
        renderBody(panel, table, field, null); // loading
        btn?.classList?.add('loading');
        try {
            const qs = new URLSearchParams({ table: table || '', column: field, numeric: String(numeric), topN: '5' });
            const resp = await fetch('/api/datasources/' + dsId + '/profile?' + qs.toString());
            const data = await resp.json();
            cache.set(key, data);
            renderBody(panel, table, field, data);
            if (data && data.success !== false && data.supported !== false) {
                const total = Number(data.rowCount || 0);
                const nonNull = Number(data.nonNullCount || 0);
                const nullPct = total > 0 ? ((total - nonNull) * 100 / total) : 0;
                stampBadge(row, nullPct);
            }
        } catch (e) {
            renderBody(panel, table, field, { success: false, error: (e && e.message) || 'Request failed.' });
        } finally {
            btn?.classList?.remove('loading');
        }
    }

    function injectInfoButtons(scope) {
        const rows = (scope || document).querySelectorAll
            ? (scope || document).querySelectorAll('.data-field-row')
            : [];
        rows.forEach(row => {
            if (row._cppWired) return;
            row._cppWired = true;
            const nameCell = row.querySelector('.data-field-name');
            if (!nameCell) return;
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'cpp-info-btn';
            btn.title = 'View column profile';
            btn.innerHTML = '<i class="bi bi-info-circle"></i>';
            btn.addEventListener('mousedown', e => e.stopPropagation()); // avoid drag
            btn.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                openProfile(row, btn);
            });
            nameCell.appendChild(btn);
        });
    }

    function observe() {
        const container = document.getElementById('data-fields-list');
        if (!container) return;
        injectInfoButtons(container);
        if (container._cppObserver) return;
        const obs = new MutationObserver(() => injectInfoButtons(container));
        obs.observe(container, { childList: true, subtree: true });
        container._cppObserver = obs;
    }

    function init() {
        observe();
        // Re-check periodically for late mounts of the panel.
        const kicker = setInterval(() => {
            if (document.getElementById('data-fields-list')) { observe(); clearInterval(kicker); }
        }, 500);
        setTimeout(() => clearInterval(kicker), 15000);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();

    global.columnProfilePanel = { open: openProfile, refresh: observe };
})(window);
