// Transform Workbench — Phase 20 end-user enhancements.
// Adds Power-Query-style column header menus, IntelliSense (column + DAX
// function autocomplete), a DAX expression editor modal, and an AI agent
// capabilities review pane. Wires into the base module via window.TWP and
// the `twp:preview-rendered` / `twp:add-step` CustomEvents.
(function () {
    'use strict';

    const esc = (v) => String(v ?? '').replace(/[&<>"']/g, s => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[s]));

    // DAX-style function catalog surfaced in the IntelliSense popup and the
    // DAX editor sidebar. Mirrors the expressions accepted by the
    // `derived_field` / `conditional` / `filter` handlers.
    const DAX_FUNCS = [
        { name: 'IF',           sig: 'IF(condition, then, else)',                category: 'Logical' },
        { name: 'SWITCH',       sig: 'SWITCH(value, match1, result1, …, default)', category: 'Logical' },
        { name: 'AND',          sig: 'AND(a, b)',                                category: 'Logical' },
        { name: 'OR',           sig: 'OR(a, b)',                                 category: 'Logical' },
        { name: 'NOT',          sig: 'NOT(a)',                                   category: 'Logical' },
        { name: 'IFERROR',      sig: 'IFERROR(value, value_if_error)',           category: 'Logical' },
        { name: 'ISBLANK',      sig: 'ISBLANK(value)',                           category: 'Logical' },
        { name: 'BLANK',        sig: 'BLANK()',                                  category: 'Logical' },

        { name: 'SUM',          sig: 'SUM([Column])',                            category: 'Aggregate' },
        { name: 'AVERAGE',      sig: 'AVERAGE([Column])',                        category: 'Aggregate' },
        { name: 'MIN',          sig: 'MIN([Column])',                            category: 'Aggregate' },
        { name: 'MAX',          sig: 'MAX([Column])',                            category: 'Aggregate' },
        { name: 'COUNT',        sig: 'COUNT([Column])',                          category: 'Aggregate' },
        { name: 'COUNTROWS',    sig: 'COUNTROWS(Table)',                         category: 'Aggregate' },
        { name: 'DISTINCTCOUNT', sig: 'DISTINCTCOUNT([Column])',                 category: 'Aggregate' },
        { name: 'SUMX',         sig: 'SUMX(Table, expression)',                  category: 'Iterator' },
        { name: 'AVERAGEX',     sig: 'AVERAGEX(Table, expression)',              category: 'Iterator' },
        { name: 'CALCULATE',    sig: 'CALCULATE(expression, filter1, …)',        category: 'Filter' },
        { name: 'FILTER',       sig: 'FILTER(Table, condition)',                 category: 'Filter' },
        { name: 'ALL',          sig: 'ALL(Table | [Column])',                    category: 'Filter' },
        { name: 'VALUES',       sig: 'VALUES([Column])',                         category: 'Filter' },
        { name: 'RELATED',      sig: 'RELATED([Column])',                        category: 'Relationship' },
        { name: 'LOOKUPVALUE',  sig: 'LOOKUPVALUE(result_col, search_col, value)', category: 'Relationship' },

        { name: 'DATEDIFF',     sig: 'DATEDIFF(start, end, interval)',           category: 'Date' },
        { name: 'YEAR',         sig: 'YEAR([Date])',                             category: 'Date' },
        { name: 'MONTH',        sig: 'MONTH([Date])',                            category: 'Date' },
        { name: 'DAY',          sig: 'DAY([Date])',                              category: 'Date' },
        { name: 'TODAY',        sig: 'TODAY()',                                  category: 'Date' },
        { name: 'NOW',          sig: 'NOW()',                                    category: 'Date' },
        { name: 'FORMAT',       sig: 'FORMAT(value, format_string)',             category: 'Date' },

        { name: 'LEFT',         sig: 'LEFT(text, count)',                        category: 'Text' },
        { name: 'RIGHT',        sig: 'RIGHT(text, count)',                       category: 'Text' },
        { name: 'MID',          sig: 'MID(text, start, count)',                  category: 'Text' },
        { name: 'LEN',          sig: 'LEN(text)',                                category: 'Text' },
        { name: 'UPPER',        sig: 'UPPER(text)',                              category: 'Text' },
        { name: 'LOWER',        sig: 'LOWER(text)',                              category: 'Text' },
        { name: 'TRIM',         sig: 'TRIM(text)',                               category: 'Text' },
        { name: 'CONCATENATE',  sig: 'CONCATENATE(text1, text2)',                category: 'Text' },
        { name: 'SUBSTITUTE',   sig: 'SUBSTITUTE(text, old, new)',               category: 'Text' },

        { name: 'DIVIDE',       sig: 'DIVIDE(num, denom, alt)',                  category: 'Math' },
        { name: 'ROUND',        sig: 'ROUND(value, digits)',                     category: 'Math' },
        { name: 'ABS',          sig: 'ABS(value)',                               category: 'Math' }
    ];

    // Datatype options shown in the column header "Change type" submenu.
    // The `op:'change_type'` handler uses the `type` slug (matches
    // Services/Transforms type coercion).
    const TYPE_CHOICES = [
        { type: 'text',     label: 'Text',          icon: 'bi-type'        },
        { type: 'integer',  label: 'Whole Number',  icon: 'bi-123'         },
        { type: 'decimal',  label: 'Decimal',       icon: 'bi-percent'     },
        { type: 'boolean',  label: 'Boolean',       icon: 'bi-toggle-on'   },
        { type: 'date',     label: 'Date',          icon: 'bi-calendar'    },
        { type: 'datetime', label: 'Date/Time',     icon: 'bi-clock'       },
        { type: 'time',     label: 'Time',          icon: 'bi-clock-history' }
    ];

    // What the AI assistant can do — surfaced as a review card in the right
    // pane so end users discover the capabilities without trial-and-error.
    const AI_CAPABILITIES = [
        { icon: 'bi-magic',          title: 'Suggest next step',          hint: 'remove duplicates by Id' },
        { icon: 'bi-calculator',     title: 'Author DAX expressions',     hint: 'add column Margin = DIVIDE([Profit],[Revenue])' },
        { icon: 'bi-shuffle',        title: 'Fix datatype mismatches',    hint: 'change type of Amount to decimal' },
        { icon: 'bi-funnel',         title: 'Filter & shape rows',        hint: 'filter where [Status] = "Open"' },
        { icon: 'bi-bezier2',        title: 'Suggest joins',              hint: 'merge with Customers on CustomerId' },
        { icon: 'bi-shield-check',   title: 'Audit transform health',     hint: 'review my pipeline for issues' }
    ];

    let columnMenuEl = null;
    let intelliPopupEl = null;
    let daxModalEl = null;

    document.addEventListener('DOMContentLoaded', () => {
        injectAiCapabilitiesPanel();
        document.addEventListener('twp:preview-rendered', onPreviewRendered);
        document.addEventListener('click', closeFloatingsOnOutsideClick, true);
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') { closeColumnMenu(); closeIntelli(); closeDaxModal(); }
        });
        // Late-arriving step-editor inputs: catch focus on any text field
        // marked with data-twp-intelli (or any textarea inside the step
        // editor) and attach IntelliSense.
        document.addEventListener('focusin', (e) => {
            const t = e.target;
            if (!t || !(t.matches && t.matches('textarea, input[type=text]'))) return;
            if (t.closest('#twpStepEditor') || t.dataset.twpIntelli !== undefined || t.id === 'twpTomlEditor' || t.id === 'twpAiInput' || t.id === 'twpStepAiPrompt') {
                attachIntelli(t);
            }
            // Inline DAX validity check on expression fields only.
            if (t.classList && t.classList.contains('twp-expr-input')) {
                attachExprValidator(t);
            }
        });
    });

    // ── Column header decoration ───────────────────────────────────────
    function onPreviewRendered() {
        const tbl = document.getElementById('twpPreviewTable');
        if (!tbl) return;
        tbl.querySelectorAll('thead th[data-col]').forEach(th => {
            // Avoid double-binding if the same node is reused across renders.
            if (th.dataset.twpBound === '1') return;
            th.dataset.twpBound = '1';
            const col = th.dataset.col;
            const nameEl = th.querySelector('.twp-col-name');
            const sortEl = th.querySelector('.twp-col-sort');
            const chevEl = th.querySelector('.twp-col-chevron');
            const sortClick = (e) => {
                e.stopPropagation();
                if (window.TWP && typeof window.TWP.toggleSort === 'function') window.TWP.toggleSort(col);
            };
            nameEl && nameEl.addEventListener('click', sortClick);
            sortEl && sortEl.addEventListener('click', sortClick);
            chevEl && chevEl.addEventListener('click', (e) => {
                e.stopPropagation();
                openColumnMenu(th);
            });

            // Column drag-reorder: drag a header onto another header to emit
            // a `reorder_columns` step. Drag-vs-click is resolved by motion.
            th.draggable = true;
            th.addEventListener('dragstart', (e) => {
                if (!e.dataTransfer) return;
                e.dataTransfer.setData('text/x-twp-col', col);
                e.dataTransfer.effectAllowed = 'move';
                th.classList.add('twp-col-dragging');
            });
            th.addEventListener('dragend', () => th.classList.remove('twp-col-dragging'));
            th.addEventListener('dragover', (e) => {
                if (e.dataTransfer && Array.from(e.dataTransfer.types || []).includes('text/x-twp-col')) {
                    e.preventDefault();
                    th.classList.add('twp-col-drop');
                }
            });
            th.addEventListener('dragleave', () => th.classList.remove('twp-col-drop'));
            th.addEventListener('drop', (e) => {
                th.classList.remove('twp-col-drop');
                if (!e.dataTransfer) return;
                const from = e.dataTransfer.getData('text/x-twp-col');
                if (!from || from === col) return;
                e.preventDefault();
                const heads = Array.from(document.querySelectorAll('#twpPreviewTable thead th[data-col]')).map(t => t.dataset.col);
                const fIdx = heads.indexOf(from);
                if (fIdx > -1) heads.splice(fIdx, 1);
                const tIdx = heads.indexOf(col);
                heads.splice(tIdx < 0 ? heads.length : tIdx, 0, from);
                dispatchStep('reorder_columns', { order: heads });
            });
        });
    }

    function openColumnMenu(th) {
        closeColumnMenu();
        const col = th.dataset.col;
        const rect = th.getBoundingClientRect();
        const sortState = (window.TWP && window.TWP.getSortState) ? window.TWP.getSortState(col) : null;
        const sortAscActive = sortState && sortState.dir === 'asc';
        const sortDescActive = sortState && sortState.dir === 'desc';
        const checked = '<i class="bi bi-check2 ms-auto"></i>';
        const menu = document.createElement('div');
        menu.className = 'twp-col-menu';
        menu.style.left = Math.round(rect.left) + 'px';
        menu.style.top = Math.round(rect.bottom + 2) + 'px';
        menu.innerHTML = `
            <div class="twp-col-menu-head">
                <i class="bi bi-bookmark-star me-1"></i>${esc(col)}
            </div>

            <div class="twp-col-menu-section">
                <div class="twp-col-menu-section-title">Sort</div>
                <button type="button" class="twp-col-menu-item ${sortAscActive ? 'is-active' : ''}" data-act="sort_asc"><i class="bi bi-sort-down-alt me-2"></i>Sort ascending${sortAscActive ? checked : ''}</button>
                <button type="button" class="twp-col-menu-item ${sortDescActive ? 'is-active' : ''}" data-act="sort_desc"><i class="bi bi-sort-up me-2"></i>Sort descending${sortDescActive ? checked : ''}</button>
                ${sortState ? `<button type="button" class="twp-col-menu-item" data-act="clear_sort"><i class="bi bi-x-circle me-2"></i>Clear sort</button>` : ''}
            </div>

            <div class="twp-col-menu-section">
                <div class="twp-col-menu-section-title">Filter & clean</div>
                <button type="button" class="twp-col-menu-item" data-act="filter_equals"><i class="bi bi-funnel me-2"></i>Filter rows…</button>
                <button type="button" class="twp-col-menu-item" data-act="remove_duplicates"><i class="bi bi-files me-2"></i>Remove duplicates by this column</button>
                <button type="button" class="twp-col-menu-item" data-act="remove_blanks"><i class="bi bi-eraser me-2"></i>Remove blanks</button>
                <button type="button" class="twp-col-menu-item" data-act="trim"><i class="bi bi-scissors me-2"></i>Trim text</button>
                <button type="button" class="twp-col-menu-item" data-act="clean"><i class="bi bi-magic me-2"></i>Clean (remove control chars)</button>
                <button type="button" class="twp-col-menu-item" data-act="replace"><i class="bi bi-arrow-left-right me-2"></i>Replace values…</button>
            </div>

            <div class="twp-col-menu-section">
                <div class="twp-col-menu-section-title">Change type</div>
                <div class="twp-col-menu-types">
                    ${TYPE_CHOICES.map(t => `<button type="button" class="twp-col-menu-type" data-type="${t.type}"><i class="bi ${t.icon} me-1"></i>${t.label}</button>`).join('')}
                </div>
            </div>

            <div class="twp-col-menu-section">
                <div class="twp-col-menu-section-title">Transform</div>
                <button type="button" class="twp-col-menu-item" data-act="rename"><i class="bi bi-input-cursor-text me-2"></i>Rename…</button>
                <button type="button" class="twp-col-menu-item" data-act="duplicate"><i class="bi bi-copy me-2"></i>Duplicate column</button>
                <button type="button" class="twp-col-menu-item" data-act="fill_down"><i class="bi bi-arrow-down-short me-2"></i>Fill down</button>
                <button type="button" class="twp-col-menu-item" data-act="fill_up"><i class="bi bi-arrow-up-short me-2"></i>Fill up</button>
                <button type="button" class="twp-col-menu-item" data-act="upper"><i class="bi bi-type me-2"></i>UPPERCASE</button>
                <button type="button" class="twp-col-menu-item" data-act="lower"><i class="bi bi-type me-2"></i>lowercase</button>
                <button type="button" class="twp-col-menu-item" data-act="proper"><i class="bi bi-type me-2"></i>Capitalize Each Word</button>
                <button type="button" class="twp-col-menu-item" data-act="length"><i class="bi bi-rulers me-2"></i>Text length → new column</button>
                <button type="button" class="twp-col-menu-item" data-act="round"><i class="bi bi-circle-half me-2"></i>Round (numeric)</button>
                <button type="button" class="twp-col-menu-item" data-act="abs"><i class="bi bi-plus-slash-minus me-2"></i>Absolute value (numeric)</button>
            </div>

            <div class="twp-col-menu-section">
                <div class="twp-col-menu-section-title">Group / pivot</div>
                <button type="button" class="twp-col-menu-item" data-act="group_by"><i class="bi bi-collection me-2"></i>Group by this column</button>
                <button type="button" class="twp-col-menu-item" data-act="unpivot_other"><i class="bi bi-arrow-up-down me-2"></i>Unpivot other columns</button>
            </div>

            <div class="twp-col-menu-section">
                <button type="button" class="twp-col-menu-item twp-col-menu-primary" data-act="dax">
                    <i class="bi bi-calculator me-2"></i>New DAX column…
                </button>
                <button type="button" class="twp-col-menu-item twp-col-menu-danger" data-act="remove"><i class="bi bi-trash me-2"></i>Remove column</button>
            </div>
        `;
        document.body.appendChild(menu);
        columnMenuEl = menu;

        menu.addEventListener('click', (e) => {
            const typeBtn = e.target.closest('[data-type]');
            const itemBtn = e.target.closest('[data-act]');
            if (typeBtn) {
                dispatchStep('change_type', { column: col, type: typeBtn.dataset.type });
                closeColumnMenu();
                return;
            }
            if (!itemBtn) return;
            handleColumnMenuAction(itemBtn.dataset.act, col);
            closeColumnMenu();
        });

        // Clamp to viewport
        requestAnimationFrame(() => {
            const r = menu.getBoundingClientRect();
            if (r.right > window.innerWidth - 8) {
                menu.style.left = Math.max(8, window.innerWidth - r.width - 8) + 'px';
            }
            if (r.bottom > window.innerHeight - 8) {
                menu.style.top = Math.max(8, rect.top - r.height - 4) + 'px';
            }
        });
    }

    function closeColumnMenu() {
        if (columnMenuEl) { columnMenuEl.remove(); columnMenuEl = null; }
    }

    function handleColumnMenuAction(act, col) {
        switch (act) {
            case 'rename': {
                const to = prompt(`Rename "${col}" to:`, col);
                if (to && to !== col) dispatchStep('rename_column', { from: col, to });
                break;
            }
            case 'duplicate':
                dispatchStep('duplicate_column', { source: col, target_field: col + ' (copy)' });
                break;
            case 'remove':
                dispatchStep('remove_columns', { columns: [col] });
                break;
            case 'replace': {
                const from = prompt(`Replace what value in "${col}"?`, '');
                if (from === null) return;
                const to = prompt(`Replace "${from}" with:`, '');
                if (to === null) return;
                dispatchStep('replace_values', { column: col, from, to });
                break;
            }
            case 'fill_down':  dispatchStep('fill_down', { column: col }); break;
            case 'fill_up':    dispatchStep('fill_up',   { column: col }); break;
            case 'trim':       dispatchStep('text_trim', { column: col }); break;
            case 'clean':      dispatchStep('text_clean', { column: col }); break;
            case 'upper':      dispatchStep('text_upper', { column: col }); break;
            case 'lower':      dispatchStep('text_lower', { column: col }); break;
            case 'proper':     dispatchStep('text_proper', { column: col }); break;
            case 'length':     dispatchStep('text_length', { column: col, target_field: col + 'Length' }); break;
            case 'round':      dispatchStep('num_round', { column: col, digits: 2 }); break;
            case 'abs':        dispatchStep('num_abs',   { column: col }); break;
            case 'sort_asc':
                if (window.TWP && window.TWP.toggleSort) {
                    const st = window.TWP.getSortState && window.TWP.getSortState(col);
                    if (!st || st.dir !== 'asc') window.TWP.toggleSort(col);
                    if (st && st.dir === 'desc') window.TWP.toggleSort(col); // desc → none, then need asc
                    const after = window.TWP.getSortState && window.TWP.getSortState(col);
                    if (!after) window.TWP.toggleSort(col);
                } else {
                    dispatchStep('sort_asc', { column: col, direction: 'asc' });
                }
                break;
            case 'sort_desc':
                if (window.TWP && window.TWP.toggleSort) {
                    const st = window.TWP.getSortState && window.TWP.getSortState(col);
                    if (!st) { window.TWP.toggleSort(col); window.TWP.toggleSort(col); }
                    else if (st.dir === 'asc') { window.TWP.toggleSort(col); }
                } else {
                    dispatchStep('sort_desc', { column: col, direction: 'desc' });
                }
                break;
            case 'clear_sort': {
                const st = window.TWP && window.TWP.getSortState && window.TWP.getSortState(col);
                if (st && window.TWP.state) {
                    const q = window.TWP.activeQuery && window.TWP.activeQuery();
                    if (q) {
                        q.steps.splice(st.stepIdx, 1);
                        if (window.TWP.refresh) window.TWP.refresh();
                    }
                }
                break;
            }
            case 'remove_duplicates':
                dispatchStep('remove_duplicates', { keys: [col] });
                break;
            case 'remove_blanks':
                dispatchStep('filter', { expression: `[${col}] <> "" AND NOT ISBLANK([${col}])` });
                break;
            case 'filter_equals': {
                const v = prompt(`Keep rows where [${col}] equals:`, '');
                if (v === null) return;
                dispatchStep('filter', { expression: `[${col}] = "${String(v).replace(/"/g, '\\"')}"` });
                break;
            }
            case 'unpivot_other': {
                const heads = document.querySelectorAll('#twpPreviewTable thead th[data-col]');
                const others = Array.from(heads).map(t => t.dataset.col).filter(c => c !== col);
                if (others.length === 0) return;
                dispatchStep('unpivot', { columns: others, attribute_name: 'Attribute', value_name: 'Value' });
                break;
            }
            case 'group_by':   dispatchStep('group_by',  { keys: [col], aggregations: [{ name: 'Count', op: 'count' }] }); break;
            case 'dax':        openDaxModal({ column: col }); break;
        }
    }

    function dispatchStep(op, params) {
        document.dispatchEvent(new CustomEvent('twp:add-step', { detail: { op, params, enabled: true } }));
    }

    // ── IntelliSense popup ─────────────────────────────────────────────
    function attachIntelli(input) {
        if (!input || input.dataset.twpIntelliBound === '1') return;
        input.dataset.twpIntelliBound = '1';
        input.addEventListener('input', () => showIntelli(input));
        input.addEventListener('keydown', (e) => {
            if (!intelliPopupEl) return;
            const items = Array.from(intelliPopupEl.querySelectorAll('.twp-intelli-item'));
            if (items.length === 0) return;
            const active = intelliPopupEl.querySelector('.twp-intelli-item.active');
            let idx = items.indexOf(active);
            if (e.key === 'ArrowDown') { e.preventDefault(); idx = (idx + 1) % items.length; setActive(items, idx); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); idx = (idx - 1 + items.length) % items.length; setActive(items, idx); }
            else if (e.key === 'Tab' || e.key === 'Enter') {
                if (idx >= 0) { e.preventDefault(); applyIntelli(input, items[idx]); }
            } else if (e.key === 'Escape') { closeIntelli(); }
        });
        input.addEventListener('blur', () => setTimeout(closeIntelli, 120));
    }

    function setActive(items, idx) {
        items.forEach((el, i) => el.classList.toggle('active', i === idx));
    }

    function getCaretToken(input) {
        const v = input.value || '';
        const pos = input.selectionStart || v.length;
        const left = v.slice(0, pos);
        const m = left.match(/([A-Za-z_][A-Za-z0-9_]*|\[[^\]]*)$/);
        return m ? { token: m[0], start: pos - m[0].length, end: pos } : null;
    }

    function getColumns() {
        const q = window.TWP && window.TWP.activeQuery && window.TWP.activeQuery();
        const tbl = document.getElementById('twpPreviewTable');
        const heads = tbl ? Array.from(tbl.querySelectorAll('thead th[data-col]')).map(th => th.dataset.col) : [];
        if (heads.length > 0) return heads;
        if (q && Array.isArray(q.schema)) return q.schema;
        return [];
    }

    function showIntelli(input) {
        const tok = getCaretToken(input);
        if (!tok || tok.token.length < 1) { closeIntelli(); return; }
        const isCol = tok.token.startsWith('[');
        const needle = (isCol ? tok.token.slice(1) : tok.token).toLowerCase();
        const cols = getColumns().filter(c => c.toLowerCase().includes(needle))
            .slice(0, 12)
            .map(c => ({ kind: 'col', label: '[' + c + ']', insert: '[' + c + ']', detail: 'column' }));
        const fns = isCol ? [] : DAX_FUNCS
            .filter(f => f.name.toLowerCase().startsWith(needle))
            .slice(0, 12)
            .map(f => ({ kind: 'fn', label: f.name, insert: f.name + '(', detail: f.sig, category: f.category }));
        const items = cols.concat(fns);
        if (items.length === 0) { closeIntelli(); return; }

        if (!intelliPopupEl) {
            intelliPopupEl = document.createElement('div');
            intelliPopupEl.className = 'twp-intelli-popup';
            document.body.appendChild(intelliPopupEl);
        }
        intelliPopupEl.innerHTML = items.map((it, i) => `
            <div class="twp-intelli-item ${i === 0 ? 'active' : ''}" data-insert="${esc(it.insert)}" data-start="${tok.start}" data-end="${tok.end}">
                <i class="bi ${it.kind === 'col' ? 'bi-columns-gap' : 'bi-braces'} me-2"></i>
                <span class="twp-intelli-label">${esc(it.label)}</span>
                <span class="twp-intelli-detail">${esc(it.detail || '')}</span>
            </div>`).join('');
        intelliPopupEl.querySelectorAll('.twp-intelli-item').forEach(el => {
            el.addEventListener('mousedown', (e) => { e.preventDefault(); applyIntelli(input, el); });
        });
        positionIntelliPopup(input);
    }

    function positionIntelliPopup(input) {
        const r = input.getBoundingClientRect();
        intelliPopupEl.style.left = Math.round(r.left) + 'px';
        intelliPopupEl.style.top = Math.round(r.bottom + 2) + 'px';
        intelliPopupEl.style.minWidth = Math.max(220, r.width) + 'px';
    }

    function applyIntelli(input, item) {
        if (!item) return;
        const insert = item.dataset.insert;
        const start = +item.dataset.start;
        const end = +item.dataset.end;
        const v = input.value || '';
        input.value = v.slice(0, start) + insert + v.slice(end);
        const caret = start + insert.length;
        input.setSelectionRange(caret, caret);
        input.dispatchEvent(new Event('input', { bubbles: true }));
        closeIntelli();
        input.focus();
    }

    function closeIntelli() {
        if (intelliPopupEl) { intelliPopupEl.remove(); intelliPopupEl = null; }
    }

    // ── DAX expression editor modal ───────────────────────────────────
    function openDaxModal(opts) {
        closeDaxModal();
        const initialName = (opts && opts.column) ? (opts.column + 'Calc') : 'NewMeasure';
        const initialExpr = (opts && opts.column) ? `[${opts.column}]` : '';
        const cats = Array.from(new Set(DAX_FUNCS.map(f => f.category)));
        const cols = getColumns();

        const modal = document.createElement('div');
        modal.className = 'twp-dax-modal';
        modal.innerHTML = `
            <div class="twp-dax-card">
                <div class="twp-dax-head">
                    <span><i class="bi bi-calculator me-2"></i>New DAX Column</span>
                    <button type="button" class="btn-close" data-act="close" aria-label="Close"></button>
                </div>
                <div class="twp-dax-body">
                    <div class="twp-dax-form">
                        <label class="form-label small fw-semibold">Column name</label>
                        <input type="text" class="form-control form-control-sm twp-dax-name" value="${esc(initialName)}" />
                        <label class="form-label small fw-semibold mt-3">Expression</label>
                        <textarea class="form-control twp-dax-expr" rows="9" data-twp-intelli spellcheck="false">${esc(initialExpr)}</textarea>
                        <div class="twp-dax-tip">
                            <i class="bi bi-info-circle me-1"></i>
                            Use <code>[Column]</code> for fields and DAX-style functions (IF, SWITCH, DATEDIFF, CALCULATE, …). Press <kbd>Tab</kbd> to accept a suggestion.
                        </div>
                    </div>
                    <div class="twp-dax-side">
                        <div class="twp-dax-side-tabs">
                            <button type="button" class="twp-dax-side-tab active" data-side="fn">Functions</button>
                            <button type="button" class="twp-dax-side-tab" data-side="col">Columns</button>
                        </div>
                        <div class="twp-dax-side-body" data-side-body="fn">
                            ${cats.map(cat => `
                                <div class="twp-dax-cat">${esc(cat)}</div>
                                ${DAX_FUNCS.filter(f => f.category === cat).map(f =>
                                    `<button type="button" class="twp-dax-fn" data-insert="${esc(f.name)}(" title="${esc(f.sig)}">${esc(f.name)}</button>`).join('')}
                            `).join('')}
                        </div>
                        <div class="twp-dax-side-body" data-side-body="col" style="display:none">
                            ${cols.length === 0
                                ? '<div class="text-muted small px-2 py-2">Run a preview to load columns.</div>'
                                : cols.map(c => `<button type="button" class="twp-dax-col" data-insert="[${esc(c)}]">${esc(c)}</button>`).join('')}
                        </div>
                    </div>
                </div>
                <div class="twp-dax-foot">
                    <button type="button" class="btn btn-sm btn-outline-secondary" data-act="cancel">Cancel</button>
                    <button type="button" class="btn btn-sm btn-primary" data-act="save"><i class="bi bi-check2 me-1"></i>Add column</button>
                </div>
            </div>
        `;
        document.body.appendChild(modal);
        daxModalEl = modal;

        const expr = modal.querySelector('.twp-dax-expr');
        attachIntelli(expr);
        setTimeout(() => expr.focus(), 30);

        modal.addEventListener('click', (e) => {
            const sideTab = e.target.closest('[data-side]');
            if (sideTab) {
                modal.querySelectorAll('[data-side]').forEach(b => b.classList.toggle('active', b === sideTab));
                modal.querySelectorAll('[data-side-body]').forEach(b => b.style.display = (b.dataset.sideBody === sideTab.dataset.side ? '' : 'none'));
                return;
            }
            const ins = e.target.closest('[data-insert]');
            if (ins) {
                insertAtCursor(expr, ins.dataset.insert);
                return;
            }
            const act = e.target.closest('[data-act]');
            if (!act) return;
            if (act.dataset.act === 'close' || act.dataset.act === 'cancel') closeDaxModal();
            else if (act.dataset.act === 'save') {
                const name = (modal.querySelector('.twp-dax-name').value || '').trim() || 'NewColumn';
                const expression = (expr.value || '').trim();
                if (!expression) { expr.focus(); return; }
                dispatchStep('derived_field', { target_field: name, expression });
                closeDaxModal();
            }
        });
    }

    function insertAtCursor(input, text) {
        const v = input.value || '';
        const s = input.selectionStart || v.length;
        const e = input.selectionEnd || v.length;
        input.value = v.slice(0, s) + text + v.slice(e);
        const caret = s + text.length;
        input.setSelectionRange(caret, caret);
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.focus();
    }

    function closeDaxModal() {
        if (daxModalEl) { daxModalEl.remove(); daxModalEl = null; }
    }

    // ── AI capabilities review pane ───────────────────────────────────
    function injectAiCapabilitiesPanel() {
        const aiPanel = document.querySelector('.twp-ai-panel');
        if (!aiPanel || aiPanel.querySelector('.twp-ai-caps')) return;
        const wrap = document.createElement('div');
        wrap.className = 'twp-ai-caps';
        // Persist the collapsed state so it doesn't fight the user every load.
        const collapsed = localStorage.getItem('twpAiCapsCollapsed') === '1';
        wrap.className = 'twp-ai-caps' + (collapsed ? ' collapsed' : '');
        wrap.innerHTML = `
            <button type="button" class="twp-ai-caps-head" aria-expanded="${collapsed ? 'false' : 'true'}">
                <span><i class="bi bi-stars me-1"></i>What the AI agent can do</span>
                <i class="bi bi-chevron-down twp-ai-caps-chev"></i>
            </button>
            <div class="twp-ai-caps-list">
                ${AI_CAPABILITIES.map(c => `
                    <button type="button" class="twp-ai-cap" data-prompt="${esc(c.hint)}" title="Click to load this prompt">
                        <span class="twp-ai-cap-icon"><i class="bi ${c.icon}"></i></span>
                        <span class="twp-ai-cap-text">
                            <span class="twp-ai-cap-title">${esc(c.title)}</span>
                            <span class="twp-ai-cap-hint">${esc(c.hint)}</span>
                        </span>
                        <i class="bi bi-arrow-right-short twp-ai-cap-go"></i>
                    </button>
                `).join('')}
            </div>
        `;
        // Insert just before the chat so it's discoverable but not blocking.
        const chat = aiPanel.querySelector('.twp-ai-chat');
        if (chat) aiPanel.insertBefore(wrap, chat); else aiPanel.appendChild(wrap);

        wrap.addEventListener('click', (e) => {
            const head = e.target.closest('.twp-ai-caps-head');
            if (head) {
                const isCollapsed = wrap.classList.toggle('collapsed');
                head.setAttribute('aria-expanded', String(!isCollapsed));
                localStorage.setItem('twpAiCapsCollapsed', isCollapsed ? '1' : '0');
                return;
            }
            const btn = e.target.closest('[data-prompt]');
            if (!btn) return;
            const input = document.getElementById('twpAiInput');
            if (input) { input.value = btn.dataset.prompt; input.focus(); }
        });
    }

    // ── DAX expression validator ───────────────────────────────────────
    // Lightweight, client-side, syntactic. Catches the common authoring
    // mistakes (unbalanced brackets / parens / quotes, unknown function
    // calls, references to columns that aren't in the active query) without
    // round-tripping to the server. Returns { ok, issues:[{level,message}] }.
    const FN_NAMES = new Set(DAX_FUNCS.map(f => f.name.toUpperCase()));
    function validateDaxLocally(expr, columns) {
        const issues = [];
        const text = String(expr || '');
        if (!text.trim()) return { ok: false, issues: [{ level: 'warn', message: 'Empty expression.' }] };

        // Bracket / paren / quote balance
        let paren = 0, brack = 0, inStr = false;
        for (let i = 0; i < text.length; i++) {
            const ch = text[i];
            if (ch === '"' && text[i - 1] !== '\\') { inStr = !inStr; continue; }
            if (inStr) continue;
            if (ch === '(') paren++;
            else if (ch === ')') paren--;
            else if (ch === '[') brack++;
            else if (ch === ']') brack--;
            if (paren < 0) { issues.push({ level: 'error', message: 'Unbalanced parenthesis: extra ")".' }); break; }
            if (brack < 0) { issues.push({ level: 'error', message: 'Unbalanced bracket: extra "]".' }); break; }
        }
        if (inStr) issues.push({ level: 'error', message: 'Unterminated string literal (missing closing ").' });
        if (paren > 0) issues.push({ level: 'error', message: `Missing ${paren} closing ")".` });
        if (brack > 0) issues.push({ level: 'error', message: `Missing ${brack} closing "]".` });

        // Unknown functions: identifiers immediately followed by '('
        const fnCalls = text.match(/\b([A-Za-z_][A-Za-z0-9_]*)\s*\(/g) || [];
        const seenUnknown = new Set();
        fnCalls.forEach(c => {
            const name = c.replace(/\s*\($/, '').toUpperCase();
            if (!FN_NAMES.has(name) && !seenUnknown.has(name)) {
                seenUnknown.add(name);
                issues.push({ level: 'warn', message: `Unknown function "${name}".` });
            }
        });

        // Unknown column references
        if (Array.isArray(columns) && columns.length > 0) {
            const colSet = new Set(columns.map(c => String(c).toLowerCase()));
            const refs = text.match(/\[([^\]]+)\]/g) || [];
            const seenCols = new Set();
            refs.forEach(r => {
                const name = r.slice(1, -1);
                const k = name.toLowerCase();
                if (!colSet.has(k) && !seenCols.has(k)) {
                    seenCols.add(k);
                    issues.push({ level: 'warn', message: `Column [${name}] not found in this query.` });
                }
            });
        }

        const hasError = issues.some(i => i.level === 'error');
        return { ok: !hasError, issues };
    }

    // Cross-module bridge: page.js's saveStepEditor calls this to refuse
    // commits that have hard validation errors. Falling back to columns
    // detected from the active query when the caller doesn't pass any.
    window.TWP_validateExpression = function (expr, columns) {
        const cols = Array.isArray(columns) && columns.length ? columns : getColumns();
        return validateDaxLocally(expr, cols);
    };

    function attachExprValidator(input) {
        if (!input || input.dataset.twpValBound === '1') return;
        input.dataset.twpValBound = '1';
        // Mount badge after the input.
        const badge = document.createElement('div');
        badge.className = 'twp-expr-validity twp-expr-validity-idle';
        badge.innerHTML = '<i class="bi bi-circle"></i><span></span>';
        input.insertAdjacentElement('afterend', badge);

        let timer = null;
        const run = () => {
            const cols = getColumns();
            const res = validateDaxLocally(input.value, cols);
            const labelEl = badge.querySelector('span');
            const iconEl = badge.querySelector('i');
            badge.classList.remove('twp-expr-validity-ok', 'twp-expr-validity-warn', 'twp-expr-validity-error', 'twp-expr-validity-idle');
            if (!input.value.trim()) {
                badge.classList.add('twp-expr-validity-idle');
                iconEl.className = 'bi bi-circle';
                labelEl.textContent = '';
                badge.title = '';
                return;
            }
            if (!res.ok) {
                badge.classList.add('twp-expr-validity-error');
                iconEl.className = 'bi bi-exclamation-octagon-fill';
                labelEl.textContent = res.issues[0].message;
                badge.title = res.issues.map(i => `[${i.level}] ${i.message}`).join('\n');
            } else if (res.issues.length > 0) {
                badge.classList.add('twp-expr-validity-warn');
                iconEl.className = 'bi bi-exclamation-triangle-fill';
                labelEl.textContent = res.issues[0].message;
                badge.title = res.issues.map(i => `[${i.level}] ${i.message}`).join('\n');
            } else {
                badge.classList.add('twp-expr-validity-ok');
                iconEl.className = 'bi bi-check-circle-fill';
                labelEl.textContent = 'Valid';
                badge.title = 'Expression looks good.';
            }
        };
        const debounced = () => { clearTimeout(timer); timer = setTimeout(run, 220); };
        input.addEventListener('input', debounced);
        input.addEventListener('blur', run);
        run();
    }

    // ── Outside-click handling ────────────────────────────────────────
    function closeFloatingsOnOutsideClick(e) {
        if (columnMenuEl && !columnMenuEl.contains(e.target) && !e.target.closest('.twp-col-head')) {
            closeColumnMenu();
        }
        if (intelliPopupEl && !intelliPopupEl.contains(e.target)
            && !(e.target.matches && e.target.matches('textarea, input[type=text]'))) {
            closeIntelli();
        }
    }
})();
