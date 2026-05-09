// Transform Workbench — full-page Power-Query-style editor.
// Layout: Queries (left) | Ribbon + Data Preview / TOML (center) | Applied Steps + AI Assistant (right).
//
// Concepts:
//  - "Query" = a Datasource. Each query owns its own ordered list of `Applied Steps`.
//  - "Step"  = one transform rule (TOML [[rules]] block) with a friendly label.
//  - The Applied Steps list is the source of truth; TOML is rebuilt from it.
//  - The TOML editor view lets users hand-edit (DAX-like expressions) and Apply
//    back to Steps via a parser. Publish stores the published TOML on the DS so
//    downstream services (Auto Reports, dashboard tiles, etc.) see the transform.
//  - Server endpoints reused: GET /api/datasources/{guid}/transform,
//    POST .../transform/draft, .../transform/preview, .../transform/validate,
//    .../transform/publish.

(function () {
    'use strict';

    const $ = (id) => document.getElementById(id);
    const esc = (v) => String(v ?? '').replace(/[&<>"']/g, s => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[s]));
    const csrfToken = () => {
        const m = document.cookie.match(/(?:^|;\s*)XSRF-TOKEN=([^;]+)/);
        return m ? decodeURIComponent(m[1]) : '';
    };

    // ── Ribbon definition (Power-Query-style groups) ───────────────────
    // Each item: { op, label, icon, group }. `op` matches the [[rules]].type
    // consumed by Services/Transforms/TransformWorkbenchServices.cs handlers.
    const RIBBON = {
        home: [
            { group: 'Manage', items: [
                { op: 'add_datasource',   label: 'Add datasource',   icon: 'bi-database-add',    kind: 'action' },
                { op: 'refresh',          label: 'Refresh preview',  icon: 'bi-arrow-clockwise', kind: 'action' },
                { op: 'remove_columns',   label: 'Choose columns',   icon: 'bi-funnel' }
            ]},
            { group: 'Reduce rows', items: [
                { op: 'remove_duplicates',  label: 'Remove duplicates', icon: 'bi-files' },
                { op: 'filter',             label: 'Filter rows',       icon: 'bi-funnel-fill' },
                { op: 'remove_rows',        label: 'Remove top N',      icon: 'bi-dash-square' },
                { op: 'remove_bottom_rows', label: 'Remove bottom N',   icon: 'bi-dash-square-dotted' },
                { op: 'remove_blank_rows',  label: 'Remove blank rows', icon: 'bi-eraser-fill' },
                { op: 'select_rows',        label: 'Keep matching',     icon: 'bi-check2-square' }
            ]},
            { group: 'Sort', items: [
                { op: 'sort_asc',  label: 'Sort ↑', icon: 'bi-sort-down' },
                { op: 'sort_desc', label: 'Sort ↓', icon: 'bi-sort-up'   }
            ]},
            { group: 'Combine', items: [
                { op: 'merge',  label: 'Merge queries',  icon: 'bi-bezier2' },
                { op: 'append', label: 'Append queries', icon: 'bi-layout-three-columns' }
            ]}
        ],
        transform: [
            { group: 'Table', items: [
                { op: 'group_by',         label: 'Group by',     icon: 'bi-collection' },
                { op: 'pivot',            label: 'Pivot column', icon: 'bi-arrow-down-up' },
                { op: 'unpivot',          label: 'Unpivot',      icon: 'bi-arrows-collapse-vertical' },
                { op: 'transpose',        label: 'Transpose',    icon: 'bi-arrows-angle-expand' },
                { op: 'reverse',          label: 'Reverse rows', icon: 'bi-arrow-counterclockwise' },
                { op: 'count_rows',       label: 'Count rows',   icon: 'bi-123' },
                { op: 'promote_headers',  label: 'First row as headers', icon: 'bi-arrow-up-square' },
                { op: 'demote_headers',   label: 'Demote headers',       icon: 'bi-arrow-down-square' }
            ]},
            { group: 'Any column', items: [
                { op: 'rename_column',     label: 'Rename',         icon: 'bi-input-cursor-text' },
                { op: 'duplicate_column',  label: 'Duplicate',      icon: 'bi-copy' },
                { op: 'replace_values',    label: 'Replace values', icon: 'bi-arrow-left-right' },
                { op: 'split_column',      label: 'Split column',   icon: 'bi-distribute-horizontal' },
                { op: 'merge_columns',     label: 'Merge columns',  icon: 'bi-link-45deg' },
                { op: 'fill_down',         label: 'Fill down',      icon: 'bi-arrow-down-short' },
                { op: 'fill_up',           label: 'Fill up',        icon: 'bi-arrow-up-short' },
                { op: 'reorder_columns',   label: 'Reorder',        icon: 'bi-arrows-move' },
                { op: 'change_type',       label: 'Change type',    icon: 'bi-shuffle' }
            ]}
        ],
        addcol: [
            { group: 'General', items: [
                { op: 'add_column',     label: 'Custom column',    icon: 'bi-plus-square' },
                { op: 'derived_field',  label: 'Custom (DAX-like)', icon: 'bi-calculator' },
                { op: 'index_column',   label: 'Index column',     icon: 'bi-list-ol' },
                { op: 'conditional',    label: 'Conditional column', icon: 'bi-diagram-2' }
            ]}
        ],
        text: [
            { group: 'Text', items: [
                { op: 'text_upper',    label: 'UPPERCASE',  icon: 'bi-type' },
                { op: 'text_lower',    label: 'lowercase',  icon: 'bi-type' },
                { op: 'text_proper',   label: 'Capitalize', icon: 'bi-type' },
                { op: 'text_trim',     label: 'Trim',       icon: 'bi-scissors' },
                { op: 'text_clean',    label: 'Clean',      icon: 'bi-eraser' },
                { op: 'text_replace',  label: 'Replace',    icon: 'bi-arrow-left-right' },
                { op: 'text_extract',  label: 'Extract',    icon: 'bi-vector-pen' },
                { op: 'text_length',   label: 'Length',     icon: 'bi-rulers' },
                { op: 'text_contains', label: 'Contains',   icon: 'bi-search' }
            ]}
        ],
        number: [
            { group: 'Number', items: [
                { op: 'num_round',     label: 'Round',          icon: 'bi-circle-half' },
                { op: 'num_round_up',  label: 'Round up',       icon: 'bi-arrow-up' },
                { op: 'num_round_down',label: 'Round down',     icon: 'bi-arrow-down' },
                { op: 'num_abs',       label: 'Absolute value', icon: 'bi-plus-slash-minus' },
                { op: 'num_power',     label: 'Power',          icon: 'bi-superscript' },
                { op: 'num_log',       label: 'Logarithm',      icon: 'bi-graph-up' },
                { op: 'num_mod',       label: 'Modulo',         icon: 'bi-percent' }
            ]}
        ],
        datetime: [
            { group: 'Date', items: [
                { op: 'date_year',          label: 'Year',           icon: 'bi-calendar' },
                { op: 'date_month',         label: 'Month',          icon: 'bi-calendar2-month' },
                { op: 'date_day',           label: 'Day',            icon: 'bi-calendar-day' },
                { op: 'date_add_days',      label: 'Add days',       icon: 'bi-calendar-plus' },
                { op: 'date_add_months',    label: 'Add months',     icon: 'bi-calendar-plus' },
                { op: 'date_start_of_month', label: 'Start of month', icon: 'bi-calendar-event' },
                { op: 'date_end_of_month',  label: 'End of month',   icon: 'bi-calendar-event' },
                { op: 'date_now',           label: 'Now',            icon: 'bi-clock' }
            ]},
            { group: 'Time', items: [
                { op: 'time_hour',   label: 'Hour',   icon: 'bi-clock' },
                { op: 'time_minute', label: 'Minute', icon: 'bi-clock' },
                { op: 'time_second', label: 'Second', icon: 'bi-clock' }
            ]}
        ],
        combine: [
            { group: 'Combine queries', items: [
                { op: 'merge',         label: 'Merge',          icon: 'bi-bezier2' },
                { op: 'append',        label: 'Append',         icon: 'bi-layout-three-columns' },
                { op: 'nested_join',   label: 'Nested join',    icon: 'bi-diagram-3' },
                { op: 'combine_files', label: 'Combine tables', icon: 'bi-collection-fill' }
            ]}
        ],
        view: [
            { group: 'View', items: [
                { op: 'show_toml',  label: 'Toggle TOML',     icon: 'bi-code-square', kind: 'action' },
                { op: 'show_steps', label: 'Show steps pane', icon: 'bi-list-ol',     kind: 'action' }
            ]}
        ]
    };

    // ── Default templates per op so a click yields a usable rule. ───────
    // NOTE: Most ops are now built data-aware via smartDefaults() reading the
    // live preview. defaultParamsFor() is the fallback for ops that don't need
    // any column reference (transpose, reverse, etc.) and a last-resort for
    // brand-new queries with no preview yet — values here are deliberately
    // empty/blank so the step editor (which auto-opens) requires the user to
    // fill them in rather than inheriting bogus example column names.
    function defaultParamsFor(op, ctx) {
        const otherDs = (ctx?.otherQueries || [])[0]?.name || '';
        switch (op) {
            // home / reduce
            case 'remove_duplicates':  return { keys: [] };
            case 'filter':             return { expression: '' };
            case 'remove_rows':        return { count: 1, position: 'top' };
            case 'remove_bottom_rows': return { count: 1, position: 'bottom' };
            case 'remove_blank_rows':  return {};
            case 'select_rows':        return { expression: '' };
            case 'remove_columns':    return { columns: [] };
            case 'sort_asc':          return { column: '', direction: 'asc' };
            case 'sort_desc':         return { column: '', direction: 'desc' };
            // table
            case 'group_by':       return { keys: [], aggregations: [{ name: 'Count', op: 'count' }] };
            case 'pivot':          return { column: '', values_column: '', agg: 'sum' };
            case 'unpivot':        return { columns: [], attribute_name: 'Attribute', value_name: 'Value' };
            case 'transpose':      return {};
            case 'reverse':        return {};
            case 'count_rows':     return { target_field: 'RowCount' };
            // any column
            case 'rename_column':    return { from: '', to: '' };
            case 'duplicate_column': return { source: '', target_field: '' };
            case 'replace_values':   return { column: '', from: '', to: '' };
            case 'fill_down':        return { column: '' };
            case 'fill_up':          return { column: '' };
            case 'reorder_columns':  return { order: [] };
            case 'change_type':      return { column: '', type: 'text' };
            case 'split_column':     return { column: '', delimiter: ' ', into: [] };
            case 'merge_columns':    return { columns: [], separator: ' ', target_field: '' };
            case 'promote_headers':  return {};
            case 'demote_headers':   return {};
            // add column — empty target/expression so the editor demands input
            case 'add_column':    return { target_field: '', value: '' };
            case 'derived_field': return { target_field: '', expression: '' };
            case 'index_column':  return { target_field: 'Index', start: 1, step: 1 };
            case 'conditional':   return { target_field: '', expression: '' };
            // text
            case 'text_upper':    return { column: '' };
            case 'text_lower':    return { column: '' };
            case 'text_proper':   return { column: '' };
            case 'text_trim':     return { column: '' };
            case 'text_clean':    return { column: '' };
            case 'text_replace':  return { column: '', from: '', to: '' };
            case 'text_extract':  return { column: '', start: 0, length: 5, target_field: '' };
            case 'text_length':   return { column: '', target_field: '' };
            case 'text_contains': return { column: '', value: '', target_field: '' };
            // number
            case 'num_round':       return { column: '', digits: 2 };
            case 'num_round_up':    return { column: '' };
            case 'num_round_down':  return { column: '' };
            case 'num_abs':         return { column: '' };
            case 'num_power':       return { column: '', exponent: 2 };
            case 'num_log':         return { column: '', base: 10 };
            case 'num_mod':         return { column: '', divisor: 1 };
            // date / time
            case 'date_year':           return { column: '', target_field: 'Year' };
            case 'date_month':          return { column: '', target_field: 'Month' };
            case 'date_day':            return { column: '', target_field: 'Day' };
            case 'date_add_days':       return { column: '', days: 30 };
            case 'date_add_months':     return { column: '', months: 1 };
            case 'date_start_of_month': return { column: '', target_field: 'MonthStart' };
            case 'date_end_of_month':   return { column: '', target_field: 'MonthEnd' };
            case 'date_now':            return { target_field: 'Now' };
            case 'time_hour':           return { column: '', target_field: 'Hour' };
            case 'time_minute':         return { column: '', target_field: 'Minute' };
            case 'time_second':         return { column: '', target_field: 'Second' };
            // combine
            case 'merge':         return { right: otherDs, left_key: '', right_key: '', how: 'inner' };
            case 'append':        return { source: otherDs, align_columns: true };
            case 'nested_join':   return { right: otherDs, left_key: '', right_key: '', target_field: 'Joined' };
            case 'combine_files': return { sources: otherDs ? [otherDs] : [] };
            default: return {};
        }
    }

    function labelFor(op, params) {
        const map = {
            remove_duplicates: 'Removed duplicates',
            filter: 'Filtered rows',
            remove_rows: `Removed top ${params?.count ?? 'N'} rows`,
            remove_bottom_rows: `Removed bottom ${params?.count ?? 'N'} rows`,
            remove_blank_rows: 'Removed blank rows',
            select_rows: 'Kept rows matching',
            remove_columns: 'Removed columns',
            sort_asc: 'Sorted ascending',
            sort_desc: 'Sorted descending',
            group_by: 'Grouped rows',
            pivot: 'Pivoted column',
            unpivot: 'Unpivoted columns',
            transpose: 'Transposed table',
            reverse: 'Reversed rows',
            count_rows: 'Counted rows',
            rename_column: `Renamed ${params?.from || ''} → ${params?.to || ''}`,
            duplicate_column: `Duplicated ${params?.source || ''}`,
            replace_values: `Replaced values in ${params?.column || ''}`,
            fill_down: `Filled down ${params?.column || ''}`,
            fill_up:   `Filled up ${params?.column || ''}`,
            reorder_columns: 'Reordered columns',
            change_type: `Changed type of ${params?.column || ''}`,
            split_column: `Split ${params?.column || ''}`,
            merge_columns: `Merged into "${params?.target_field || ''}"`,
            promote_headers: 'Used first row as headers',
            demote_headers: 'Demoted headers',
            add_column:    `Added column "${params?.target_field || ''}"`,
            derived_field: `Added derived "${params?.target_field || ''}"`,
            index_column:  `Added index "${params?.target_field || ''}"`,
            conditional:   `Added conditional "${params?.target_field || ''}"`,
            merge:  `Merged with ${params?.right || ''}`,
            append: `Appended ${params?.source || ''}`,
            nested_join: `Nested join with ${params?.right || ''}`,
            combine_files: 'Combined tables'
        };
        if (map[op]) return map[op];
        if (op.startsWith('text_'))   return `Text · ${op.replace(/^text_/, '')} on ${params?.column || ''}`;
        if (op.startsWith('num_'))    return `Number · ${op.replace(/^num_/, '')} on ${params?.column || ''}`;
        if (op.startsWith('date_'))   return `Date · ${op.replace(/^date_/, '')}`;
        if (op.startsWith('time_'))   return `Time · ${op.replace(/^time_/, '')}`;
        return op;
    }

    // ── State ──────────────────────────────────────────────────────────
    const state = {
        wsGuid: '',
        queries: [],         // [{ id, guid, name, type, steps:[{op,enabled,params}] }]
        activeQueryGuid: null,
        view: 'preview'      // 'preview' | 'toml'
    };

    function activeQuery() {
        return state.queries.find(q => q.guid === state.activeQueryGuid) || null;
    }

    // ── Init ───────────────────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', init);

    function init() {
        const shell = $('twpShell');
        state.wsGuid = shell.dataset.wsGuid || '';
        wireRibbon();
        wireViewTabs();
        wireToolbar();
        wireAi();
        wireStepEditor();
        // Bridge for the enhancements module (column-header menu, intellisense,
        // DAX editor, AI capabilities pane). External code can:
        //  • read `window.TWP.activeQuery()` for the current schema/columns
        //  • dispatch a `twp:add-step` CustomEvent({detail:{op,params,enabled?}}) to append a step
        //  • listen for `twp:preview-rendered` to decorate the preview table
        window.TWP = {
            state,
            activeQuery,
            labelFor,
            defaultParamsFor,
            addStep: (step) => {
                const q = activeQuery();
                if (!q || !step || !step.op) return false;
                q.steps.push({ op: step.op, enabled: step.enabled !== false, params: step.params || {} });
                onStepsChanged();
                return true;
            },
            // Inspect the most-recently-applied sort step for `column`, if any.
            getSortState: (column) => {
                const q = activeQuery();
                if (!q) return null;
                for (let i = q.steps.length - 1; i >= 0; i--) {
                    const s = q.steps[i];
                    if (s && (s.op === 'sort_asc' || s.op === 'sort_desc') && s.params && s.params.column === column) {
                        return { dir: s.op === 'sort_asc' ? 'asc' : 'desc', stepIdx: i };
                    }
                }
                return null;
            },
            // Cycle column sort: none → asc → desc → none.
            toggleSort: (column) => {
                const q = activeQuery();
                if (!q) return;
                const st = window.TWP.getSortState(column);
                if (!st) {
                    q.steps.push({ op: 'sort_asc', enabled: true, params: { column, direction: 'asc' } });
                } else if (st.dir === 'asc') {
                    q.steps[st.stepIdx] = { op: 'sort_desc', enabled: true, params: { column, direction: 'desc' } };
                } else {
                    q.steps.splice(st.stepIdx, 1);
                }
                onStepsChanged();
            },
            refresh: () => onStepsChanged()
        };
        document.addEventListener('twp:add-step', (e) => {
            if (e && e.detail) window.TWP.addStep(e.detail);
        });
        // Delegated click handler for column / row action buttons rendered
        // by renderPreview(). Attached once on the table; the inner HTML is
        // recycled on each preview but the listener stays put.
        const previewTbl = $('twpPreviewTable');
        if (previewTbl) previewTbl.addEventListener('click', onPreviewTableClick);
        // Keyboard shortcuts:
        //   F5            \u2192 run preview
        //   Ctrl/Cmd + S  \u2192 publish
        //   Ctrl/Cmd + Shift + P \u2192 focus AI assistant
        document.addEventListener('keydown', (e) => {
            const tag = (e.target && e.target.tagName) || '';
            const inField = tag === 'INPUT' || tag === 'TEXTAREA' || (e.target && e.target.isContentEditable);
            const mod = e.ctrlKey || e.metaKey;
            if (e.key === 'F5' && !inField) { e.preventDefault(); runPreview(); return; }
            if (mod && !e.shiftKey && (e.key === 's' || e.key === 'S')) { e.preventDefault(); runPublish(); return; }
            if (mod && e.shiftKey && (e.key === 'p' || e.key === 'P')) {
                e.preventDefault();
                const ai = $('twpAiInput'); if (ai) { ai.focus(); ai.select && ai.select(); }
            }
        });
        loadQueries().catch(err => setStatus('Failed to load queries: ' + err.message, 'error'));
    }

    function setStatus(msg, kind) {
        const el = $('twpGlobalStatus');
        if (el) {
            el.className = 'twp-status' + (kind ? ' twp-status-' + kind : '');
            el.textContent = msg || '';
            if (msg && kind === 'success') {
                setTimeout(() => { if (el.textContent === msg) el.textContent = ''; }, 2400);
            }
        }
        const sm = $('twpStatusMessage');
        if (sm) {
            sm.className = 'twp-status-cell twp-status-msg' + (kind ? ' kind-' + kind : '');
            sm.textContent = msg || '';
        }
    }

    function updateStatusBar(opts) {
        opts = opts || {};
        const q = activeQuery();
        const qn = $('twpStatusQuery'); if (qn) qn.textContent = q ? q.name : '—';
        const sc = $('twpStatusSteps'); if (sc) sc.textContent = q ? q.steps.length : 0;
        if (opts.rows != null)  { const r = $('twpStatusRows'); if (r) r.textContent = opts.rows; }
        if (opts.cols != null)  { const c = $('twpStatusCols'); if (c) c.textContent = opts.cols; }
        if (opts.clientMs != null) {
            const d = $('twpStatusDuration');
            if (d) {
                const srv = (opts.serverMs != null) ? ` (server ${Math.round(opts.serverMs)} ms)` : '';
                d.textContent = `${Math.round(opts.clientMs)} ms${srv}`;
            }
        }
        if (opts.validity) {
            const v = $('twpStatusValidity');
            if (v) {
                v.textContent = opts.validity.text || 'Ready';
                v.className = opts.validity.kind ? ('kind-' + opts.validity.kind) : '';
            }
        }
    }

    function setProgress(on) {
        const p = $('twpProgress');
        if (p) p.style.display = on ? '' : 'none';
    }

    // ── Queries ────────────────────────────────────────────────────────
    async function loadQueries() {
        setStatus('Loading workspace…');
        const r = await fetch('/api/workspaces/' + encodeURIComponent(state.wsGuid));
        if (!r.ok) throw new Error('Workspace fetch failed (' + r.status + ')');
        const ws = await r.json();
        // Cache numeric workspace id so the "Add datasource" easy-step
        // can pass `WorkspaceId` (int) to POST /api/datasources without a
        // second round-trip.
        state.wsId = ws.id || ws.Id || null;
        const dsList = ws.datasources || [];

        // Hydrate steps from each DS's published transform (best-effort).
        const hydrated = await Promise.all(dsList.map(async d => {
            let steps = [];
            try {
                const tr = await fetch('/api/datasources/' + encodeURIComponent(d.guid || d.id) + '/transform');
                if (tr.ok) {
                    const body = await tr.json();
                    steps = parseStepsFromBody(body) || [];
                }
            } catch { /* ignore */ }
            return { id: d.id, guid: d.guid, name: d.name, type: d.type, steps };
        }));

        state.queries = hydrated;
        if (!state.activeQueryGuid && hydrated.length > 0) {
            state.activeQueryGuid = hydrated[0].guid;
        }
        renderQueriesList();
        renderRibbon();
        renderStepsList();
        renderTomlFromSteps();
        updateStatusBar({});
        setStatus(hydrated.length === 0 ? 'No datasources in this workspace.' : '');
        // Auto-run preview for the initially-active query so the user sees
        // data immediately without having to click Preview.
        if (activeQuery()) schedulePreview();
    }

    // The /transform endpoint shape varies; do a best-effort parse so we
    // don't blow up if the server hasn't seen this DS yet.
    function parseStepsFromBody(body) {
        if (!body) return [];
        if (Array.isArray(body.steps)) return body.steps;
        if (typeof body.stepsJson === 'string') {
            try { return JSON.parse(body.stepsJson) || []; } catch { return []; }
        }
        return [];
    }

    function renderQueriesList() {
        const list = $('twpQueriesList');
        const count = $('twpQueryCount');
        if (count) count.textContent = state.queries.length;
        if (!list) return;
        if (state.queries.length === 0) {
            list.innerHTML = `<div class="twp-empty-mini">No queries. Add a datasource from the workspace home.</div>`;
            return;
        }
        list.innerHTML = state.queries.map(q => `
            <div class="twp-query-item ${q.guid === state.activeQueryGuid ? 'active' : ''}" data-q="${esc(q.guid)}">
                <div class="twp-query-icon"><i class="bi bi-database"></i></div>
                <div class="twp-query-meta">
                    <div class="twp-query-name">${esc(q.name)}</div>
                    <div class="twp-query-sub">${esc(q.type)} · ${q.steps.length} step${q.steps.length === 1 ? '' : 's'}</div>
                </div>
            </div>
        `).join('');
        list.querySelectorAll('.twp-query-item').forEach(el => {
            el.addEventListener('click', () => {
                state.activeQueryGuid = el.dataset.q;
                renderQueriesList();
                renderStepsList();
                renderTomlFromSteps();
                clearPreview();
                updateStatusBar({ rows: 0, cols: 0 });
                schedulePreview();
            });
        });
    }

    // ── Ribbon ─────────────────────────────────────────────────────────
    function wireRibbon() {
        $('twpRibbonTabs').addEventListener('click', e => {
            const b = e.target.closest('.twp-ribbon-tab');
            if (!b) return;
            $('twpRibbonTabs').querySelectorAll('.twp-ribbon-tab').forEach(x => x.classList.remove('active'));
            b.classList.add('active');
            renderRibbon(b.dataset.ribbon);
        });
    }

    function renderRibbon(tabKey) {
        const key = tabKey || ($('twpRibbonTabs').querySelector('.twp-ribbon-tab.active')?.dataset.ribbon) || 'home';
        const groups = RIBBON[key] || [];
        const body = $('twpRibbonBody');
        body.innerHTML = groups.map(g => `
            <div class="twp-ribbon-group">
                <div class="twp-ribbon-group-items">
                    ${g.items.map(it => `
                        <button class="twp-ribbon-btn" data-op="${esc(it.op)}" data-kind="${esc(it.kind || 'rule')}" title="${esc(it.label)}">
                            <i class="bi ${esc(it.icon)}"></i>
                            <span>${esc(it.label)}</span>
                        </button>
                    `).join('')}
                </div>
                <div class="twp-ribbon-group-label">${esc(g.group)}</div>
            </div>
        `).join('');
        body.querySelectorAll('.twp-ribbon-btn').forEach(btn => {
            btn.addEventListener('click', () => onRibbonClick(btn.dataset.op, btn.dataset.kind));
        });
    }

    function onRibbonClick(op, kind) {
        if (kind === 'action') {
            if (op === 'refresh')        return runPreview();
            if (op === 'show_toml')      return setView('toml');
            if (op === 'show_steps')     return setView('preview');
            if (op === 'add_datasource') return openAddDatasourceDialog();
            return;
        }
        // Rule op — append a step to the active query.
        const q = activeQuery();
        if (!q) { setStatus('Pick a query first.', 'warn'); return; }
        const cols  = getColumnsForActive();
        const types = getColumnTypes();
        const ctx = {
            otherQueries: state.queries.filter(x => x.guid !== q.guid),
            cols, types
        };
        const sd = smartDefaults(op, ctx);
        if (sd.error) { setStatus(sd.error, 'warn'); return; }
        const step = { op, enabled: true, params: sd.params || {} };
        q.steps.push(step);
        renderQueriesList();
        renderStepsList();
        renderTomlFromSteps();
        // Special UX hint for promote_headers — the rename happens server-
        // side, but the original first row remains in the data; tell the
        // user how to drop it.
        if (op === 'promote_headers') {
            setStatus('First row is now the header. Click the × on row 1 to drop the duplicate row.', 'success');
        }
        // Open the editor immediately so the user can refine the new step.
        openStepEditor(q.steps.length - 1);
    }

    // ── Add Datasource (easy step) ─────────────────────────────────────
    // Lightweight modal launched from the Home ribbon. The list of
    // datasource families AND their connection-parameter fields are
    // fetched from GET /api/datasources/type-schemas, which aggregates the
    // schema declared by each registered IDatasourceTypeService on the
    // server. Adding a new service implementation in DI automatically
    // surfaces it here — no client edits required.
    let _dsTypeSchemas = null; // cached on first open

    async function loadDsTypeSchemas() {
        if (_dsTypeSchemas) return _dsTypeSchemas;
        try {
            const r = await fetch('/api/datasources/type-schemas', { credentials: 'same-origin' });
            if (r.ok) {
                const data = await r.json();
                if (Array.isArray(data) && data.length > 0) { _dsTypeSchemas = data; return data; }
            }
        } catch { /* fall through to legacy fallback */ }
        // Legacy fallback: derive a minimal schema from /api/datasources/types
        // (older deployments where type-schemas isn't deployed yet).
        try {
            const r = await fetch('/api/datasources/types', { credentials: 'same-origin' });
            if (r.ok) {
                const names = await r.json();
                _dsTypeSchemas = (names || []).map(t => ({
                    type: t,
                    parameters: [{ key: 'connectionString', label: 'Connection details', type: 'multiline', required: true }]
                }));
                return _dsTypeSchemas;
            }
        } catch { /* offline */ }
        _dsTypeSchemas = [];
        return _dsTypeSchemas;
    }

    function fieldHtmlForParam(p, value) {
        const id = 'twpAddDs_' + p.key;
        const ph = p.placeholder ? esc(p.placeholder) : '';
        const reqMark = p.required ? ' <span class="text-danger">*</span>' : '';
        const lbl = `<label class="form-label small fw-semibold" for="${id}">${esc(p.label || p.key)}${reqMark}</label>`;
        const help = p.help ? `<div class="form-text small text-muted">${esc(p.help)}</div>` : '';
        const v = value ?? '';
        let input;
        switch ((p.type || 'text').toLowerCase()) {
            case 'multiline':
                input = `<textarea id="${id}" class="form-control form-control-sm" rows="3" spellcheck="false" data-param="${esc(p.key)}" placeholder="${ph}">${esc(v)}</textarea>`;
                break;
            case 'password':
                input = `<input id="${id}" type="password" class="form-control form-control-sm" data-param="${esc(p.key)}" placeholder="${ph}" value="${esc(v)}" autocomplete="new-password">`;
                break;
            case 'url':
                input = `<input id="${id}" type="url" class="form-control form-control-sm" data-param="${esc(p.key)}" placeholder="${ph}" value="${esc(v)}">`;
                break;
            case 'select': {
                const opts = (p.options || []).map(o => `<option value="${esc(o)}" ${String(v) === String(o) ? 'selected' : ''}>${esc(o)}</option>`).join('');
                input = `<select id="${id}" class="form-select form-select-sm" data-param="${esc(p.key)}">${opts}</select>`;
                break;
            }
            case 'text':
            default:
                input = `<input id="${id}" type="text" class="form-control form-control-sm" data-param="${esc(p.key)}" placeholder="${ph}" value="${esc(v)}">`;
                break;
        }
        return `<div class="twp-field">${lbl}${input}${help}</div>`;
    }

    async function openAddDatasourceDialog() {
        const schemas = await loadDsTypeSchemas();
        if (!schemas || schemas.length === 0) {
            setStatus('No datasource types are registered on the server.', 'error');
            return;
        }

        let modal = document.getElementById('twpAddDsModal');
        if (!modal) {
            modal = document.createElement('div');
            modal.id = 'twpAddDsModal';
            modal.className = 'twp-add-ds-modal';
            modal.innerHTML = `
                <div class="twp-add-ds-card" role="dialog" aria-modal="true" aria-labelledby="twpAddDsTitle">
                    <div class="twp-add-ds-head">
                        <span id="twpAddDsTitle"><i class="bi bi-database-add me-2"></i>Add datasource</span>
                        <button type="button" class="twp-add-ds-close" aria-label="Close">&times;</button>
                    </div>
                    <div class="twp-add-ds-body">
                        <div class="twp-field">
                            <label class="form-label small fw-semibold" for="twpAddDsName">Name <span class="text-danger">*</span></label>
                            <input id="twpAddDsName" type="text" class="form-control form-control-sm" placeholder="My data">
                        </div>
                        <div class="twp-field">
                            <label class="form-label small fw-semibold" for="twpAddDsType">Type <span class="text-danger">*</span></label>
                            <select id="twpAddDsType" class="form-select form-select-sm"></select>
                        </div>
                        <div id="twpAddDsParams"></div>
                        <div class="twp-add-ds-error small text-danger" id="twpAddDsError" style="display:none;"></div>
                    </div>
                    <div class="twp-add-ds-foot">
                        <button type="button" class="btn btn-sm btn-outline-secondary" id="twpAddDsCancel">Cancel</button>
                        <button type="button" class="btn btn-sm btn-primary" id="twpAddDsSave">
                            <i class="bi bi-check2 me-1"></i>Add
                        </button>
                    </div>
                </div>`;
            document.body.appendChild(modal);

            const close = () => { modal.style.display = 'none'; };
            modal.querySelector('.twp-add-ds-close').addEventListener('click', close);
            modal.querySelector('#twpAddDsCancel').addEventListener('click', close);
            modal.addEventListener('click', e => { if (e.target === modal) close(); });
            modal.querySelector('#twpAddDsSave').addEventListener('click', () => submitAddDatasource(modal));
        }

        // (Re)populate type dropdown from the live schema list.
        const typeSel = modal.querySelector('#twpAddDsType');
        typeSel.innerHTML = schemas.map(s => `<option value="${esc(s.type)}">${esc(s.type)}</option>`).join('');

        const paramsHost = modal.querySelector('#twpAddDsParams');
        const renderParams = () => {
            const sel = schemas.find(s => s.type === typeSel.value) || schemas[0];
            const params = (sel && sel.parameters) || [];
            paramsHost.innerHTML = params.map(p => fieldHtmlForParam(p)).join('') ||
                '<div class="text-muted small">This datasource type has no extra parameters.</div>';
        };
        // Avoid stacking listeners across reopens.
        const fresh = typeSel.cloneNode(true);
        typeSel.replaceWith(fresh);
        fresh.addEventListener('change', renderParams);
        renderParams();

        // Reset & show.
        modal.querySelector('#twpAddDsName').value = '';
        const err = modal.querySelector('#twpAddDsError'); err.style.display = 'none'; err.textContent = '';
        modal.style.display = '';
        setTimeout(() => modal.querySelector('#twpAddDsName').focus(), 50);
    }

    async function submitAddDatasource(modal) {
        const name = (modal.querySelector('#twpAddDsName').value || '').trim();
        const type = modal.querySelector('#twpAddDsType').value;
        const err  = modal.querySelector('#twpAddDsError');
        const save = modal.querySelector('#twpAddDsSave');

        const schema = (_dsTypeSchemas || []).find(s => s.type === type);
        const params = (schema && schema.parameters) || [];

        if (!name) { err.textContent = 'Please enter a name.'; err.style.display = ''; return; }

        const payload = { name, type, workspaceId: state.wsId || null };
        for (const p of params) {
            const el = modal.querySelector(`[data-param="${(window.CSS && CSS.escape) ? CSS.escape(p.key) : p.key}"]`);
            const v = el ? (el.value || '').trim() : '';
            if (p.required && !v) {
                err.textContent = `Please fill in "${p.label || p.key}".`;
                err.style.display = '';
                return;
            }
            if (v) payload[p.key] = v;
        }

        save.disabled = true; save.innerHTML = '<i class="bi bi-hourglass-split me-1"></i>Adding…';
        err.style.display = 'none';
        try {
            const r = await fetch('/api/datasources', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken() },
                body: JSON.stringify(payload)
            });
            const data = await r.json().catch(() => ({}));
            if (!r.ok) throw new Error(data.error || ('Add failed (' + r.status + ')'));
            modal.style.display = 'none';
            setStatus(`Datasource "${data.name || name}" added.`, 'success');
            if (data.guid) state.activeQueryGuid = data.guid;
            await loadQueries();
        } catch (e) {
            err.textContent = e.message || 'Add failed.';
            err.style.display = '';
        } finally {
            save.disabled = false;
            save.innerHTML = '<i class="bi bi-check2 me-1"></i>Add';
        }
    }

    // ── Applied Steps ──────────────────────────────────────────────────
    function renderStepsList() {
        const list = $('twpStepsList');
        const count = $('twpStepsCount');
        const q = activeQuery();
        const steps = q ? q.steps : [];
        if (count) count.textContent = steps.length;
        if (!list) return;
        if (!q) { list.innerHTML = `<div class="twp-empty-mini">Select a query.</div>`; return; }
        if (steps.length === 0) {
            list.innerHTML = `<div class="twp-empty-mini">No steps yet. Pick an operation from the ribbon.</div>`;
            return;
        }
        list.innerHTML = steps.map((s, i) => `
            <div class="twp-step-item ${s.enabled === false ? 'disabled' : ''}" draggable="true" data-idx="${i}">
                <span class="twp-step-grip"><i class="bi bi-grip-vertical"></i></span>
                <span class="twp-step-num">${i + 1}</span>
                <span class="twp-step-label" title="${esc(s.op)}">${esc(labelFor(s.op, s.params))}</span>
                <span class="twp-step-actions">
                    <button class="twp-step-btn" data-act="toggle" title="${s.enabled === false ? 'Enable' : 'Disable'}">
                        <i class="bi ${s.enabled === false ? 'bi-eye-slash' : 'bi-eye'}"></i>
                    </button>
                    <button class="twp-step-btn" data-act="edit"   title="Edit"><i class="bi bi-pencil"></i></button>
                    <button class="twp-step-btn" data-act="up"     title="Move up"   ${i === 0 ? 'disabled' : ''}><i class="bi bi-arrow-up"></i></button>
                    <button class="twp-step-btn" data-act="down"   title="Move down" ${i === steps.length - 1 ? 'disabled' : ''}><i class="bi bi-arrow-down"></i></button>
                    <button class="twp-step-btn twp-step-btn-danger" data-act="remove" title="Remove"><i class="bi bi-x-lg"></i></button>
                </span>
            </div>
        `).join('');

        list.querySelectorAll('.twp-step-item').forEach(item => {
            const idx = +item.dataset.idx;
            item.querySelector('[data-act="toggle"]').addEventListener('click', () => {
                steps[idx].enabled = steps[idx].enabled === false;
                onStepsChanged();
            });
            item.querySelector('[data-act="edit"]').addEventListener('click', () => openStepEditor(idx));
            item.querySelector('[data-act="up"]').addEventListener('click', () => {
                if (idx === 0) return;
                [steps[idx - 1], steps[idx]] = [steps[idx], steps[idx - 1]];
                onStepsChanged();
            });
            item.querySelector('[data-act="down"]').addEventListener('click', () => {
                if (idx === steps.length - 1) return;
                [steps[idx + 1], steps[idx]] = [steps[idx], steps[idx + 1]];
                onStepsChanged();
            });
            item.querySelector('[data-act="remove"]').addEventListener('click', () => {
                steps.splice(idx, 1);
                onStepsChanged();
            });

            // Drag & drop reorder
            item.addEventListener('dragstart', e => { e.dataTransfer.setData('text/plain', String(idx)); item.classList.add('dragging'); });
            item.addEventListener('dragend',   () => item.classList.remove('dragging'));
            item.addEventListener('dragover',  e => { e.preventDefault(); item.classList.add('drag-over'); });
            item.addEventListener('dragleave', () => item.classList.remove('drag-over'));
            item.addEventListener('drop', e => {
                e.preventDefault();
                item.classList.remove('drag-over');
                const from = +e.dataTransfer.getData('text/plain');
                const to = idx;
                if (from === to) return;
                const [moved] = steps.splice(from, 1);
                steps.splice(to, 0, moved);
                onStepsChanged();
            });
        });
    }

    function onStepsChanged() {
        renderQueriesList();
        renderStepsList();
        renderTomlFromSteps();
        updateStatusBar({});
        // Debounced auto-preview so every step add/remove/edit/toggle/reorder
        // immediately reflects in the preview table and status bar without
        // making the user click "Preview" each time.
        schedulePreview();
    }

    let _previewDebounce = null;
    function schedulePreview() {
        const q = activeQuery();
        if (!q) return;
        clearTimeout(_previewDebounce);
        _previewDebounce = setTimeout(() => { runPreview(); }, 180);
    }

    // ── Step Editor — wizard-style, no raw JSON ────────────────────────
    // Each op declares a friendly schema of fields (column dropdowns, query
    // pickers, selects, expressions). The TOML stays backend-only; users
    // never see/edit raw params unless they flip the power-user toggle.
    // An AI suggestion strip in the middle lets users describe intent and
    // pre-fill the form.
    const PARAM_SCHEMA = {
        // home / reduce
        remove_duplicates: [{ key:'keys', label:'Match duplicates on columns', type:'columns' }],
        filter:            [{ key:'expression', label:'Keep rows where', type:'expression', placeholder:'[Status] <> "Closed"' }],
        remove_rows:       [
            { key:'count',    label:'How many rows to remove?', type:'number', min:1 },
            { key:'position', label:'From',                     type:'select',
              options:[{value:'top',label:'Top of table'},{value:'bottom',label:'Bottom of table'}] }
        ],
        remove_bottom_rows: [{ key:'count', label:'How many bottom rows to remove?', type:'number', min:1 }],
        remove_blank_rows:  [],
        select_rows:       [{ key:'expression', label:'Keep rows where', type:'expression', placeholder:'[Active] = true' }],
        remove_columns:    [{ key:'columns', label:'Columns to remove', type:'columns' }],
        sort_asc:          [{ key:'column', label:'Sort by column', type:'column' }],
        sort_desc:         [{ key:'column', label:'Sort by column', type:'column' }],

        // table
        group_by: [
            { key:'keys', label:'Group by columns', type:'columns' },
            { key:'aggregations', label:'Aggregations', type:'aggregations' }
        ],
        pivot: [
            { key:'column',        label:'Column to pivot',  type:'column' },
            { key:'values_column', label:'Values column',    type:'column' },
            { key:'agg',           label:'Aggregation',      type:'select', options:['sum','avg','count','min','max'] }
        ],
        unpivot: [
            { key:'columns',        label:'Columns to unpivot',     type:'columns' },
            { key:'attribute_name', label:'Attribute column name',  type:'text', placeholder:'Quarter' },
            { key:'value_name',     label:'Value column name',      type:'text', placeholder:'Value'   }
        ],
        transpose:  [],
        reverse:    [],
        count_rows: [{ key:'target_field', label:'New column name', type:'text', placeholder:'RowCount' }],

        // any column
        rename_column:    [{ key:'from', label:'Column', type:'column' }, { key:'to', label:'New name', type:'text' }],
        duplicate_column: [{ key:'source', label:'Column', type:'column' }, { key:'target_field', label:'New column name', type:'text' }],
        replace_values: [
            { key:'column', label:'Column', type:'column' },
            { key:'from',   label:'Find',   type:'text' },
            { key:'to',     label:'Replace with', type:'text' }
        ],
        fill_down:       [{ key:'column', label:'Column', type:'column' }],
        fill_up:         [{ key:'column', label:'Column', type:'column' }],
        reorder_columns: [{ key:'order',  label:'Desired column order',  type:'columns' }],
        change_type: [
            { key:'column', label:'Column',        type:'column' },
            { key:'type',   label:'New data type', type:'select',
              options:[
                  { value:'text',     label:'Text' },
                  { value:'integer',  label:'Whole Number' },
                  { value:'decimal',  label:'Decimal' },
                  { value:'boolean',  label:'Boolean' },
                  { value:'date',     label:'Date' },
                  { value:'datetime', label:'Date / Time' },
                  { value:'time',     label:'Time' }
              ] }
        ],
        split_column: [
            { key:'column',    label:'Column to split',  type:'column' },
            { key:'delimiter', label:'Delimiter',        type:'text', placeholder:' ' },
            { key:'into',      label:'New column names (in order)', type:'columns' }
        ],
        merge_columns: [
            { key:'columns',      label:'Columns to merge (in order)', type:'columns' },
            { key:'separator',    label:'Separator',                   type:'text', placeholder:' ' },
            { key:'target_field', label:'New column name',             type:'text', placeholder:'FullName' }
        ],
        promote_headers: [],
        demote_headers:  [],

        // add column
        add_column: [
            { key:'target_field', label:'New column name', type:'text' },
            { key:'value',        label:'Expression or literal', type:'expression' }
        ],
        derived_field: [
            { key:'target_field', label:'New column name', type:'text' },
            { key:'expression',   label:'DAX-style expression', type:'expression',
              placeholder:'DATEDIFF(minute,[IncidentDateTime],[CloseDateTime])' }
        ],
        index_column: [
            { key:'target_field', label:'Column name', type:'text', placeholder:'Index' },
            { key:'start',        label:'Start at',    type:'number' },
            { key:'step',         label:'Step',        type:'number' }
        ],
        conditional: [
            { key:'target_field', label:'New column name', type:'text' },
            { key:'expression',   label:'Condition (DAX-style)', type:'expression',
              placeholder:'IF([Amount] > 1000, "High", "Low")' }
        ],

        // combine
        merge: [
            { key:'right',     label:'Merge with query',          type:'query' },
            { key:'left_key',  label:'Left key (this query)',     type:'column' },
            { key:'right_key', label:'Right key (other query)',   type:'text', placeholder:'Id' },
            { key:'how',       label:'Join type',                 type:'select',
              options:[
                  { value:'inner', label:'Inner — only matching rows' },
                  { value:'left',  label:'Left  — keep all from this query' },
                  { value:'right', label:'Right — keep all from other query' },
                  { value:'full',  label:'Full  — all rows from both' }
              ] }
        ],
        append: [
            { key:'source',         label:'Append rows from query', type:'query' },
            { key:'align_columns',  label:'Align columns by name',  type:'boolean' }
        ],
        nested_join: [
            { key:'right',        label:'Join with query', type:'query' },
            { key:'left_key',     label:'Left key',        type:'column' },
            { key:'right_key',    label:'Right key',       type:'text' },
            { key:'target_field', label:'Nested field name', type:'text' }
        ],
        combine_files: [{ key:'sources', label:'Tables to combine', type:'queries' }]
    };

    function schemaFor(op) {
        if (PARAM_SCHEMA[op]) return PARAM_SCHEMA[op];
        // Generic single-column transforms (text_*, num_*, date_*, time_*).
        if (/^text_(upper|lower|proper|trim|clean)$/.test(op))           return [{ key:'column', label:'Column', type:'column' }];
        if (op === 'text_replace')   return [{ key:'column', label:'Column', type:'column' }, { key:'from', label:'Find', type:'text' }, { key:'to', label:'Replace with', type:'text' }];
        if (op === 'text_extract')   return [{ key:'column', label:'Column', type:'column' }, { key:'start', label:'Start index', type:'number' }, { key:'length', label:'Length', type:'number' }, { key:'target_field', label:'New column name', type:'text' }];
        if (op === 'text_length')    return [{ key:'column', label:'Column', type:'column' }, { key:'target_field', label:'New column name', type:'text' }];
        if (op === 'text_contains')  return [{ key:'column', label:'Column', type:'column' }, { key:'value', label:'Search for', type:'text' }, { key:'target_field', label:'New column name', type:'text' }];
        if (/^num_(round_up|round_down|abs)$/.test(op)) return [{ key:'column', label:'Column', type:'column' }];
        if (op === 'num_round')   return [{ key:'column', label:'Column', type:'column' }, { key:'digits', label:'Decimal digits', type:'number' }];
        if (op === 'num_power')   return [{ key:'column', label:'Column', type:'column' }, { key:'exponent', label:'Exponent', type:'number' }];
        if (op === 'num_log')     return [{ key:'column', label:'Column', type:'column' }, { key:'base', label:'Base', type:'number' }];
        if (op === 'num_mod')     return [{ key:'column', label:'Column', type:'column' }, { key:'divisor', label:'Divisor', type:'number' }];
        if (/^date_(year|month|day|start_of_month|end_of_month)$/.test(op)) return [{ key:'column', label:'Date column', type:'column' }, { key:'target_field', label:'New column name', type:'text' }];
        if (op === 'date_add_days')   return [{ key:'column', label:'Date column', type:'column' }, { key:'days', label:'Days to add', type:'number' }];
        if (op === 'date_add_months') return [{ key:'column', label:'Date column', type:'column' }, { key:'months', label:'Months to add', type:'number' }];
        if (op === 'date_now')        return [{ key:'target_field', label:'New column name', type:'text' }];
        if (/^time_(hour|minute|second)$/.test(op)) return [{ key:'column', label:'Time column', type:'column' }, { key:'target_field', label:'New column name', type:'text' }];
        return [];
    }

    function getColumnsForActive() {
        const q = activeQuery();
        const fromSchema = q && Array.isArray(q.schema) ? q.schema : null;
        if (fromSchema && fromSchema.length) return fromSchema;
        const tbl = $('twpPreviewTable');
        const heads = tbl ? Array.from(tbl.querySelectorAll('thead th[data-col]')).map(th => th.dataset.col) : [];
        return heads;
    }

    // ── Preview-data introspection helpers ─────────────────────────────
    // Read the per-column type map stashed on the preview table by
    // renderPreview(). Returns {} when no preview has been rendered yet.
    function getColumnTypes() {
        const tbl = $('twpPreviewTable');
        if (!tbl) return {};
        try { return JSON.parse(tbl.dataset.colTypes || '{}'); } catch { return {}; }
    }
    // Find the first column whose inferred type matches one of `kinds`,
    // searching kinds in priority order.
    function firstColOf(types, kinds) {
        for (const k of kinds) {
            for (const c of Object.keys(types || {})) {
                if (types[c] === k) return c;
            }
        }
        return null;
    }
    function colsOfKinds(types, kinds) {
        return Object.keys(types || {}).filter(c => kinds.includes(types[c]));
    }
    // Return {col:value} for the first body row, or null if no data. Prefers
    // the cached preview payload (typed values) and falls back to DOM text.
    function firstPreviewRow() {
        const cached = state.lastPreview;
        if (cached && Array.isArray(cached.rows) && cached.rows.length > 0) {
            return { ...cached.rows[0] };
        }
        const tbl = $('twpPreviewTable'); if (!tbl) return null;
        const heads = Array.from(tbl.querySelectorAll('thead th[data-col]')).map(th => th.dataset.col);
        const tr = tbl.querySelector('tbody tr'); if (!tr) return null;
        // Skip leading row-actions <td>; remaining cells align with `heads`.
        const cells = Array.from(tr.querySelectorAll('td')).slice(1);
        const out = {};
        heads.forEach((h, i) => { out[h] = cells[i] ? cells[i].textContent : ''; });
        return out;
    }

    // Build sensible default params for an op based on the actual preview
    // data. Returns {params} on success, or {error} when the op needs a
    // column type the current preview doesn't have. Falls back to
    // defaultParamsFor() for ops with no specific data requirement.
    function smartDefaults(op, ctx) {
        const types = ctx.types || {};
        const cols  = ctx.cols  || [];
        const otherDs = (ctx.otherQueries || [])[0]?.name || null;
        const need = (msg) => ({ error: msg });
        const pickText  = () => firstColOf(types, ['text']) || cols[0] || null;
        const pickNum   = () => firstColOf(types, ['integer','decimal']);
        const pickDate  = () => firstColOf(types, ['datetime','date']);
        const pickAny   = () => cols[0] || null;
        // Combine ops require another query.
        if (['merge','append','nested_join','combine_files'].indexOf(op) !== -1 && !otherDs) {
            return need('This step needs a second query in the workspace. Add another datasource first.');
        }
        // Pivot / unpivot need richer schema.
        if (op === 'pivot') {
            const cat = pickText(); const val = pickNum();
            if (cols.length && (!cat || !val)) return need('Pivot needs at least one text column (to pivot) and one numeric column (the values). Your preview is missing one.');
            return { params: { column: cat, values_column: val, agg: 'sum' } };
        }
        if (op === 'unpivot') {
            const nums = colsOfKinds(types, ['integer','decimal']);
            if (cols.length && nums.length < 2) return need('Unpivot needs two or more numeric columns to combine. Your preview has fewer than that.');
            return { params: { columns: nums, attribute_name: 'Attribute', value_name: 'Value' } };
        }
        if (op === 'merge_columns') {
            const texts = colsOfKinds(types, ['text']);
            if (cols.length && texts.length < 2) return need('Merge columns needs at least two text columns.');
            const pair = texts.slice(0, 2);
            return { params: { columns: pair, separator: ' ', target_field: pair.join('_') || 'Merged' } };
        }
        if (op === 'split_column') {
            const c = pickText();
            if (cols.length && !c) return need('Split column needs a text column.');
            return { params: { column: c || 'Column', delimiter: ' ', into: [(c||'Col')+'_1', (c||'Col')+'_2'] } };
        }
        // Single-column text ops.
        if (/^text_/.test(op)) {
            const c = pickText();
            if (cols.length && !c) return need('This text step needs a text column.');
            const base = { column: c || 'Column' };
            if (op === 'text_replace')  return { params: { ...base, from: '', to: '' } };
            if (op === 'text_extract')  return { params: { ...base, start: 0, length: 5, target_field: (c||'Col')+'_Extract' } };
            if (op === 'text_length')   return { params: { ...base, target_field: (c||'Col')+'Length' } };
            if (op === 'text_contains') return { params: { ...base, value: '', target_field: 'Has'+(c||'Value') } };
            return { params: base };
        }
        // Number ops.
        if (/^num_/.test(op)) {
            const c = pickNum();
            if (cols.length && !c) return need('This step needs a numeric column.');
            const base = { column: c };
            if (op === 'num_round')  return { params: { ...base, digits: 2 } };
            if (op === 'num_power')  return { params: { ...base, exponent: 2 } };
            if (op === 'num_log')    return { params: { ...base, base: 10 } };
            if (op === 'num_mod')    return { params: { ...base, divisor: 1 } };
            return { params: base };
        }
        // Date ops (excluding date_now).
        if (op.indexOf('date_') === 0 && op !== 'date_now') {
            const c = pickDate();
            if (cols.length && !c) return need('This step needs a date or date/time column, but the current preview has none.');
            const base = { column: c };
            if (op === 'date_add_days')   return { params: { ...base, days: 30 } };
            if (op === 'date_add_months') return { params: { ...base, months: 1 } };
            const targetMap = { date_year:'Year', date_month:'Month', date_day:'Day', date_start_of_month:'MonthStart', date_end_of_month:'MonthEnd' };
            return { params: { ...base, target_field: targetMap[op] || 'Value' } };
        }
        if (op === 'date_now') return { params: { target_field: 'Now' } };
        // Time ops.
        if (op.indexOf('time_') === 0) {
            const c = firstColOf(types, ['datetime','time','date']);
            if (cols.length && !c) return need('This step needs a time / date-time column, but the current preview has none.');
            const targetMap = { time_hour:'Hour', time_minute:'Minute', time_second:'Second' };
            return { params: { column: c, target_field: targetMap[op] || 'Value' } };
        }
        // Single-column-keyed ops.
        if (op === 'sort_asc' || op === 'sort_desc') {
            const c = pickAny();
            if (cols.length && !c) return need('Pick a query with at least one column first.');
            return { params: { column: c, direction: op === 'sort_desc' ? 'desc' : 'asc' } };
        }
        if (op === 'remove_columns')   return { params: { columns: cols.length ? [cols[0]] : [] } };
        if (op === 'reorder_columns')  return { params: { order: cols.slice() } };
        if (op === 'remove_duplicates')return { params: { keys: cols.length ? [cols[0]] : [] } };
        if (op === 'fill_down' || op === 'fill_up') {
            const c = pickAny();
            if (cols.length && !c) return need('This step needs a column.');
            return { params: { column: c } };
        }
        if (op === 'rename_column')    return { params: { from: pickAny() || '', to: '' } };
        if (op === 'duplicate_column') { const c = pickAny(); return { params: { source: c || '', target_field: c ? c + 'Copy' : 'Copy' } }; }
        if (op === 'replace_values')   return { params: { column: pickAny() || '', from: '', to: '' } };
        if (op === 'change_type')      { const c = pickAny(); return { params: { column: c || '', type: types[c] || 'text' } }; }
        if (op === 'group_by')         return { params: { keys: cols.length ? [pickText() || cols[0]] : [], aggregations: [{ name: 'Count', op: 'count' }] } };
        if (op === 'promote_headers') {
            if (!firstPreviewRow()) return need('Promote headers needs preview data. Run preview first, then click again — the first row will become the header.');
            return { params: {} };
        }
        // Filter / select_rows: leave the expression empty so the editor
        // demands user input. Pre-filling with arbitrary column references
        // would silently change the dataset on Save.
        if (op === 'filter' || op === 'select_rows') return { params: { expression: '' } };
        // Custom-column / derived / conditional: open with empty expression
        // and a sensible target_field name; the editor and DAX validator
        // require a real expression before Save.
        if (op === 'add_column')    return { params: { target_field: '', value: '' } };
        if (op === 'derived_field') return { params: { target_field: '', expression: '' } };
        if (op === 'conditional')   return { params: { target_field: '', expression: '' } };
        if (op === 'index_column')  return { params: { target_field: 'Index', start: 1, step: 1 } };
        if (op === 'count_rows')    return { params: { target_field: 'RowCount' } };
        // Combine ops with another query.
        if (op === 'merge')         return { params: { right: otherDs, left_key: pickAny() || 'Id', right_key: 'Id', how: 'inner' } };
        if (op === 'append')        return { params: { source: otherDs, align_columns: true } };
        if (op === 'nested_join')   return { params: { right: otherDs, left_key: pickAny() || 'Id', right_key: 'Id', target_field: 'Joined' } };
        if (op === 'combine_files') return { params: { sources: [otherDs] } };
        // Anything else: fall back to the static templates.
        return { params: defaultParamsFor(op, ctx) };
    }

    // ── Floating popup menu / input helpers ────────────────────────────
    // Used by the column header type pill and 3-dot menu so the workbench
    // never has to fall back to the unstyled native window.prompt/confirm.
    let _twpOpenMenu = null;
    function _twpMenuOutside(e) {
        if (_twpOpenMenu && !_twpOpenMenu.contains(e.target)) closeFloatingMenu();
    }
    function _twpMenuKey(e) {
        if (e.key === 'Escape') { e.preventDefault(); closeFloatingMenu(); }
    }
    function closeFloatingMenu() {
        if (_twpOpenMenu) {
            try { _twpOpenMenu.remove(); } catch { /* noop */ }
            _twpOpenMenu = null;
            document.removeEventListener('mousedown', _twpMenuOutside, true);
            document.removeEventListener('keydown',   _twpMenuKey,     true);
        }
    }
    function _positionPopup(menu, anchor) {
        const r = menu.getBoundingClientRect();
        const vw = window.innerWidth, vh = window.innerHeight;
        let left = anchor.left, top = anchor.bottom + 4;
        if (left + r.width  > vw - 6) left = Math.max(6, vw - r.width - 6);
        if (top  + r.height > vh - 6) top  = Math.max(6, anchor.top - r.height - 4);
        menu.style.left = left + 'px';
        menu.style.top  = top  + 'px';
    }
    // items: [{ icon, label, value, current?, kind?: 'danger'|'separator'|'header' }]
    function showFloatingMenu(items, anchor, onPick) {
        closeFloatingMenu();
        const menu = document.createElement('div');
        menu.className = 'twp-type-popup';
        menu.setAttribute('role', 'menu');
        menu.innerHTML = (items || []).map((it, i) => {
            if (it.kind === 'separator') return '<div class="twp-type-popup-sep"></div>';
            if (it.kind === 'header')    return `<div class="twp-type-popup-header">${esc(it.label)}</div>`;
            const cur = it.current ? ' is-current' : '';
            const dng = it.kind === 'danger' ? ' is-danger' : '';
            const ico = it.icon ? `<i class="bi ${esc(it.icon)}"></i>` : '<i></i>';
            const chk = it.current ? '<i class="bi bi-check2 twp-type-popup-check"></i>' : '';
            return `<button type="button" class="twp-type-popup-item${cur}${dng}" data-i="${i}" role="menuitem">${ico}<span>${esc(it.label)}</span>${chk}</button>`;
        }).join('');
        document.body.appendChild(menu);
        _positionPopup(menu, anchor);
        menu.addEventListener('click', e => {
            const b = e.target.closest('[data-i]'); if (!b) return;
            const it = items[+b.dataset.i];
            closeFloatingMenu();
            if (it && typeof onPick === 'function') onPick(it);
        });
        setTimeout(() => {
            document.addEventListener('mousedown', _twpMenuOutside, true);
            document.addEventListener('keydown',   _twpMenuKey,     true);
        }, 0);
        _twpOpenMenu = menu;
        return menu;
    }
    // Branded input popup — small floating card with a label, input and
    // OK/Cancel actions. Replaces window.prompt for inline rename flows.
    function showInputPopup(opts, anchor, onSave) {
        closeFloatingMenu();
        const o = opts || {};
        const menu = document.createElement('div');
        menu.className = 'twp-type-popup twp-input-popup';
        menu.setAttribute('role', 'dialog');
        menu.innerHTML = `
            ${o.title ? `<div class="twp-type-popup-header">${esc(o.title)}</div>` : ''}
            ${o.label ? `<label class="twp-input-popup-label">${esc(o.label)}</label>` : ''}
            <input type="text" class="form-control form-control-sm twp-input-popup-input" value="${esc(o.value ?? '')}" placeholder="${esc(o.placeholder ?? '')}">
            <div class="twp-input-popup-actions">
                <button type="button" class="btn btn-sm btn-outline-secondary" data-act="cancel">Cancel</button>
                <button type="button" class="btn btn-sm btn-primary" data-act="ok">${esc(o.okLabel || 'OK')}</button>
            </div>`;
        document.body.appendChild(menu);
        _positionPopup(menu, anchor);
        const input = menu.querySelector('.twp-input-popup-input');
        const commit = () => {
            const v = (input.value || '').trim();
            closeFloatingMenu();
            if (typeof onSave === 'function') onSave(v);
        };
        menu.addEventListener('click', e => {
            const a = e.target.closest('[data-act]'); if (!a) return;
            if (a.dataset.act === 'ok') commit();
            else closeFloatingMenu();
        });
        input.addEventListener('keydown', e => {
            if (e.key === 'Enter')  { e.preventDefault(); commit(); }
            if (e.key === 'Escape') { e.preventDefault(); closeFloatingMenu(); }
        });
        setTimeout(() => {
            document.addEventListener('mousedown', _twpMenuOutside, true);
            document.addEventListener('keydown',   _twpMenuKey,     true);
            input.focus(); input.select();
        }, 0);
        _twpOpenMenu = menu;
        return menu;
    }

    // ── Preview table delegated click handling ─────────────────────────
    // Buttons are rendered with [data-act] attributes by renderPreview().
    // Routing them through one delegated listener keeps the click logic in
    // one place and survives table re-renders.
    function onPreviewTableClick(e) {
        const btn = e.target.closest('[data-act]');
        const nameSpan = e.target.closest('.twp-col-name');
        if (!btn && !nameSpan) return;
        const q = activeQuery();
        if (!q) { setStatus('Pick a query first.', 'warn'); return; }

        // Sort cycle when the user clicks the column name itself.
        if (nameSpan && !btn) {
            const th = nameSpan.closest('th[data-col]');
            if (th && window.TWP && window.TWP.toggleSort) {
                e.stopPropagation();
                window.TWP.toggleSort(th.dataset.col);
            }
            return;
        }

        e.stopPropagation();
        const act = btn.dataset.act;
        const th = btn.closest('th[data-col]');
        const tr = btn.closest('tr[data-rowsig]');
        const col = th ? th.dataset.col : null;

        const removeExistingSort = (c) => {
            for (let i = q.steps.length - 1; i >= 0; i--) {
                const s = q.steps[i];
                if (s && (s.op === 'sort_asc' || s.op === 'sort_desc') && s.params && s.params.column === c) {
                    q.steps.splice(i, 1);
                }
            }
        };

        switch (act) {
            case 'col-sort-asc':
            case 'col-sort-desc': {
                if (!col) return;
                const wantDesc = act === 'col-sort-desc';
                // Tri-state cycle: asc → desc → none → asc. If a sort already
                // exists in the requested direction, clicking it again removes
                // the sort so the user can return to natural order.
                const existing = (window.TWP && window.TWP.getSortState) ? window.TWP.getSortState(col) : null;
                removeExistingSort(col);
                if (existing && ((existing.dir === 'desc') === wantDesc)) {
                    onStepsChanged();
                    return;
                }
                const dir = wantDesc ? 'desc' : 'asc';
                q.steps.push({ op: wantDesc ? 'sort_desc' : 'sort_asc', enabled: true, params: { column: col, direction: dir } });
                onStepsChanged();
                return;
            }
            case 'col-delete': {
                if (!col) return;
                q.steps.push({ op: 'remove_columns', enabled: true, params: { columns: [col] } });
                onStepsChanged();
                return;
            }
            case 'col-type': {
                if (!col) return;
                openTypePopup(col, th, btn);
                return;
            }
            case 'col-menu': {
                if (!col || !th) return;
                // Let an external enhancements module take over if it wants to.
                const ev = new CustomEvent('twp:col-menu', { detail: { column: col, anchor: btn.getBoundingClientRect() } , cancelable: true });
                document.dispatchEvent(ev);
                if (ev.defaultPrevented) return;
                openColumnMenu(col, th, btn, q, removeExistingSort);
                return;
            }
            case 'row-delete': {
                if (!tr) return;
                let row = null;
                try { row = JSON.parse(tr.dataset.rowsig || 'null'); } catch { row = null; }
                if (!row) return;
                // Build a precise filter that excludes this exact row by
                // AND-ing up to 3 non-empty columns. Filter semantics keep
                // rows where the expression is TRUE, so we negate the
                // "matches all anchors" condition with NOT(...).
                const anchors = Object.keys(row)
                    .filter(k => row[k] !== null && row[k] !== undefined && String(row[k]).trim() !== '')
                    .slice(0, 3);
                if (anchors.length === 0) {
                    setStatus('Cannot remove a fully-blank row from the preview \u2014 add a Remove blank rows step instead.', 'warn');
                    return;
                }
                const escVal = (v) => String(v).replace(/\\/g, '\\\\').replace(/"/g, '\\"');
                const conds = anchors.map(k => `[${k}] = "${escVal(row[k])}"`).join(' AND ');
                const expr = `NOT(${conds})`;
                q.steps.push({ op: 'filter', enabled: true, params: { expression: expr } });
                onStepsChanged();
                return;
            }
        }
    }

    // Renders the column type popup, used by both the type-pill click and
    // the "Change type" entry of the column 3-dot menu.
    function openTypePopup(col, th, btn) {
        const q = activeQuery(); if (!q) return;
        const current = (th && th.dataset.coltype) || 'text';
        const order = ['text','integer','decimal','boolean','date','datetime','time'];
        const items = order.map(t => ({
            icon: (TYPE_META[t] || TYPE_META.text).icon,
            label: (TYPE_META[t] || TYPE_META.text).label,
            value: t,
            current: t === current
        }));
        const rect = (btn || th).getBoundingClientRect();
        showFloatingMenu(items, rect, (it) => {
            if (!it || it.value === current) return;
            q.steps.push({ op: 'change_type', enabled: true, params: { column: col, type: it.value } });
            onStepsChanged();
        });
    }

    // Rich column action menu — replaces the legacy info-text fallback.
    function openColumnMenu(col, th, btn, q, removeExistingSort) {
        const sort = (window.TWP && window.TWP.getSortState) ? window.TWP.getSortState(col) : null;
        const curType = (th && th.dataset.coltype) || 'text';
        const tm = TYPE_META[curType] || TYPE_META.text;
        const items = [
            { kind:'header', label: col },
            { icon:'bi-caret-up-fill',   label:'Sort ascending',  value:'sort-asc',  current: sort && sort.dir === 'asc'  },
            { icon:'bi-caret-down-fill', label:'Sort descending', value:'sort-desc', current: sort && sort.dir === 'desc' },
            ...(sort ? [{ icon:'bi-x-circle', label:'Remove sort', value:'sort-clear' }] : []),
            { kind:'separator' },
            { icon: tm.icon,               label: 'Change type — ' + tm.label, value:'change-type' },
            { icon:'bi-input-cursor-text', label: 'Rename column…',            value:'rename' },
            { icon:'bi-copy',              label: 'Duplicate column',          value:'duplicate' },
            { kind:'separator' },
            { icon:'bi-x-lg', label:'Remove column', value:'remove', kind:'danger' }
        ];
        const rect = (btn || th).getBoundingClientRect();
        showFloatingMenu(items, rect, (it) => {
            if (!it) return;
            switch (it.value) {
                case 'sort-asc':
                case 'sort-desc': {
                    const wantDesc = it.value === 'sort-desc';
                    removeExistingSort(col);
                    q.steps.push({ op: wantDesc ? 'sort_desc' : 'sort_asc', enabled: true, params: { column: col, direction: wantDesc ? 'desc' : 'asc' } });
                    onStepsChanged();
                    return;
                }
                case 'sort-clear':
                    removeExistingSort(col); onStepsChanged(); return;
                case 'change-type':
                    // Re-open as the type popup anchored at the same button.
                    setTimeout(() => openTypePopup(col, th, btn), 0);
                    return;
                case 'rename':
                    showInputPopup({ title:'Rename column', label:`Rename "${col}" to:`, value: col, okLabel:'Rename' }, rect, (newName) => {
                        if (!newName || newName === col) return;
                        q.steps.push({ op: 'rename_column', enabled: true, params: { from: col, to: newName } });
                        onStepsChanged();
                    });
                    return;
                case 'duplicate':
                    q.steps.push({ op: 'duplicate_column', enabled: true, params: { source: col, target_field: col + 'Copy' } });
                    onStepsChanged();
                    return;
                case 'remove':
                    q.steps.push({ op: 'remove_columns', enabled: true, params: { columns: [col] } });
                    onStepsChanged();
                    return;
            }
        });
    }

    function wireStepEditor() {
        $('twpStepEditorClose').addEventListener('click', cancelStepEditor);
        $('twpStepEditorCancel').addEventListener('click', cancelStepEditor);
        $('twpStepEditorSave').addEventListener('click', saveStepEditor);
    }

    let editingStepIdx = -1;
    let editStartSnapshot = null;

    function openStepEditor(idx) {
        const q = activeQuery(); if (!q) return;
        const step = q.steps[idx]; if (!step) return;
        editingStepIdx = idx;
        // Snapshot original params/enabled so Cancel can revert any live edits.
        editStartSnapshot = JSON.parse(JSON.stringify({ op: step.op, params: step.params || {}, enabled: step.enabled }));
        const friendly = labelFor(step.op, step.params) || step.op;
        $('twpStepEditorTitle').textContent = `Edit step — ${friendly}`;
        renderStepForm(step);
        hideStepEditorError();
        setStepEditorBusy(false);
        $('twpStepEditor').style.display = '';
    }

    function renderStepForm(step) {
        const body = $('twpStepEditorBody');
        const schema = schemaFor(step.op);
        const cols = getColumnsForActive();
        const otherQs = state.queries.filter(x => x.guid !== state.activeQueryGuid);
        const params = step.params || {};

        const fieldHtml = schema.length === 0
            ? `<div class="text-muted small">This operation has no parameters.</div>`
            : schema.map(f => renderField(f, params[f.key], cols, otherQs)).join('');

        body.innerHTML = `
            <div class="twp-form-head">
                <span class="twp-form-op"><i class="bi bi-magic me-1"></i>${esc(step.op)}</span>
                <div class="form-check form-switch m-0">
                    <input class="form-check-input" type="checkbox" id="twpStepEnabled" ${step.enabled === false ? '' : 'checked'}>
                    <label class="form-check-label small" for="twpStepEnabled">Enabled</label>
                </div>
            </div>

            <div class="twp-ai-suggest">
                <div class="twp-ai-suggest-label">
                    <i class="bi bi-stars me-1"></i>AI assist
                    <span class="text-muted">Describe what you want and let AI fill in the fields.</span>
                </div>
                <div class="twp-ai-suggest-row">
                    <input type="text" class="form-control form-control-sm" id="twpStepAiPrompt"
                           placeholder="${esc(aiPlaceholderFor(step.op))}" />
                    <button type="button" class="btn btn-sm btn-outline-primary" id="twpStepAiBtn">
                        <i class="bi bi-magic me-1"></i>Suggest
                    </button>
                </div>
                <div class="twp-ai-suggest-status small text-muted" id="twpStepAiStatus"></div>
            </div>

            <div class="twp-form-fields">${fieldHtml}</div>

            <details class="twp-advanced">
                <summary><i class="bi bi-code-slash me-1"></i>Advanced (raw parameters)</summary>
                <textarea class="form-control form-control-sm twp-step-json" id="twpStepJson" rows="6" spellcheck="false">${esc(JSON.stringify(step.params || {}, null, 2))}</textarea>
                <div class="small text-muted mt-1">Edited here? Toggle this section closed before Save to use the form values.</div>
            </details>
        `;

        // Wire AI suggest
        $('twpStepAiBtn').addEventListener('click', () => runStepAiSuggest(step));
        $('twpStepAiPrompt').addEventListener('keydown', e => {
            if (e.key === 'Enter') { e.preventDefault(); runStepAiSuggest(step); }
        });

        // Wire dynamic widgets (multi-column chips, aggregation builder, queries chips)
        body.querySelectorAll('[data-widget="columns"], [data-widget="queries"]').forEach(wireChipPicker);
        body.querySelectorAll('[data-widget="aggregations"]').forEach(wireAggregationsEditor);

        // Live preview: any edit to form fields or chip widgets debounce-updates
        // the step in-place so the preview pane reflects the draft as you type.
        const liveCommit = () => {
            const q = activeQuery();
            if (!q || editingStepIdx < 0) return;
            const s = q.steps[editingStepIdx]; if (!s) return;
            const adv = body.querySelector('.twp-advanced');
            if (adv && adv.open) {
                try { s.params = JSON.parse($('twpStepJson').value || '{}'); } catch { /* ignore while typing */ }
            } else {
                s.params = collectFormParams();
            }
            const enChk = $('twpStepEnabled');
            if (enChk) s.enabled = !!enChk.checked;
            renderStepsList();
            renderTomlFromSteps();
            schedulePreview();
        };
        body.addEventListener('input', liveCommit);
        body.addEventListener('change', liveCommit);
    }

    function aiPlaceholderFor(op) {
        switch (op) {
            case 'merge':         return 'merge with Customers on CustomerId, left join';
            case 'append':        return 'append rows from Sales2024';
            case 'filter':        return 'keep rows where Status is Open and Amount > 100';
            case 'group_by':      return 'group by Region and sum Amount';
            case 'derived_field': return 'add Margin = Profit / Revenue';
            case 'change_type':   return 'change Amount to decimal';
            case 'replace_values':return 'replace "N/A" with empty in Region';
            default:              return 'describe what this step should do…';
        }
    }

    function renderField(f, value, cols, otherQs) {
        const id = 'twpFld_' + f.key;
        const lbl = `<label class="form-label small fw-semibold" for="${id}">${esc(f.label)}</label>`;
        const ph = f.placeholder ? esc(f.placeholder) : '';
        switch (f.type) {
            case 'text':
                return `<div class="twp-field">${lbl}<input id="${id}" type="text" class="form-control form-control-sm" data-key="${esc(f.key)}" data-type="text" value="${esc(value ?? '')}" placeholder="${ph}"></div>`;
            case 'number':
                return `<div class="twp-field">${lbl}<input id="${id}" type="number" class="form-control form-control-sm" data-key="${esc(f.key)}" data-type="number" value="${value == null ? '' : esc(value)}" ${f.min != null ? `min="${f.min}"` : ''}></div>`;
            case 'boolean':
                return `<div class="twp-field"><div class="form-check form-switch"><input id="${id}" class="form-check-input" type="checkbox" data-key="${esc(f.key)}" data-type="boolean" ${value === false ? '' : 'checked'}><label class="form-check-label small" for="${id}">${esc(f.label)}</label></div></div>`;
            case 'expression':
                return `<div class="twp-field">${lbl}<textarea id="${id}" class="form-control form-control-sm twp-expr-input" data-key="${esc(f.key)}" data-type="text" rows="3" data-twp-intelli spellcheck="false" placeholder="${ph}">${esc(value ?? '')}</textarea><div class="form-text small">Use <code>[Column]</code> for fields. Try IF, SWITCH, DATEDIFF, CALCULATE, DIVIDE…</div></div>`;
            case 'select': {
                const opts = (f.options || []).map(o => {
                    const ov = typeof o === 'string' ? o : o.value;
                    const ol = typeof o === 'string' ? o : o.label;
                    return `<option value="${esc(ov)}" ${String(value) === String(ov) ? 'selected' : ''}>${esc(ol)}</option>`;
                }).join('');
                return `<div class="twp-field">${lbl}<select id="${id}" class="form-select form-select-sm" data-key="${esc(f.key)}" data-type="text">${opts}</select></div>`;
            }
            case 'column': {
                const opts = ['<option value="">— pick a column —</option>']
                    .concat(cols.map(c => `<option value="${esc(c)}" ${value === c ? 'selected' : ''}>${esc(c)}</option>`)).join('');
                const fallback = cols.length === 0
                    ? `<input id="${id}" type="text" class="form-control form-control-sm" data-key="${esc(f.key)}" data-type="text" value="${esc(value ?? '')}" placeholder="Run preview to load columns, or type a name">`
                    : `<select id="${id}" class="form-select form-select-sm" data-key="${esc(f.key)}" data-type="text">${opts}</select>`;
                return `<div class="twp-field">${lbl}${fallback}</div>`;
            }
            case 'columns': {
                const arr = Array.isArray(value) ? value : (value ? [value] : []);
                return `<div class="twp-field">${lbl}
                    <div class="twp-chips" data-widget="columns" data-key="${esc(f.key)}" data-options='${esc(JSON.stringify(cols))}' data-value='${esc(JSON.stringify(arr))}'></div>
                    <div class="form-text small">Tip: click chips to remove. Use the picker to add.</div>
                </div>`;
            }
            case 'query': {
                const opts = ['<option value="">— pick a query —</option>']
                    .concat(otherQs.map(q => `<option value="${esc(q.name)}" ${value === q.name ? 'selected' : ''}>${esc(q.name)}</option>`)).join('');
                return `<div class="twp-field">${lbl}<select id="${id}" class="form-select form-select-sm" data-key="${esc(f.key)}" data-type="text">${opts}</select>${otherQs.length === 0 ? '<div class="form-text small text-warning">No other queries in this workspace yet.</div>' : ''}</div>`;
            }
            case 'queries': {
                const arr = Array.isArray(value) ? value : [];
                const names = otherQs.map(q => q.name);
                return `<div class="twp-field">${lbl}
                    <div class="twp-chips" data-widget="queries" data-key="${esc(f.key)}" data-options='${esc(JSON.stringify(names))}' data-value='${esc(JSON.stringify(arr))}'></div>
                </div>`;
            }
            case 'aggregations': {
                const arr = Array.isArray(value) ? value : [];
                return `<div class="twp-field">${lbl}
                    <div class="twp-aggs" data-widget="aggregations" data-key="${esc(f.key)}" data-cols='${esc(JSON.stringify(cols))}' data-value='${esc(JSON.stringify(arr))}'></div>
                </div>`;
            }
            default:
                return '';
        }
    }

    // ── Chip multi-picker (columns / queries) ──────────────────────────
    function wireChipPicker(host) {
        const opts = JSON.parse(host.dataset.options || '[]');
        const value = JSON.parse(host.dataset.value || '[]');
        const render = () => {
            const remaining = opts.filter(o => !value.includes(o));
            host.innerHTML = `
                <div class="twp-chip-list">
                    ${value.map((v, i) => `<span class="twp-chip" data-idx="${i}">${esc(v)}<button type="button" class="twp-chip-x" aria-label="Remove">&times;</button></span>`).join('')}
                </div>
                <div class="twp-chip-add">
                    ${remaining.length > 0
                        ? `<select class="form-select form-select-sm twp-chip-select"><option value="">+ add…</option>${remaining.map(o => `<option value="${esc(o)}">${esc(o)}</option>`).join('')}</select>`
                        : `<input type="text" class="form-control form-control-sm twp-chip-input" placeholder="+ add custom…">`}
                </div>`;
            host.querySelectorAll('.twp-chip-x').forEach((b, i) => b.addEventListener('click', () => { value.splice(i, 1); host.dataset.value = JSON.stringify(value); render(); }));
            const sel = host.querySelector('.twp-chip-select');
            if (sel) sel.addEventListener('change', e => { if (e.target.value) { value.push(e.target.value); host.dataset.value = JSON.stringify(value); render(); } });
            const inp = host.querySelector('.twp-chip-input');
            if (inp) inp.addEventListener('keydown', e => { if (e.key === 'Enter' && inp.value.trim()) { e.preventDefault(); value.push(inp.value.trim()); host.dataset.value = JSON.stringify(value); render(); } });
        };
        render();
    }

    // ── Aggregations builder for group_by ──────────────────────────────
    function wireAggregationsEditor(host) {
        const cols = JSON.parse(host.dataset.cols || '[]');
        const value = JSON.parse(host.dataset.value || '[]');
        const ops = ['count','sum','avg','min','max','distinct_count'];
        const render = () => {
            host.innerHTML = `
                <div class="twp-aggs-list">
                    ${value.map((a, i) => `
                        <div class="twp-agg-row" data-idx="${i}">
                            <input type="text" class="form-control form-control-sm twp-agg-name" placeholder="Result name" value="${esc(a.name || '')}">
                            <select class="form-select form-select-sm twp-agg-op">${ops.map(o => `<option value="${o}" ${a.op === o ? 'selected' : ''}>${o}</option>`).join('')}</select>
                            <select class="form-select form-select-sm twp-agg-col">
                                <option value="">(rows)</option>
                                ${cols.map(c => `<option value="${esc(c)}" ${a.column === c ? 'selected' : ''}>${esc(c)}</option>`).join('')}
                            </select>
                            <button type="button" class="btn btn-sm btn-outline-danger twp-agg-x" title="Remove"><i class="bi bi-x-lg"></i></button>
                        </div>`).join('')}
                </div>
                <button type="button" class="btn btn-sm btn-outline-secondary twp-agg-add"><i class="bi bi-plus-lg me-1"></i>Add aggregation</button>
            `;
            host.querySelectorAll('.twp-agg-row').forEach((row, i) => {
                const sync = () => {
                    value[i] = {
                        name:   row.querySelector('.twp-agg-name').value.trim() || ('Agg' + (i+1)),
                        op:     row.querySelector('.twp-agg-op').value,
                        column: row.querySelector('.twp-agg-col').value || undefined
                    };
                    host.dataset.value = JSON.stringify(value);
                };
                row.querySelectorAll('input, select').forEach(el => el.addEventListener('change', sync));
                row.querySelector('.twp-agg-x').addEventListener('click', () => { value.splice(i, 1); host.dataset.value = JSON.stringify(value); render(); });
            });
            host.querySelector('.twp-agg-add').addEventListener('click', () => { value.push({ name:'Count', op:'count' }); host.dataset.value = JSON.stringify(value); render(); });
        };
        render();
    }

    // ── AI suggest for the active step ─────────────────────────────────
    async function runStepAiSuggest(step) {
        const prompt = ($('twpStepAiPrompt').value || '').trim();
        if (!prompt) return;
        const status = $('twpStepAiStatus');
        status.textContent = 'Asking AI…';
        const q = activeQuery();
        let suggested = null;
        try {
            const r = await fetch('/api/transforms/ai-suggest', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken() },
                body: JSON.stringify({
                    datasourceGuid: q?.guid,
                    op: step.op,
                    columns: getColumnsForActive(),
                    otherQueries: state.queries.filter(x => x.guid !== state.activeQueryGuid).map(x => x.name),
                    prompt
                })
            });
            if (r.ok) suggested = await r.json();
        } catch { /* offline / no endpoint */ }

        // Pick first matching step from server, else local fallback for this op.
        let params = null;
        if (suggested && Array.isArray(suggested.steps)) {
            const match = suggested.steps.find(s => s.op === step.op) || suggested.steps[0];
            if (match && match.params) params = match.params;
        }
        if (!params) params = localStepAiFallback(step.op, prompt, q);

        if (!params) { status.textContent = "I couldn't infer values from that. Try being more specific."; return; }

        // Merge into current step and re-render the form.
        step.params = Object.assign({}, step.params, params);
        renderStepForm(step);
        status.textContent = 'Filled fields from AI suggestion.';
    }

    function localStepAiFallback(op, text, q) {
        const t = text.toLowerCase();
        switch (op) {
            case 'merge': {
                const m = text.match(/(?:merge|join)\s+(?:with\s+)?([A-Za-z0-9_ ]+?)\s+on\s+([A-Za-z0-9_]+)(?:\s+and\s+([A-Za-z0-9_]+))?/i);
                const how = /left\s*join/i.test(text) ? 'left' : /right\s*join/i.test(text) ? 'right' : /full\s*join/i.test(text) ? 'full' : 'inner';
                if (m) return { right: m[1].trim(), left_key: m[2], right_key: m[3] || m[2], how };
                return null;
            }
            case 'append': {
                const m = text.match(/append(?:\s+rows)?\s+(?:from\s+)?([A-Za-z0-9_ ]+)/i);
                if (m) return { source: m[1].trim(), align_columns: true };
                return null;
            }
            case 'filter': return { expression: text };
            case 'select_rows': return { expression: text };
            case 'derived_field':
            case 'add_column': {
                const m = text.match(/(?:add|create)\s+(?:column\s+)?([A-Za-z0-9_]+)\s*=\s*(.+)/i);
                if (m) return { target_field: m[1], expression: m[2].trim() };
                return null;
            }
            case 'change_type': {
                const m = text.match(/([A-Za-z0-9_]+)\s+to\s+(text|integer|whole number|decimal|number|boolean|date|datetime|date\/time|time)/i);
                if (m) {
                    const map = { 'whole number':'integer', 'number':'decimal', 'date/time':'datetime' };
                    return { column: m[1], type: map[m[2].toLowerCase()] || m[2].toLowerCase() };
                }
                return null;
            }
            case 'replace_values': {
                const m = text.match(/replace\s+"?([^"]+?)"?\s+with\s+"?([^"]*?)"?(?:\s+in\s+([A-Za-z0-9_]+))?/i);
                if (m) return { column: m[3] || '', from: m[1], to: m[2] };
                return null;
            }
            case 'group_by': {
                const m = text.match(/group\s+by\s+([A-Za-z0-9_, ]+?)(?:\s+(?:and\s+)?(sum|avg|count|min|max)\s+([A-Za-z0-9_]+))?/i);
                if (m) {
                    const keys = m[1].split(/[, ]+/).filter(Boolean);
                    const aggs = m[2] ? [{ name: (m[2][0].toUpperCase()+m[2].slice(1)) + (m[3]||''), op: m[2].toLowerCase(), column: m[3] }] : [{ name:'Count', op:'count' }];
                    return { keys, aggregations: aggs };
                }
                return null;
            }
            case 'remove_columns': {
                const m = text.match(/remove\s+columns?\s+([A-Za-z0-9_, ]+)/i);
                if (m) return { columns: m[1].split(/[, ]+/).filter(Boolean) };
                return null;
            }
            default: return null;
        }
    }

    function closeStepEditor() {
        $('twpStepEditor').style.display = 'none';
        hideStepEditorError();
        setStepEditorBusy(false);
        editingStepIdx = -1;
        editStartSnapshot = null;
    }

    // Inline error inside the step editor — shown when applying the step
    // causes the engine to reject the resulting pipeline. Keeps the panel
    // open (so the user keeps their in-progress edits) and explains both
    // what failed and the most likely reason.
    function showStepEditorError(message, reason) {
        const box = $('twpStepEditorError');
        if (!box) return;
        const msg = $('twpStepEditorErrorMessage');
        const why = $('twpStepEditorErrorReason');
        if (msg) msg.textContent = message || 'Apply failed.';
        if (why) {
            if (reason) { why.textContent = 'Why: ' + reason; why.style.display = ''; }
            else { why.textContent = ''; why.style.display = 'none'; }
        }
        box.style.display = '';
        try { box.scrollIntoView({ block: 'nearest', behavior: 'smooth' }); } catch { /* noop */ }
    }
    function hideStepEditorError() {
        const box = $('twpStepEditorError');
        if (box) box.style.display = 'none';
    }
    function setStepEditorBusy(busy) {
        const save = $('twpStepEditorSave');
        const cancel = $('twpStepEditorCancel');
        if (save) {
            save.disabled = !!busy;
            save.textContent = busy ? 'Applying…' : 'Save';
        }
        if (cancel) cancel.disabled = !!busy;
    }

    // Best-effort interpretation of a server preview error so the user
    // understands *why* the apply was rejected, not just that it was.
    function explainPreviewError(message) {
        const m = (message || '').toLowerCase();
        if (!m) return 'The transform engine rejected the configuration after applying this step.';
        if (m.includes('column') && (m.includes('not found') || m.includes('unknown') || m.includes("doesn't exist") || m.includes('does not exist')))
            return 'A column referenced by this step is missing from the current dataset. Re-pick the column or remove the reference.';
        if (m.includes('parse') || m.includes('syntax') || m.includes('unexpected token'))
            return 'The expression / formula could not be parsed. Check brackets, quotes and operators.';
        if (m.includes('type') && (m.includes('mismatch') || m.includes('cannot convert') || m.includes('invalid cast')))
            return 'A value or column type does not match what this operation expects (e.g. text used where a number is required).';
        if (m.includes('divide by zero')) return 'A division in the expression evaluates to divide-by-zero on at least one row.';
        if (m.includes('duplicate')) return 'The configuration produces duplicate column or key names that the engine will not accept.';
        if (m.includes('required') || m.includes('missing'))
            return 'A required parameter for this step is empty or missing.';
        if (m.includes('timeout')) return 'The preview took too long to run. Reduce the data volume or simplify the step.';
        return 'The transform engine rejected the configuration after applying this step. Adjust the fields above and try again.';
    }

    // Cancel = revert any live edits made since the editor opened.
    function cancelStepEditor() {
        const q = activeQuery();
        if (q && editingStepIdx >= 0 && editStartSnapshot && q.steps[editingStepIdx]) {
            q.steps[editingStepIdx].params = editStartSnapshot.params || {};
            q.steps[editingStepIdx].enabled = editStartSnapshot.enabled !== false;
            renderStepsList();
            renderTomlFromSteps();
            schedulePreview();
        }
        closeStepEditor();
    }

    function saveStepEditor() {
        const q = activeQuery(); if (!q) return closeStepEditor();
        const step = q.steps[editingStepIdx]; if (!step) return closeStepEditor();

        // If the user opened the Advanced section, prefer raw JSON; otherwise
        // collect values from the form fields.
        const adv = document.querySelector('.twp-advanced');
        let nextParams;
        if (adv && adv.open) {
            const json = $('twpStepJson').value;
            try { nextParams = JSON.parse(json || '{}'); }
            catch (e) {
                setStatus('Invalid JSON: ' + e.message, 'error');
                showStepEditorError('Invalid JSON in the Advanced section.', e.message);
                return;
            }
        } else {
            nextParams = collectFormParams();
        }

        // Block save when any DAX/expression field has hard errors. Warnings
        // (unknown column / function) are allowed through so users can still
        // experiment, but unbalanced brackets or empty required formulas are
        // refused so we never persist a step that the engine will reject.
        const exprKeys = (schemaFor(step.op) || []).filter(f => f.type === 'expression').map(f => f.key);
        if (exprKeys.length && typeof window.TWP_validateExpression === 'function') {
            const cols = getColumnsForActive();
            for (const k of exprKeys) {
                const v = nextParams?.[k];
                if (v == null || String(v).trim() === '') {
                    const msg = `The "${k}" formula is required.`;
                    setStatus(msg, 'error');
                    showStepEditorError(msg, 'A required formula field is empty.');
                    return;
                }
                const res = window.TWP_validateExpression(String(v), cols);
                if (res && res.ok === false) {
                    const first = (res.issues || []).find(i => i.level === 'error') || res.issues?.[0];
                    const detail = first ? first.message : '';
                    setStatus('Invalid formula' + (detail ? ': ' + detail : '.') + ' Step was not saved.', 'error');
                    showStepEditorError('Invalid formula in "' + k + '". Step was not saved.', detail || 'Check brackets, quotes and operators in the expression.');
                    return;
                }
            }
        }

        // Apply changes optimistically, then run a server preview. If the
        // preview fails the panel is kept open with an inline error so the
        // user can see (and fix) what went wrong without losing their edits.
        const prevParams = JSON.parse(JSON.stringify(step.params || {}));
        const prevEnabled = step.enabled !== false;
        step.params = nextParams;
        step.enabled = !!$('twpStepEnabled').checked;

        hideStepEditorError();
        setStepEditorBusy(true);

        // Reflect the change in the steps list / TOML / status bar even while
        // we wait for preview — the user asked for the panel to *update* its
        // changes immediately.
        renderQueriesList();
        renderStepsList();
        renderTomlFromSteps();
        updateStatusBar({});

        // Cancel any pending debounced preview so we get a fresh, awaited one.
        clearTimeout(_previewDebounce);

        runPreview().then(result => {
            setStepEditorBusy(false);
            if (result && result.ok === false) {
                // Roll back the in-memory step so the preview pane and TOML
                // keep matching the last known-good pipeline state, but leave
                // the editor form populated with the rejected values so the
                // user can correct them.
                step.params = prevParams;
                step.enabled = prevEnabled;
                renderStepsList();
                renderTomlFromSteps();
                showStepEditorError(result.error || 'Apply failed.', explainPreviewError(result.error));
                return;
            }
            editStartSnapshot = null; // commit
            closeStepEditor();
        }).catch(err => {
            setStepEditorBusy(false);
            step.params = prevParams;
            step.enabled = prevEnabled;
            renderStepsList();
            renderTomlFromSteps();
            const msg = (err && err.message) ? err.message : 'Apply failed.';
            showStepEditorError(msg, explainPreviewError(msg));
        });
    }

    function collectFormParams() {
        const params = {};
        document.querySelectorAll('.twp-form-fields [data-key]').forEach(el => {
            const key = el.dataset.key;
            const type = el.dataset.type;
            if (el.tagName === 'SELECT' || (el.tagName === 'INPUT' && type === 'text') || el.tagName === 'TEXTAREA') {
                const v = el.value;
                if (v !== '' && v != null) params[key] = v;
            } else if (el.tagName === 'INPUT' && type === 'number') {
                if (el.value !== '') params[key] = Number(el.value);
            } else if (el.tagName === 'INPUT' && type === 'boolean') {
                params[key] = !!el.checked;
            }
        });
        document.querySelectorAll('.twp-form-fields [data-widget]').forEach(host => {
            const key = host.dataset.key;
            const widget = host.dataset.widget;
            if (widget === 'columns' || widget === 'queries') {
                params[key] = JSON.parse(host.dataset.value || '[]');
            } else if (widget === 'aggregations') {
                params[key] = JSON.parse(host.dataset.value || '[]');
            }
        });
        return params;
    }

    // ── View toggle / TOML ─────────────────────────────────────────────
    function wireViewTabs() {
        document.querySelectorAll('.twp-view-tab').forEach(b => {
            b.addEventListener('click', () => setView(b.dataset.view));
        });
    }

    function setView(v) {
        state.view = v;
        document.querySelectorAll('.twp-view-tab').forEach(t => t.classList.toggle('active', t.dataset.view === v));
        $('twpViewPreview').classList.toggle('active', v === 'preview');
        $('twpViewToml').classList.toggle('active', v === 'toml');
    }

    function buildToml(steps) {
        const lines = [
            '[transform]',
            'name = "AI Insights 365 ETL"',
            'enabled = true',
            'dax_like_expressions = true',
            ''
        ];
        (steps || []).forEach(rawStep => {
            const s = translateStepForServer(rawStep);
            lines.push('[[rules]]');
            lines.push(`type = "${s.op}"`);
            lines.push(`enabled = ${s.enabled === false ? 'false' : 'true'}`);
            const p = s.params || {};
            Object.keys(p).forEach(k => {
                // `type` is reserved for the rule discriminator emitted above;
                // a stray params.type (e.g. from an untranslated change_type
                // step) would produce a duplicate-key TOML parse error.
                if (k === 'type' || k === 'enabled') return;
                const v = p[k];
                if (Array.isArray(v)) {
                    lines.push(`${k} = [${v.map(x => typeof x === 'number' ? x : `"${String(x).replace(/\\/g,'\\\\').replace(/"/g,'\\"')}"`).join(', ')}]`);
                } else if (typeof v === 'boolean') {
                    lines.push(`${k} = ${v ? 'true' : 'false'}`);
                } else if (typeof v === 'number') {
                    lines.push(`${k} = ${v}`);
                } else if (v && typeof v === 'object') {
                    // Inline TOML table — used by rules whose params are
                    // dictionaries (e.g. rename_columns mapping, cast_types
                    // types, map_codes ranges).
                    const entries = Object.keys(v).map(kk => {
                        const vv = v[kk];
                        const ks = String(kk).replace(/\\/g,'\\\\').replace(/"/g,'\\"');
                        if (typeof vv === 'number')  return `"${ks}" = ${vv}`;
                        if (typeof vv === 'boolean') return `"${ks}" = ${vv ? 'true' : 'false'}`;
                        return `"${ks}" = "${String(vv ?? '').replace(/\\/g,'\\\\').replace(/"/g,'\\"')}"`;
                    });
                    lines.push(`${k} = { ${entries.join(', ')} }`);
                } else if (v != null) {
                    lines.push(`${k} = "${String(v).replace(/\\/g,'\\\\').replace(/"/g,'\\"')}"`);
                }
            });
            lines.push('');
        });
        return lines.join('\n');
    }

    // Some UI affordances emit friendly op names (e.g. sort_asc / sort_desc,
    // text_*, num_*, date_*) that don't match a server-registered rule
    // handler. Translate them here to canonical server ops and parameter
    // shapes so the engine actually executes them — and the preview, validate
    // and publish flows all stay in sync. Anything we can't map to a real
    // handler is forwarded unchanged so server-side validation flags it as
    // unsupported_rule (validate should *fail* in that case, per UX ask).
    function translateStepForServer(s) {
        if (!s || !s.op) return s;
        const p = { ...(s.params || {}) };
        const wrap = (op, params) => ({ op, enabled: s.enabled, params });
        const escDax = (v) => String(v).replace(/\\/g, '\\\\').replace(/"/g, '\\"');
        const derivedSelf = (exprFn) => {
            const c = p.column || p.field || '';
            return wrap('derived_field', {
                target_field: c,
                expression: c ? exprFn(c) : ''
            });
        };
        const derivedNew = (exprFn, suffix) => {
            const c = p.column || p.field || '';
            return wrap('derived_field', {
                target_field: p.target_field || (c ? c + suffix : suffix),
                expression: c ? exprFn(c) : ''
            });
        };

        switch (s.op) {
            // ── direct aliases (UI op → registered rule handler) ───────
            case 'sort_asc':
            case 'sort_desc':
                return wrap('sort_rows', {
                    by: p.by || p.column || p.field || '',
                    direction: s.op === 'sort_desc' ? 'desc' : 'asc'
                });

            case 'filter':
            case 'select_rows':
                return wrap('filter_rows', { condition: p.condition || p.expression || '' });

            case 'remove_columns':
                return wrap('select_columns', {
                    exclude: Array.isArray(p.columns) ? p.columns : (p.columns ? [p.columns] : [])
                });

            case 'rename_column':
                return wrap('rename_columns', {
                    mapping: (p.from && p.to) ? { [p.from]: p.to } : {}
                });

            case 'change_type':
                return wrap('cast_types', {
                    types: (p.column && p.type) ? { [p.column]: p.type } : {}
                });

            case 'pivot':
                return wrap('pivot_table', {
                    index: p.index || '',
                    columns: p.column || '',
                    values: p.values_column || ''
                });

            case 'unpivot':
                return wrap('unpivot_table', {
                    columns: Array.isArray(p.columns) ? p.columns : [],
                    name_field: p.attribute_name || 'attribute',
                    value_field: p.value_name || 'value'
                });

            case 'transpose':
                return wrap('transpose_table', {});

            case 'append':
                return wrap('append_rows', { sources: p.source ? [p.source] : [] });

            case 'merge':
                return wrap('merge_tables', {
                    left_key: p.left_key || '',
                    right_key: p.right_key || p.left_key || '',
                    left_source: 'left',
                    right_source: p.right || 'right',
                    join_type: p.how || 'inner'
                });

            case 'group_by':
                return wrap('aggregate', {
                    group_by: Array.isArray(p.keys) ? p.keys : [],
                    metrics: (Array.isArray(p.aggregations) ? p.aggregations : []).map(a => {
                        const fn = String(a.op || 'count').toLowerCase();
                        const field = a.column || (fn === 'count' ? '*' : '');
                        const alias = a.name || (fn + '_' + (a.column || 'rows'));
                        return `${fn}:${field}:${alias}`;
                    })
                });

            case 'replace_values':
                return wrap('replace_values', {
                    field: p.column || p.field || '',
                    find: p.from ?? p.find ?? '',
                    replace: p.to ?? p.replace ?? ''
                });

            case 'split_column':
                return wrap('split_column', {
                    field: p.column || p.field || '',
                    delimiter: p.delimiter || ',',
                    targets: Array.isArray(p.into) ? p.into : (Array.isArray(p.targets) ? p.targets : [])
                });

            // ── add_column / conditional / duplicate → derived_field ──
            case 'add_column':
                return wrap('derived_field', {
                    target_field: p.target_field || '',
                    expression: p.value != null ? String(p.value) : (p.expression || '')
                });

            case 'conditional':
                return wrap('derived_field', {
                    target_field: p.target_field || '',
                    expression: p.expression || ''
                });

            case 'duplicate_column':
                return wrap('derived_field', {
                    target_field: p.target_field || (p.source ? p.source + '_copy' : ''),
                    expression: p.source ? `[${p.source}]` : ''
                });

            // ── text ops (DAX-style derived_field) ─────────────────────
            case 'text_upper':   return derivedSelf(c => `UPPER([${c}])`);
            case 'text_lower':   return derivedSelf(c => `LOWER([${c}])`);
            case 'text_proper':  return derivedSelf(c => `PROPER([${c}])`);
            case 'text_trim':    return derivedSelf(c => `TRIM([${c}])`);
            case 'text_clean':   return derivedSelf(c => `CLEAN([${c}])`);
            case 'text_replace':
                return wrap('replace_values', {
                    field: p.column || '',
                    find: p.from || '',
                    replace: p.to || ''
                });
            case 'text_extract':
                return wrap('derived_field', {
                    target_field: p.target_field || ((p.column || 'col') + '_extract'),
                    expression: p.column ? `MID([${p.column}], ${(Number(p.start) || 0) + 1}, ${Number(p.length) || 1})` : ''
                });
            case 'text_length':
                return wrap('derived_field', {
                    target_field: p.target_field || ((p.column || 'col') + 'Length'),
                    expression: p.column ? `LEN([${p.column}])` : ''
                });
            case 'text_contains':
                return wrap('derived_field', {
                    target_field: p.target_field || ((p.column || 'col') + '_has'),
                    expression: (p.column && p.value != null)
                        ? `IF(SEARCH("${escDax(p.value)}",[${p.column}],1,0) > 0, TRUE, FALSE)`
                        : ''
                });

            // ── number ops ─────────────────────────────────────────────
            case 'num_round':       return derivedSelf(c => `ROUND([${c}], ${Number(p.digits) || 0})`);
            case 'num_round_up':    return derivedSelf(c => `ROUNDUP([${c}], 0)`);
            case 'num_round_down':  return derivedSelf(c => `ROUNDDOWN([${c}], 0)`);
            case 'num_abs':         return derivedSelf(c => `ABS([${c}])`);
            case 'num_power':       return derivedSelf(c => `POWER([${c}], ${Number(p.exponent) || 2})`);
            case 'num_log':         return derivedSelf(c => `LOG([${c}], ${Number(p.base) || 10})`);
            case 'num_mod':         return derivedSelf(c => `MOD([${c}], ${Number(p.divisor) || 1})`);

            // ── date / time ────────────────────────────────────────────
            case 'date_year':           return derivedNew(c => `YEAR([${c}])`,         'Year');
            case 'date_month':          return derivedNew(c => `MONTH([${c}])`,        'Month');
            case 'date_day':            return derivedNew(c => `DAY([${c}])`,          'Day');
            case 'date_add_days':       return derivedSelf(c => `DATEADD([${c}], ${Number(p.days) || 0}, "day")`);
            case 'date_add_months':     return derivedSelf(c => `DATEADD([${c}], ${Number(p.months) || 0}, "month")`);
            case 'date_start_of_month': return derivedNew(c => `STARTOFMONTH([${c}])`, 'MonthStart');
            case 'date_end_of_month':   return derivedNew(c => `ENDOFMONTH([${c}])`,   'MonthEnd');
            case 'date_now':
                return wrap('derived_field', { target_field: p.target_field || 'Now', expression: 'NOW()' });

            case 'time_hour':   return derivedNew(c => `HOUR([${c}])`,   'Hour');
            case 'time_minute': return derivedNew(c => `MINUTE([${c}])`, 'Minute');
            case 'time_second': return derivedNew(c => `SECOND([${c}])`, 'Second');

            // ── misc shape ─────────────────────────────────────────────
            case 'reorder_columns':
                return wrap('select_columns', {
                    include: Array.isArray(p.order) ? p.order : []
                });
            case 'merge_columns': {
                const cols = Array.isArray(p.columns) ? p.columns : [];
                const sep = String(p.separator ?? ' ');
                return wrap('derived_field', {
                    target_field: p.target_field || cols.join('_'),
                    expression: cols.length
                        ? cols.map(c => `[${c}]`).join(`&"${escDax(sep)}"&`)
                        : ''
                });
            }
            case 'index_column':
                return wrap('derived_field', {
                    target_field: p.target_field || 'Index',
                    expression: String(Number(p.start) || 1)
                });

            // Promote headers: take the values of the first preview row and
            // rename each column accordingly. The original first row is
            // retained — the user removes it via the row-× action (status
            // hint shown when the step is added from the ribbon).
            case 'promote_headers': {
                const row = firstPreviewRow();
                if (!row) return s; // no preview yet → server flags unsupported
                const mapping = {};
                const seen = new Set();
                Object.keys(row).forEach(k => {
                    let v = String(row[k] ?? '').trim();
                    if (!v) v = k; // keep original name for empty cells
                    let candidate = v, n = 2;
                    while (seen.has(candidate)) { candidate = v + '_' + n; n++; }
                    seen.add(candidate);
                    mapping[k] = candidate;
                });
                return wrap('rename_columns', { mapping });
            }

            // No registered handler → leave unchanged so server validation
            // surfaces it as 'unsupported_rule' and the user sees a clear
            // error. (fill_down, fill_up, remove_rows, remove_blank_rows,
            // demote_headers, count_rows, reverse, etc.)
            // Strip `type` from params here too so it can never collide with
            // the reserved rule discriminator emitted by buildToml.
            default: {
                if (Object.prototype.hasOwnProperty.call(p, 'type')) {
                    const safe = { ...p }; delete safe.type;
                    return { op: s.op, enabled: s.enabled, params: safe };
                }
                return s;
            }
        }
    }

    function renderTomlFromSteps() {
        const q = activeQuery();
        $('twpTomlEditor').value = q ? buildToml(q.steps) : '';
    }

    function wireToolbar() {
        $('twpRefreshBtn').addEventListener('click', () => loadQueries().catch(err => setStatus(err.message, 'error')));
        $('twpPreviewBtn').addEventListener('click', runPreview);
        $('twpValidateBtn').addEventListener('click', runValidate);
        $('twpPublishBtn').addEventListener('click', runPublish);
        $('twpTomlFromStepsBtn').addEventListener('click', renderTomlFromSteps);
        $('twpTomlApplyBtn').addEventListener('click', () => {
            // Lightweight TOML → steps parser. Keeps the steps in sync with any
            // hand edits before Publish. Only handles the subset emitted by
            // buildToml(); anything more exotic should round-trip via Publish.
            const txt = $('twpTomlEditor').value || '';
            const blocks = splitTomlBlocks(txt);
            const newSteps = blocks
                .filter(b => b.header === '[[rules]]')
                .map(b => {
                    const params = { ...b.fields };
                    const op = params.type || ''; delete params.type;
                    const enabled = params.enabled !== false; delete params.enabled;
                    return { op, enabled, params };
                })
                .filter(s => !!s.op);
            const q = activeQuery();
            if (q) { q.steps = newSteps; onStepsChanged(); setStatus('Applied to steps.', 'success'); }
        });
    }

    function splitTomlBlocks(txt) {
        const lines = String(txt || '').split(/\r?\n/);
        const out = [];
        let cur = null;
        for (const line of lines) {
            const t = line.trim();
            if (!t || t.startsWith('#')) continue;
            if (t.startsWith('[')) {
                if (cur) out.push(cur);
                cur = { header: t, fields: {} };
                continue;
            }
            if (!cur) continue;
            const m = t.match(/^([A-Za-z0-9_]+)\s*=\s*(.+)$/);
            if (!m) continue;
            cur.fields[m[1]] = parseTomlValue(m[2]);
        }
        if (cur) out.push(cur);
        return out;
    }

    function parseTomlValue(raw) {
        const s = raw.trim().replace(/\s*#.*$/, '');
        if (s === 'true') return true;
        if (s === 'false') return false;
        if (/^-?\d+(\.\d+)?$/.test(s)) return Number(s);
        if (s.startsWith('[') && s.endsWith(']')) {
            const inner = s.slice(1, -1).trim();
            if (!inner) return [];
            return inner.split(/\s*,\s*/).map(x => parseTomlValue(x));
        }
        if (s.startsWith('"') && s.endsWith('"')) {
            return s.slice(1, -1).replace(/\\"/g, '"').replace(/\\\\/g, '\\');
        }
        return s;
    }

    // ── Server interactions ────────────────────────────────────────────
    async function runPreview() {
        const q = activeQuery();
        if (!q) { setStatus('Pick a query first.', 'warn'); return { ok: false, error: 'Pick a query first.' }; }
        const toml = buildToml(q.steps);
        setProgress(true);
        setStatus('Running preview…');
        const t0 = performance.now();
        try {
            const r = await fetch('/api/datasources/' + encodeURIComponent(q.guid) + '/transform/preview', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken() },
                body: JSON.stringify({ toml, page: 1, pageSize: 1000 })
            });
            const data = await r.json().catch(() => ({}));
            const t1 = performance.now();
            if (!r.ok) throw new Error(data.error || ('Preview failed (' + r.status + ')'));
            renderPreview(data);
            hidePreviewError();
            clearErrorColumn();
            const rows = data.rows || data.data || [];
            const cols = data.columns || (rows.length > 0 ? Object.keys(rows[0]) : []);
            updateStatusBar({
                rows: data.rowCount != null ? data.rowCount : rows.length,
                cols: cols.length,
                clientMs: t1 - t0,
                serverMs: data.renderDurationMs
            });
            setStatus('Preview ready.', 'success');
        } catch (e) {
            setStatus(e.message || 'Preview failed.', 'error');
            updateStatusBar({ validity: { text: 'Preview failed', kind: 'error' } });
            showPreviewError(e.message || 'Preview failed.');
            // Highlight the offending column on the preview header so the
            // user can see exactly which field broke the pipeline.
            const col = extractErrorColumn(e.message || '') || guessErrorColumnFromEditingStep();
            if (col) highlightErrorColumn(col);
            return { ok: false, error: e.message || 'Preview failed.' };
        } finally {
            setProgress(false);
        }
        return { ok: true };
    }

    // Best-effort: pull a column / field name out of a server error message.
    // Server messages vary; we look for common shapes:
    //   `Column "Foo" not found`, `field 'Foo'`, `[Foo]`, `column Foo is ...`.
    function extractErrorColumn(message) {
        if (!message) return null;
        const patterns = [
            /column\s+["'`]([^"'`]+)["'`]/i,
            /field\s+["'`]([^"'`]+)["'`]/i,
            /\[([A-Za-z0-9_ ]+)\]/,
            /column\s+([A-Za-z0-9_]+)/i,
            /field\s+([A-Za-z0-9_]+)/i
        ];
        for (const re of patterns) {
            const m = message.match(re);
            if (m && m[1]) return m[1].trim();
        }
        return null;
    }

    // If the error didn't name a column, fall back to the column the user
    // is currently editing (rename_column.from, change_type.column, etc.).
    function guessErrorColumnFromEditingStep() {
        const q = activeQuery();
        if (!q || editingStepIdx < 0) return null;
        const s = q.steps[editingStepIdx]; if (!s) return null;
        const p = s.params || {};
        return p.column || p.field || p.from || p.source || p.by || null;
    }

    function highlightErrorColumn(col) {
        clearErrorColumn();
        if (!col) return;
        const tbl = $('twpPreviewTable');
        if (!tbl) return;
        const th = tbl.querySelector(`thead th[data-col="${(window.CSS && CSS.escape) ? CSS.escape(col) : col.replace(/"/g,'\\"')}"]`);
        if (th) th.classList.add('twp-col-error');
    }

    function clearErrorColumn() {
        const tbl = $('twpPreviewTable');
        if (!tbl) return;
        tbl.querySelectorAll('thead th.twp-col-error').forEach(th => th.classList.remove('twp-col-error'));
    }

    // Sniff the column data type from up to ~25 sampled non-null values.
    // Returns one of: text | integer | decimal | boolean | date | datetime.
    // Used to render an inline type pill in the preview header so users can
    // see (and click to change) each column's type at a glance.
    function inferColType(rows, col) {
        const sample = [];
        for (let i = 0; i < rows.length && sample.length < 25; i++) {
            const v = rows[i]?.[col];
            if (v !== null && v !== undefined && String(v).trim() !== '') sample.push(v);
        }
        if (sample.length === 0) return 'text';
        let allBool = true, allInt = true, allDec = true, allDate = true, allDateTime = true;
        const isoDate = /^\d{4}-\d{2}-\d{2}$/;
        const isoDateTime = /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/;
        for (const v of sample) {
            const s = String(v).trim();
            if (allBool && !/^(true|false|yes|no|0|1)$/i.test(s)) allBool = false;
            if (allInt && !/^-?\d+$/.test(s)) allInt = false;
            if (allDec && !/^-?\d+(\.\d+)?$/.test(s)) allDec = false;
            if (allDate && !isoDate.test(s)) allDate = false;
            if (allDateTime && !isoDateTime.test(s)) allDateTime = false;
        }
        if (allBool && sample.every(v => /^(true|false)$/i.test(String(v).trim()))) return 'boolean';
        if (allInt) return 'integer';
        if (allDec) return 'decimal';
        if (allDateTime) return 'datetime';
        if (allDate) return 'date';
        return 'text';
    }
    const TYPE_META = {
        text:     { icon: 'bi-type',          label: 'Text' },
        integer:  { icon: 'bi-123',           label: 'Whole Number' },
        decimal:  { icon: 'bi-percent',       label: 'Decimal' },
        boolean:  { icon: 'bi-toggle-on',     label: 'Boolean' },
        date:     { icon: 'bi-calendar',      label: 'Date' },
        datetime: { icon: 'bi-clock',         label: 'Date/Time' },
        time:     { icon: 'bi-clock-history', label: 'Time' }
    };

    function renderPreview(payload) {
        const empty = $('twpPreviewEmpty');
        const wrap = $('twpPreviewWrap');
        const tbl = $('twpPreviewTable');
        const pill = $('twpRowCountPill');

        const rows = payload.rows || payload.data || [];
        const cols = payload.columns
            || (rows.length > 0 ? Object.keys(rows[0]) : []);

        if (cols.length === 0) {
            empty.style.display = '';
            wrap.style.display = 'none';
            pill.style.display = 'none';
            return;
        }
        empty.style.display = 'none';
        wrap.style.display = '';
        pill.style.display = '';
        pill.textContent = `${rows.length} row${rows.length === 1 ? '' : 's'}`;

        // Build a per-column type map once and stash it for the enhancements
        // module (column menu's "Change type" submenu reads it).
        const typeMap = {};
        cols.forEach(c => { typeMap[c] = inferColType(rows, c); });
        tbl.dataset.colTypes = JSON.stringify(typeMap);
        // Cache the typed payload so smartDefaults / firstPreviewRow can
        // read native values (numbers, booleans, dates) instead of stringly-
        // typed DOM cell text.
        state.lastPreview = { rows, columns: cols.slice(), types: typeMap };

        // Header row: name (click-to-sort), up/down arrow buttons (active one
        // highlighted), three-dot menu, hover-only × delete-column. Below the
        // name, a clickable type pill so the user can see and change the
        // detected datatype without diving into the menu.
        const headCells = cols.map(c => {
            const st = (window.TWP && window.TWP.getSortState) ? window.TWP.getSortState(c) : null;
            const ascActive  = st && st.dir === 'asc'  ? ' is-active' : '';
            const descActive = st && st.dir === 'desc' ? ' is-active' : '';
            const sortCls = st ? ' twp-col-sort-active' : '';
            const t = typeMap[c] || 'text';
            const tm = TYPE_META[t] || TYPE_META.text;
            return `<th data-col="${esc(c)}" data-coltype="${esc(t)}" class="twp-col-head${sortCls}">`
                + `<div class="twp-col-h-row">`
                +   `<span class="twp-col-name" title="Click to sort">${esc(c)}</span>`
                +   `<span class="twp-col-tools">`
                +     `<button type="button" class="twp-col-tool twp-col-sort-btn${ascActive}"  data-act="col-sort-asc"  title="Sort ascending"><i class="bi bi-caret-up-fill"></i></button>`
                +     `<button type="button" class="twp-col-tool twp-col-sort-btn${descActive}" data-act="col-sort-desc" title="Sort descending"><i class="bi bi-caret-down-fill"></i></button>`
                +     `<button type="button" class="twp-col-tool twp-col-chevron" data-act="col-menu" title="Column actions"><i class="bi bi-three-dots-vertical"></i></button>`
                +     `<button type="button" class="twp-col-tool twp-col-del" data-act="col-delete" title="Remove column"><i class="bi bi-x-lg"></i></button>`
                +   `</span>`
                + `</div>`
                + `<button type="button" class="twp-col-type-pill" data-act="col-type" title="Click to change type"><i class="bi ${tm.icon}"></i><span>${tm.label}</span></button>`
                + `</th>`;
        }).join('');
        // Leading row-action column with no header label (just a separator).
        const thead = `<tr><th class="twp-row-actions-h" aria-hidden="true"></th>${headCells}</tr>`;

        // Body rows: leading row-number + delete-row × button. Stash the row
        // payload as JSON so the click handler can build a precise filter
        // expression keyed on the row's most-likely-unique column.
        const tbody = rows.map((r, i) => {
            const sig = esc(JSON.stringify(r));
            const cells = cols.map(c => `<td>${esc(r[c])}</td>`).join('');
            return `<tr data-rowidx="${i}" data-rowsig="${sig}">`
                + `<td class="twp-row-actions">`
                +   `<span class="twp-row-num">${i + 1}</span>`
                +   `<button type="button" class="twp-row-del" data-act="row-delete" title="Remove this row">\u00d7</button>`
                + `</td>${cells}</tr>`;
        }).join('');

        tbl.querySelector('thead').innerHTML = thead;
        tbl.querySelector('tbody').innerHTML = tbody;
        setView('preview');
        document.dispatchEvent(new CustomEvent('twp:preview-rendered', { detail: { columns: cols, rowCount: rows.length, types: typeMap } }));
    }

    function clearPreview() {
        $('twpPreviewEmpty').style.display = '';
        $('twpPreviewWrap').style.display = 'none';
        $('twpRowCountPill').style.display = 'none';
        hidePreviewError();
        state.lastPreview = null;
        const tbl = $('twpPreviewTable');
        if (tbl) { delete tbl.dataset.colTypes; }
    }

    // Inline error overlay on the preview pane — keeps the last good rows
    // visible underneath so the user can still see context while the issue
    // is read.
    function showPreviewError(message) {
        const host = document.getElementById('twpViewPreview');
        if (!host) return;
        host.style.position = host.style.position || 'relative';
        let overlay = document.getElementById('twpPreviewError');
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.id = 'twpPreviewError';
            overlay.className = 'twp-preview-error';
            host.appendChild(overlay);
        }
        overlay.innerHTML = `<div class="twp-preview-error-card">
            <i class="bi bi-exclamation-octagon-fill"></i>
            <div class="twp-preview-error-text">${esc(message || 'Preview failed.')}</div>
            <button type="button" class="btn btn-sm btn-outline-light" id="twpPreviewErrorRetry"><i class="bi bi-arrow-clockwise me-1"></i>Retry</button>
        </div>`;
        overlay.style.display = '';
        const btn = document.getElementById('twpPreviewErrorRetry');
        if (btn) btn.addEventListener('click', () => { hidePreviewError(); runPreview(); });
    }
    function hidePreviewError() {
        const overlay = document.getElementById('twpPreviewError');
        if (overlay) overlay.style.display = 'none';
    }

    async function runValidate() {
        const q = activeQuery(); if (!q) return;
        const toml = buildToml(q.steps);
        setStatus('Validating…');
        try {
            const r = await fetch('/api/datasources/' + encodeURIComponent(q.guid) + '/transform/validate', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken() },
                body: JSON.stringify({ toml })
            });
            const data = await r.json().catch(() => ({}));
            if (!r.ok) throw new Error(data.error || data.message || 'Validation failed');
            // Server returns { success, syntaxErrors:[{code,message,reference}], stepCount, message, aiSuggestion }
            const ok = (data.success !== false) && (data.ok !== false);
            const errs = Array.isArray(data.syntaxErrors) ? data.syntaxErrors : [];
            const detail = errs.length
                ? errs.map(e => e.message || e.code).filter(Boolean).join('; ')
                : (data.error || data.message || '');
            if (ok) {
                setStatus('Valid.', 'success');
                updateStatusBar({ validity: { text: 'Valid', kind: 'success' } });
            } else {
                setStatus('Validation failed' + (detail ? ': ' + detail : '.'), 'error');
                updateStatusBar({ validity: { text: 'Invalid', kind: 'error' } });
                // Try to highlight the first column referenced by an error.
                const colHit = errs.map(e => extractErrorColumn(e.message || ''))
                                   .find(Boolean);
                if (colHit) highlightErrorColumn(colHit);
            }
        } catch (e) {
            setStatus(e.message || 'Validation failed.', 'error');
            updateStatusBar({ validity: { text: 'Invalid', kind: 'error' } });
        }
    }

    async function runPublish() {
        const q = activeQuery(); if (!q) return;
        const toml = buildToml(q.steps);
        setStatus('Publishing…');
        try {
            const r = await fetch('/api/datasources/' + encodeURIComponent(q.guid) + '/transform/publish', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken() },
                body: JSON.stringify({ toml })
            });
            if (!r.ok) {
                const data = await r.json().catch(() => ({}));
                throw new Error(data.error || ('Publish failed (' + r.status + ')'));
            }
            setStatus('Published. Dashboard tiles will pick up the new pipeline on next refresh.', 'success');
        } catch (e) { setStatus(e.message || 'Publish failed.', 'error'); }
    }

    // ── AI Assistant (best-effort: lightweight local intent parser) ────
    function wireAi() {
        $('twpAiSendBtn').addEventListener('click', sendAi);
        $('twpAiInput').addEventListener('keydown', e => {
            if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); sendAi(); }
        });
    }

    function appendAiMsg(text, who) {
        const chat = $('twpAiChat');
        const div = document.createElement('div');
        div.className = 'twp-ai-msg twp-ai-msg-' + (who === 'user' ? 'user' : 'bot');
        div.textContent = text;
        chat.appendChild(div);
        chat.scrollTop = chat.scrollHeight;
    }

    async function sendAi() {
        const input = $('twpAiInput');
        const text = (input.value || '').trim();
        if (!text) return;
        appendAiMsg(text, 'user');
        input.value = '';

        // Local audit/review intent — produces a pipeline health report
        // without a server roundtrip. Recognised phrases cover the
        // "Audit transform health" capability card.
        if (/\b(audit|review|check|inspect|validate)\b.*\b(pipeline|steps?|transforms?|query|queries)\b/i.test(text) ||
            /^(audit|review)\s+my\s+/i.test(text)) {
            appendAiMsg(auditPipelineReport(activeQuery()), 'bot');
            return;
        }

        // First try a server endpoint (if the app exposes one); otherwise fall
        // back to a small local intent parser that maps obvious phrases to
        // ribbon ops. This keeps the UI useful even without a backend.
        const q = activeQuery();
        let suggested = null;
        try {
            const r = await fetch('/api/transforms/ai-suggest', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken() },
                body: JSON.stringify({ datasourceGuid: q?.guid, prompt: text })
            });
            if (r.ok) suggested = await r.json();
        } catch { /* network/no-endpoint — fall through */ }

        // Fall back to the local intent parser when the server is missing,
        // returns nothing, or returns an empty steps array. Without the
        // empty-array check, a 200 OK with `{ steps: [] }` would suppress the
        // local parser and the user would always see "didn't get enough
        // detail" — even for unambiguous prompts like "remove duplicates by
        // userId" that the local parser handles cleanly.
        if (!suggested || !Array.isArray(suggested.steps) || suggested.steps.length === 0) {
            suggested = localAiFallback(text, q);
        }

        // Required-param keys per op. If the AI (or local fallback) returned a
        // step but didn't fill the essentials, we DROP it instead of merging
        // with defaultParamsFor — otherwise an empty {op:'derived_field'}
        // would silently turn into the placeholder example expression.
        const REQUIRED = {
            filter:           ['expression'],
            select_rows:      ['expression'],
            remove_columns:   ['columns'],
            remove_duplicates:['keys'],
            sort_asc:         ['column'],
            sort_desc:        ['column'],
            rename_column:    ['from','to'],
            duplicate_column: ['source'],
            replace_values:   ['column','from'],
            fill_down:        ['column'],
            fill_up:          ['column'],
            change_type:      ['column','type'],
            split_column:     ['column'],
            merge_columns:    ['columns','target_field'],
            reorder_columns:  ['order'],
            add_column:       ['target_field','value'],
            derived_field:    ['target_field','expression'],
            conditional:      ['target_field','expression'],
            index_column:     ['target_field'],
            group_by:         ['keys'],
            pivot:            ['column','values_column'],
            unpivot:          ['columns'],
            merge:            ['right','left_key'],
            append:           ['source'],
            nested_join:      ['right','left_key','target_field'],
            text_replace:     ['column','from'],
            text_extract:     ['column','target_field'],
            text_length:      ['column','target_field'],
            text_contains:    ['column','value','target_field']
        };
        const hasAll = (op, params) => {
            const need = REQUIRED[op];
            if (!need) return true; // single-column text_*/num_*/date_*/time_* schemas validate themselves
            return need.every(k => {
                const v = params?.[k];
                if (Array.isArray(v)) return v.length > 0;
                return v != null && String(v).trim() !== '';
            });
        };

        // Normalize: keep only steps whose op is known AND that arrived with
        // their required params filled. Force enabled=true.
        const normalized = (suggested.steps || [])
            .filter(s => s && typeof s.op === 'string' && schemaFor(s.op))
            .map(s => ({ op: s.op, enabled: true, params: Object.assign({}, s.params || {}) }))
            .filter(s => hasAll(s.op, s.params));

        if (normalized.length === 0) {
            appendAiMsg("I understood your intent but didn't get enough detail to build a step. Try being explicit, e.g. \"merge Title and Body into FullText with separator ' - '\" or \"add column Margin = DIVIDE([Profit],[Revenue])\".", 'bot');
            return;
        }
        if (q) {
            normalized.forEach(s => q.steps.push(s));
            onStepsChanged();
            appendAiMsg(`Added ${normalized.length} step(s): ${normalized.map(s => labelFor(s.op, s.params)).join(', ')}.`, 'bot');
        }
    }

    function localAiFallback(text, q) {
        const t = text.toLowerCase();
        const steps = [];
        // Match against the original-cased `text` so column names like
        // `userId` keep their casing — the engine is case-sensitive.
        const dedupe = text.match(/remove\s+duplicates(?:\s+(?:by|on)\s+([A-Za-z0-9_, ]+?))?(?:[.;]|$)/i);
        if (dedupe) steps.push({ op: 'remove_duplicates', enabled: true, params: { keys: (dedupe[1] || 'Id').split(/[, ]+/).map(s => s.trim()).filter(Boolean) } });
        const addCol = text.match(/add column\s+([A-Za-z0-9_]+)\s*=\s*(.+?)(?:[.;]|$)/i);
        if (addCol) steps.push({ op: 'derived_field', enabled: true, params: { target_field: addCol[1], expression: addCol[2].trim() } });

        // "merge Title and Body into FullText [with separator ' - ']" — Power
        // Query "Merge Columns". Captures any number of column names joined
        // by 'and'/','. The 'into <name>' part is optional; defaults to a
        // generated name so the step is still valid.
        const mergeCols = text.match(/merge(?:\s+columns?)?\s+([A-Za-z0-9_\[\] ,]+?)(?:\s+into\s+([A-Za-z0-9_]+))?(?:\s+with\s+separator\s+['"]?([^'"]+?)['"]?)?(?:[.;]|$)/i);
        if (mergeCols && /\band\b|,/.test(mergeCols[1])) {
            const cols = mergeCols[1].split(/\s*(?:and|,)\s*/i).map(s => s.replace(/^\[|\]$/g, '').trim()).filter(Boolean);
            if (cols.length >= 2) {
                steps.push({ op: 'merge_columns', enabled: true, params: {
                    columns: cols,
                    separator: mergeCols[3] || ' ',
                    target_field: mergeCols[2] || (cols.join('_'))
                }});
            }
        }
        // "split FullName by ' ' into First, Last" — Power Query "Split Column".
        const splitCol = text.match(/split\s+(?:column\s+)?\[?([A-Za-z0-9_]+)\]?(?:\s+by\s+['"]?([^'"]+?)['"]?)?(?:\s+into\s+([A-Za-z0-9_, ]+))?(?:[.;]|$)/i);
        if (splitCol) {
            const into = (splitCol[3] || '').split(/\s*,\s*/).map(s => s.trim()).filter(Boolean);
            steps.push({ op: 'split_column', enabled: true, params: {
                column: splitCol[1],
                delimiter: splitCol[2] || ' ',
                into: into.length ? into : [splitCol[1] + '_1', splitCol[1] + '_2']
            }});
        }

        const merge = text.match(/merge\s+with\s+([A-Za-z0-9_]+)\s+on\s+([A-Za-z0-9_]+)/i);
        if (merge) steps.push({ op: 'merge', enabled: true, params: { right: merge[1], left_key: merge[2], right_key: merge[2], how: 'inner' } });
        const filter = text.match(/filter\s+(?:where\s+)?(.+?)(?:[.;]|$)/i);
        if (filter && !addCol) steps.push({ op: 'filter', enabled: true, params: { expression: filter[1].trim() } });
        return { steps };
    }

    // Pipeline audit — purely client-side. Walks the active query's steps,
    // counts disabled / no-op / DAX-error rules, and returns a multiline
    // text report. Designed for the AI "Audit transform health" prompt.
    function auditPipelineReport(q) {
        if (!q) return 'No query selected — open a query in the left pane and try again.';
        const steps = q.steps || [];
        if (steps.length === 0) return `"${q.name}" has no steps yet. Click any ribbon button or ask me to add one.`;
        const lines = [];
        let errors = 0, warnings = 0, disabled = 0;
        const cols = (Array.isArray(q.schema) ? q.schema : null) ||
            Array.from(document.querySelectorAll('#twpPreviewTable thead th[data-col]')).map(th => th.dataset.col);
        steps.forEach((s, i) => {
            const tag = `${i + 1}. ${labelFor(s.op, s.params)}`;
            if (!s.enabled) { disabled++; lines.push(`⊙ ${tag}  (disabled)`); return; }
            const exprFields = (schemaFor(s.op) || []).filter(f => f.type === 'expression').map(f => f.key);
            let issue = null;
            for (const k of exprFields) {
                const v = s.params?.[k];
                if (v == null || String(v).trim() === '') { issue = `missing "${k}"`; break; }
                if (typeof window.TWP_validateExpression === 'function') {
                    const res = window.TWP_validateExpression(String(v), cols);
                    if (res && res.ok === false) {
                        const e = (res.issues || []).find(x => x.level === 'error');
                        if (e) { issue = e.message; break; }
                    }
                }
            }
            if (issue) { errors++; lines.push(`✖ ${tag}  — ${issue}`); }
            else lines.push(`✓ ${tag}`);
        });
        const summary = `Audit of "${q.name}": ${steps.length} step(s), ${errors} error(s), ${disabled} disabled.`;
        return [summary, ''].concat(lines).join('\n');
    }
})();
