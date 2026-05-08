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
    // `derived_field` / `conditional` / `filter` handlers. Names match the
    // Microsoft DAX reference so users transferring from Power BI feel at
    // home; the engine on the server only validates a subset, but unknown
    // functions are flagged at IntelliSense time, not at submit.
    const DAX_FUNCS = (() => {
        const F = (category) => (name, sig) => ({ name, sig: sig || `${name}(…)`, category });
        const Logical       = F('Logical');
        const Agg           = F('Aggregate');
        const Iter          = F('Iterator');
        const Filt          = F('Filter');
        const Rel           = F('Relationship');
        const Info          = F('Information');
        const Date_         = F('Date');
        const Time_         = F('TimeIntelligence');
        const Text_         = F('Text');
        const Math_         = F('Math');
        const Stat          = F('Statistical');
        const Conv          = F('Conversion');
        const Tbl           = F('Table');
        const Parent        = F('Parent-Child');
        return [
            // Logical
            Logical('IF',          'IF(condition, then, else)'),
            Logical('IF.EAGER',    'IF.EAGER(condition, then, else)'),
            Logical('SWITCH',      'SWITCH(value, match1, result1, …, default)'),
            Logical('AND',         'AND(a, b)'),
            Logical('OR',          'OR(a, b)'),
            Logical('NOT',         'NOT(a)'),
            Logical('IFERROR',     'IFERROR(value, value_if_error)'),
            Logical('IFBLANK',     'IFBLANK(value, value_if_blank)'),
            Logical('COALESCE',    'COALESCE(value1, value2, …)'),
            Logical('TRUE',        'TRUE()'),
            Logical('FALSE',       'FALSE()'),
            Logical('BLANK',       'BLANK()'),
            Logical('ISBLANK',     'ISBLANK(value)'),
            Logical('ISERROR',     'ISERROR(value)'),
            Logical('ISLOGICAL',   'ISLOGICAL(value)'),
            Logical('ISNUMBER',    'ISNUMBER(value)'),
            Logical('ISTEXT',      'ISTEXT(value)'),
            Logical('ISNONTEXT',   'ISNONTEXT(value)'),
            Logical('ISEVEN',      'ISEVEN(number)'),
            Logical('ISODD',       'ISODD(number)'),
            Logical('BITAND',      'BITAND(a, b)'),
            Logical('BITOR',       'BITOR(a, b)'),
            Logical('BITXOR',      'BITXOR(a, b)'),
            Logical('BITLSHIFT',   'BITLSHIFT(value, shift)'),
            Logical('BITRSHIFT',   'BITRSHIFT(value, shift)'),

            // Information
            Info('CONTAINS',       'CONTAINS(Table, [Col1], val1, …)'),
            Info('CONTAINSROW',    'CONTAINSROW(Table, val1, …)'),
            Info('HASONEFILTER',   'HASONEFILTER([Column])'),
            Info('HASONEVALUE',    'HASONEVALUE([Column])'),
            Info('ISCROSSFILTERED','ISCROSSFILTERED(Table | [Column])'),
            Info('ISFILTERED',     'ISFILTERED(Table | [Column])'),
            Info('ISEMPTY',        'ISEMPTY(Table)'),
            Info('ISINSCOPE',      'ISINSCOPE([Column])'),
            Info('ISSELECTEDMEASURE','ISSELECTEDMEASURE(M1, M2, …)'),
            Info('SELECTEDMEASURE','SELECTEDMEASURE()'),
            Info('SELECTEDMEASURENAME','SELECTEDMEASURENAME()'),
            Info('USERELATIONSHIP','USERELATIONSHIP([Col1], [Col2])'),
            Info('USERNAME',       'USERNAME()'),
            Info('USERPRINCIPALNAME','USERPRINCIPALNAME()'),
            Info('USEROBJECTID',   'USEROBJECTID()'),

            // Aggregate
            Agg('SUM',             'SUM([Column])'),
            Agg('AVERAGE',         'AVERAGE([Column])'),
            Agg('MIN',             'MIN([Column])'),
            Agg('MAX',             'MAX([Column])'),
            Agg('COUNT',           'COUNT([Column])'),
            Agg('COUNTA',          'COUNTA([Column])'),
            Agg('COUNTBLANK',      'COUNTBLANK([Column])'),
            Agg('COUNTROWS',       'COUNTROWS(Table)'),
            Agg('DISTINCTCOUNT',   'DISTINCTCOUNT([Column])'),
            Agg('DISTINCTCOUNTNOBLANK','DISTINCTCOUNTNOBLANK([Column])'),
            Agg('PRODUCT',         'PRODUCT([Column])'),
            Agg('MEDIAN',          'MEDIAN([Column])'),
            Agg('PERCENTILE.INC',  'PERCENTILE.INC([Column], k)'),
            Agg('PERCENTILE.EXC',  'PERCENTILE.EXC([Column], k)'),
            Agg('VAR.S',           'VAR.S([Column])'),
            Agg('VAR.P',           'VAR.P([Column])'),
            Agg('STDEV.S',         'STDEV.S([Column])'),
            Agg('STDEV.P',         'STDEV.P([Column])'),
            Agg('GEOMEAN',         'GEOMEAN([Column])'),

            // Iterators
            Iter('SUMX',           'SUMX(Table, expression)'),
            Iter('AVERAGEX',       'AVERAGEX(Table, expression)'),
            Iter('MINX',           'MINX(Table, expression)'),
            Iter('MAXX',           'MAXX(Table, expression)'),
            Iter('COUNTX',         'COUNTX(Table, expression)'),
            Iter('COUNTAX',        'COUNTAX(Table, expression)'),
            Iter('PRODUCTX',       'PRODUCTX(Table, expression)'),
            Iter('CONCATENATEX',   'CONCATENATEX(Table, expression, delimiter)'),
            Iter('MEDIANX',        'MEDIANX(Table, expression)'),
            Iter('PERCENTILEX.INC','PERCENTILEX.INC(Table, expression, k)'),
            Iter('PERCENTILEX.EXC','PERCENTILEX.EXC(Table, expression, k)'),
            Iter('RANKX',          'RANKX(Table, expression, value, order, ties)'),
            Iter('GEOMEANX',       'GEOMEANX(Table, expression)'),

            // Filter / Calculate
            Filt('CALCULATE',      'CALCULATE(expression, filter1, …)'),
            Filt('CALCULATETABLE', 'CALCULATETABLE(Table, filter1, …)'),
            Filt('FILTER',         'FILTER(Table, condition)'),
            Filt('KEEPFILTERS',    'KEEPFILTERS(filter)'),
            Filt('REMOVEFILTERS',  'REMOVEFILTERS(Table | [Column], …)'),
            Filt('ALL',            'ALL(Table | [Column])'),
            Filt('ALLEXCEPT',      'ALLEXCEPT(Table, [Col1], …)'),
            Filt('ALLNOBLANKROW',  'ALLNOBLANKROW(Table)'),
            Filt('ALLSELECTED',    'ALLSELECTED(Table | [Column])'),
            Filt('VALUES',         'VALUES([Column])'),
            Filt('DISTINCT',       'DISTINCT(Table | [Column])'),
            Filt('EARLIER',        'EARLIER([Column], n)'),
            Filt('EARLIEST',       'EARLIEST([Column])'),
            Filt('SELECTEDVALUE',  'SELECTEDVALUE([Column], alt)'),
            Filt('TREATAS',        'TREATAS(Table, [Col1], …)'),
            Filt('CROSSFILTER',    'CROSSFILTER([Col1], [Col2], direction)'),

            // Relationship
            Rel('RELATED',         'RELATED([Column])'),
            Rel('RELATEDTABLE',    'RELATEDTABLE(Table)'),
            Rel('LOOKUPVALUE',     'LOOKUPVALUE(result_col, search_col, value, …)'),
            Rel('NATURALINNERJOIN','NATURALINNERJOIN(Table1, Table2)'),
            Rel('NATURALLEFTOUTERJOIN','NATURALLEFTOUTERJOIN(Table1, Table2)'),

            // Date & Time
            Date_('DATE',          'DATE(year, month, day)'),
            Date_('DATEDIFF',      'DATEDIFF(start, end, interval)'),
            Date_('DATEVALUE',     'DATEVALUE(text)'),
            Date_('YEAR',          'YEAR([Date])'),
            Date_('QUARTER',       'QUARTER([Date])'),
            Date_('MONTH',         'MONTH([Date])'),
            Date_('WEEKNUM',       'WEEKNUM([Date], type)'),
            Date_('WEEKDAY',       'WEEKDAY([Date], type)'),
            Date_('DAY',           'DAY([Date])'),
            Date_('HOUR',          'HOUR([DateTime])'),
            Date_('MINUTE',        'MINUTE([DateTime])'),
            Date_('SECOND',        'SECOND([DateTime])'),
            Date_('TIME',          'TIME(hour, minute, second)'),
            Date_('TIMEVALUE',     'TIMEVALUE(text)'),
            Date_('TODAY',         'TODAY()'),
            Date_('NOW',           'NOW()'),
            Date_('UTCNOW',        'UTCNOW()'),
            Date_('UTCTODAY',      'UTCTODAY()'),
            Date_('EOMONTH',       'EOMONTH([Date], months)'),
            Date_('EDATE',         'EDATE([Date], months)'),
            Date_('YEARFRAC',      'YEARFRAC(start, end, basis)'),
            Date_('NETWORKDAYS',   'NETWORKDAYS(start, end, holidays)'),
            Date_('CALENDAR',      'CALENDAR(start, end)'),
            Date_('CALENDARAUTO',  'CALENDARAUTO(fiscal_year_end_month)'),

            // Time intelligence
            Time_('DATEADD',       'DATEADD(Dates, n, interval)'),
            Time_('DATESBETWEEN',  'DATESBETWEEN([Date], start, end)'),
            Time_('DATESINPERIOD', 'DATESINPERIOD([Date], start, n, interval)'),
            Time_('DATESYTD',      'DATESYTD([Date], year_end)'),
            Time_('DATESQTD',      'DATESQTD([Date])'),
            Time_('DATESMTD',      'DATESMTD([Date])'),
            Time_('PARALLELPERIOD','PARALLELPERIOD([Date], n, interval)'),
            Time_('PREVIOUSDAY',   'PREVIOUSDAY([Date])'),
            Time_('PREVIOUSMONTH', 'PREVIOUSMONTH([Date])'),
            Time_('PREVIOUSQUARTER','PREVIOUSQUARTER([Date])'),
            Time_('PREVIOUSYEAR',  'PREVIOUSYEAR([Date])'),
            Time_('NEXTDAY',       'NEXTDAY([Date])'),
            Time_('NEXTMONTH',     'NEXTMONTH([Date])'),
            Time_('NEXTQUARTER',   'NEXTQUARTER([Date])'),
            Time_('NEXTYEAR',      'NEXTYEAR([Date])'),
            Time_('SAMEPERIODLASTYEAR','SAMEPERIODLASTYEAR([Date])'),
            Time_('STARTOFMONTH',  'STARTOFMONTH([Date])'),
            Time_('STARTOFQUARTER','STARTOFQUARTER([Date])'),
            Time_('STARTOFYEAR',   'STARTOFYEAR([Date])'),
            Time_('ENDOFMONTH',    'ENDOFMONTH([Date])'),
            Time_('ENDOFQUARTER',  'ENDOFQUARTER([Date])'),
            Time_('ENDOFYEAR',     'ENDOFYEAR([Date])'),
            Time_('TOTALYTD',      'TOTALYTD(expr, [Date], filter, year_end)'),
            Time_('TOTALQTD',      'TOTALQTD(expr, [Date], filter)'),
            Time_('TOTALMTD',      'TOTALMTD(expr, [Date], filter)'),
            Time_('OPENINGBALANCEMONTH','OPENINGBALANCEMONTH(expr, [Date])'),
            Time_('CLOSINGBALANCEMONTH','CLOSINGBALANCEMONTH(expr, [Date])'),
            Time_('FIRSTDATE',     'FIRSTDATE([Date])'),
            Time_('LASTDATE',      'LASTDATE([Date])'),
            Time_('FIRSTNONBLANK', 'FIRSTNONBLANK([Column], expression)'),
            Time_('LASTNONBLANK',  'LASTNONBLANK([Column], expression)'),

            // Text
            Text_('LEFT',          'LEFT(text, count)'),
            Text_('RIGHT',         'RIGHT(text, count)'),
            Text_('MID',           'MID(text, start, count)'),
            Text_('LEN',           'LEN(text)'),
            Text_('UPPER',         'UPPER(text)'),
            Text_('LOWER',         'LOWER(text)'),
            Text_('PROPER',        'PROPER(text)'),
            Text_('TRIM',          'TRIM(text)'),
            Text_('CLEAN',         'CLEAN(text)'),
            Text_('CONCATENATE',   'CONCATENATE(text1, text2)'),
            Text_('CONCATENATEX',  'CONCATENATEX(Table, expression, delimiter)'),
            Text_('SUBSTITUTE',    'SUBSTITUTE(text, old, new, instance)'),
            Text_('REPLACE',       'REPLACE(text, start, count, replacement)'),
            Text_('REPT',          'REPT(text, count)'),
            Text_('FIND',          'FIND(find, text, start, alt)'),
            Text_('SEARCH',        'SEARCH(find, text, start, alt)'),
            Text_('CONTAINSSTRING','CONTAINSSTRING(text, search)'),
            Text_('CONTAINSSTRINGEXACT','CONTAINSSTRINGEXACT(text, search)'),
            Text_('EXACT',         'EXACT(text1, text2)'),
            Text_('FIXED',         'FIXED(number, decimals, no_commas)'),
            Text_('VALUE',         'VALUE(text)'),
            Text_('UNICODE',       'UNICODE(text)'),
            Text_('UNICHAR',       'UNICHAR(number)'),
            Text_('FORMAT',        'FORMAT(value, format_string)'),
            Text_('COMBINEVALUES', 'COMBINEVALUES(delimiter, val1, val2, …)'),

            // Math / Stats
            Math_('DIVIDE',        'DIVIDE(num, denom, alt)'),
            Math_('ROUND',         'ROUND(value, digits)'),
            Math_('ROUNDUP',       'ROUNDUP(value, digits)'),
            Math_('ROUNDDOWN',     'ROUNDDOWN(value, digits)'),
            Math_('MROUND',        'MROUND(value, multiple)'),
            Math_('CEILING',       'CEILING(value, significance)'),
            Math_('FLOOR',         'FLOOR(value, significance)'),
            Math_('INT',           'INT(value)'),
            Math_('TRUNC',         'TRUNC(value, digits)'),
            Math_('ABS',           'ABS(value)'),
            Math_('SIGN',          'SIGN(value)'),
            Math_('POWER',         'POWER(base, exponent)'),
            Math_('SQRT',          'SQRT(value)'),
            Math_('EXP',           'EXP(value)'),
            Math_('LN',            'LN(value)'),
            Math_('LOG',           'LOG(value, base)'),
            Math_('LOG10',         'LOG10(value)'),
            Math_('MOD',           'MOD(value, divisor)'),
            Math_('QUOTIENT',      'QUOTIENT(num, denom)'),
            Math_('GCD',           'GCD(n1, n2, …)'),
            Math_('LCM',           'LCM(n1, n2, …)'),
            Math_('PI',            'PI()'),
            Math_('RAND',          'RAND()'),
            Math_('RANDBETWEEN',   'RANDBETWEEN(low, high)'),
            Math_('FACT',          'FACT(value)'),
            Math_('COMBIN',        'COMBIN(n, k)'),
            Math_('PERMUT',        'PERMUT(n, k)'),
            Math_('DEGREES',       'DEGREES(radians)'),
            Math_('RADIANS',       'RADIANS(degrees)'),
            Math_('SIN',           'SIN(value)'),
            Math_('COS',           'COS(value)'),
            Math_('TAN',           'TAN(value)'),
            Math_('ASIN',          'ASIN(value)'),
            Math_('ACOS',          'ACOS(value)'),
            Math_('ATAN',          'ATAN(value)'),
            Math_('ATAN2',         'ATAN2(x, y)'),
            Math_('SINH',          'SINH(value)'),
            Math_('COSH',          'COSH(value)'),
            Math_('TANH',          'TANH(value)'),
            Math_('CURRENCY',      'CURRENCY(value)'),

            // Statistical
            Stat('NORM.DIST',      'NORM.DIST(x, mean, sd, cumulative)'),
            Stat('NORM.INV',       'NORM.INV(p, mean, sd)'),
            Stat('NORM.S.DIST',    'NORM.S.DIST(z, cumulative)'),
            Stat('NORM.S.INV',     'NORM.S.INV(probability)'),
            Stat('T.DIST',         'T.DIST(x, df, cumulative)'),
            Stat('T.INV',          'T.INV(p, df)'),
            Stat('CONFIDENCE.NORM','CONFIDENCE.NORM(alpha, sd, n)'),
            Stat('CONFIDENCE.T',   'CONFIDENCE.T(alpha, sd, n)'),
            Stat('LINEST',         'LINEST(known_y, known_x, …)'),
            Stat('LINESTX',        'LINESTX(Table, known_y, known_x, …)'),
            Stat('SAMPLE',         'SAMPLE(n, Table, expr, [order])'),
            Stat('TOPN',           'TOPN(n, Table, expr, [order])'),
            Stat('BOTTOMN',        'BOTTOMN(n, Table, expr, [order])'),
            Stat('RANK.EQ',        'RANK.EQ(value, [Column], order)'),
            Stat('PERCENTRANK.INC','PERCENTRANK.INC([Column], value)'),
            Stat('PERCENTRANK.EXC','PERCENTRANK.EXC([Column], value)'),

            // Conversion
            Conv('CONVERT',        'CONVERT(value, type)'),
            Conv('CURRENCY',       'CURRENCY(value)'),
            Conv('DATATABLE',      'DATATABLE(name1, type1, …, { rows })'),
            Conv('FORMAT',         'FORMAT(value, format_string)'),

            // Table
            Tbl('ADDCOLUMNS',      'ADDCOLUMNS(Table, name, expression, …)'),
            Tbl('SELECTCOLUMNS',   'SELECTCOLUMNS(Table, name, expression, …)'),
            Tbl('SUMMARIZE',       'SUMMARIZE(Table, [Col1], …, name, expression, …)'),
            Tbl('SUMMARIZECOLUMNS','SUMMARIZECOLUMNS([Col1], …, filter, name, expression)'),
            Tbl('GROUPBY',         'GROUPBY(Table, [Col1], …, name, expression)'),
            Tbl('ROW',             'ROW(name, expression, …)'),
            Tbl('UNION',           'UNION(Table1, Table2, …)'),
            Tbl('INTERSECT',       'INTERSECT(Table1, Table2)'),
            Tbl('EXCEPT',          'EXCEPT(Table1, Table2)'),
            Tbl('CROSSJOIN',       'CROSSJOIN(Table1, Table2, …)'),
            Tbl('GENERATE',        'GENERATE(Table1, Table2)'),
            Tbl('GENERATEALL',     'GENERATEALL(Table1, Table2)'),
            Tbl('GENERATESERIES',  'GENERATESERIES(start, end, step)'),
            Tbl('CURRENTGROUP',    'CURRENTGROUP()'),
            Tbl('VAR',             'VAR name = expression RETURN …'),

            // Parent-Child
            Parent('PATH',         'PATH(idCol, parentCol)'),
            Parent('PATHCONTAINS', 'PATHCONTAINS(path, item)'),
            Parent('PATHITEM',     'PATHITEM(path, position, type)'),
            Parent('PATHITEMREVERSE','PATHITEMREVERSE(path, position, type)'),
            Parent('PATHLENGTH',   'PATHLENGTH(path)')
        ];
    })();

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
            if (e.key === 'Escape') { closeColumnMenu(); closeIntelli(); closeDaxModal(); closeTypePopup(); }
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
    function onPreviewRendered(ev) {
        // Refresh data-driven AI prompts and the last-step review panel
        // every time the preview re-renders. Both depend on the actual
        // rows/columns the user is now seeing, not on a hardcoded list.
        try { rebuildAiSuggestionsFromData(ev && ev.detail); } catch (_) { /* never break preview */ }
        try { rebuildLastStepReview(ev && ev.detail);       } catch (_) { /* never break preview */ }

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

        // P20.8 — delegated handler for new data-act buttons (sort arrows,
        // delete column, type pill, delete row). Bound once per table.
        if (tbl.dataset.twpDelegated !== '1') {
            tbl.dataset.twpDelegated = '1';
            tbl.addEventListener('click', (e) => {
                const btn = e.target.closest('[data-act]');
                if (!btn || !tbl.contains(btn)) return;
                const act = btn.dataset.act;
                const th = btn.closest('th[data-col]');
                const tr = btn.closest('tr[data-rowidx]');
                const col = th && th.dataset.col;
                switch (act) {
                    case 'col-sort-asc':
                        e.stopPropagation();
                        setSortDir(col, 'asc');
                        break;
                    case 'col-sort-desc':
                        e.stopPropagation();
                        setSortDir(col, 'desc');
                        break;
                    case 'col-delete':
                        e.stopPropagation();
                        if (col) dispatchStep('remove_columns', { columns: [col] });
                        break;
                    case 'col-type':
                        e.stopPropagation();
                        if (col) openTypePopup(btn, col);
                        break;
                    case 'row-delete':
                        e.stopPropagation();
                        deleteRowByFilter(tr);
                        break;
                }
            });
        }
    }

    function setSortDir(col, dir) {
        if (!col || !window.TWP || typeof window.TWP.toggleSort !== 'function') {
            dispatchStep(dir === 'asc' ? 'sort_asc' : 'sort_desc', { column: col, direction: dir });
            return;
        }
        const getState = window.TWP.getSortState ? () => window.TWP.getSortState(col) : () => null;
        // Cycle is none → asc → desc → none. Toggle up to 3 times to reach desired dir.
        for (let i = 0; i < 3; i++) {
            const st = getState();
            if (st && st.dir === dir) return;
            window.TWP.toggleSort(col);
        }
    }

    let typePopupEl = null;
    function closeTypePopup() {
        if (typePopupEl) { typePopupEl.remove(); typePopupEl = null; }
    }
    function openTypePopup(anchorBtn, col) {
        closeTypePopup();
        const tbl = document.getElementById('twpPreviewTable');
        let current = '';
        try {
            const map = JSON.parse((tbl && tbl.dataset.colTypes) || '{}');
            current = map[col] || '';
        } catch (_) { /* ignore */ }
        const rect = anchorBtn.getBoundingClientRect();
        const pop = document.createElement('div');
        pop.className = 'twp-type-popup';
        pop.style.left = Math.round(rect.left) + 'px';
        pop.style.top = Math.round(rect.bottom + 4) + 'px';
        pop.innerHTML = TYPE_CHOICES.map(t =>
            `<button type="button" class="twp-type-popup-item ${t.type === current ? 'is-current' : ''}" data-type="${t.type}">`
            + `<i class="bi ${t.icon}"></i><span>${esc(t.label)}</span>`
            + (t.type === current ? '<i class="bi bi-check2 ms-auto"></i>' : '')
            + `</button>`
        ).join('');
        document.body.appendChild(pop);
        typePopupEl = pop;
        pop.addEventListener('click', (e) => {
            const item = e.target.closest('[data-type]');
            if (!item) return;
            dispatchStep('change_type', { column: col, type: item.dataset.type });
            closeTypePopup();
        });
        // Clamp to viewport
        requestAnimationFrame(() => {
            const r = pop.getBoundingClientRect();
            if (r.right > window.innerWidth - 8) {
                pop.style.left = Math.max(8, window.innerWidth - r.width - 8) + 'px';
            }
            if (r.bottom > window.innerHeight - 8) {
                pop.style.top = Math.max(8, rect.top - r.height - 4) + 'px';
            }
        });
    }

    function deleteRowByFilter(tr) {
        if (!tr) return;
        let sig = {};
        try { sig = JSON.parse(tr.dataset.rowsig || '{}'); } catch (_) { return; }
        const keys = Object.keys(sig);
        if (keys.length === 0) return;
        // Prefer Id-like key; otherwise first scalar non-empty value.
        const isScalar = (v) => v === null || ['string', 'number', 'boolean'].includes(typeof v);
        const nonEmpty = (v) => v != null && String(v).trim() !== '';
        let keyCol = keys.find(k => /^id$|id$|^guid$/i.test(k) && nonEmpty(sig[k]) && isScalar(sig[k]));
        if (!keyCol) keyCol = keys.find(k => isScalar(sig[k]) && nonEmpty(sig[k]));
        if (!keyCol) return;
        const raw = sig[keyCol];
        let expr;
        if (typeof raw === 'number' || typeof raw === 'boolean') {
            expr = `[${keyCol}] <> ${raw}`;
        } else {
            const v = String(raw).replace(/"/g, '\\"');
            expr = `[${keyCol}] <> "${v}"`;
        }
        dispatchStep('filter', { expression: expr });
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
        // Most "simple" ops apply directly and only reflect through the
        // preview — no confirmation dialog, matching the user-flow ask:
        // single click → step appears in pipeline → preview re-renders.
        // The few ops that need free-text input use an inline popover
        // (openInlinePopover) instead of the native browser prompt() so
        // the look-and-feel matches the rest of the workbench.
        const headerEl = document.querySelector(`#twpPreviewTable thead th[data-col="${cssEscape(col)}"]`) || document.body;
        switch (act) {
            case 'rename': {
                openInlinePopover(headerEl, {
                    title: `Rename "${col}"`,
                    fields: [{ key: 'to', label: 'New name', value: col, autofocus: true }],
                    okLabel: 'Rename',
                    onSubmit: (vals) => {
                        const to = (vals.to || '').trim();
                        if (to && to !== col) dispatchStep('rename_column', { from: col, to });
                    }
                });
                break;
            }
            case 'duplicate':
                dispatchStep('duplicate_column', { source: col, target_field: col + ' (copy)' });
                break;
            case 'remove':
                dispatchStep('remove_columns', { columns: [col] });
                break;
            case 'replace': {
                openInlinePopover(headerEl, {
                    title: `Replace values in "${col}"`,
                    fields: [
                        { key: 'from', label: 'Value to find', value: '', autofocus: true },
                        { key: 'to',   label: 'Replacement',   value: '' }
                    ],
                    okLabel: 'Replace',
                    onSubmit: (vals) => {
                        if (vals.from === '' && vals.to === '') return;
                        dispatchStep('replace_values', { column: col, from: vals.from, to: vals.to });
                    }
                });
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
                openInlinePopover(headerEl, {
                    title: `Keep rows where [${col}] equals…`,
                    fields: [{ key: 'v', label: 'Value', value: '', autofocus: true }],
                    okLabel: 'Apply filter',
                    onSubmit: (vals) => {
                        const v = vals.v;
                        if (v === '' || v == null) return;
                        dispatchStep('filter', { expression: `[${col}] = "${String(v).replace(/"/g, '\\"')}"` });
                    }
                });
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
        if (typePopupEl && !typePopupEl.contains(e.target)
            && !(e.target.closest && e.target.closest('.twp-col-type-pill'))) {
            closeTypePopup();
        }
        if (inlinePopoverEl && !inlinePopoverEl.contains(e.target)) {
            closeInlinePopover();
        }
    }

    // ── Inline popover (replaces native prompt() for rename/replace/filter) ──
    let inlinePopoverEl = null;
    function cssEscape(s) {
        if (window.CSS && CSS.escape) return CSS.escape(s);
        return String(s).replace(/[^a-zA-Z0-9_-]/g, ch => '\\' + ch);
    }
    function closeInlinePopover() {
        if (inlinePopoverEl) { inlinePopoverEl.remove(); inlinePopoverEl = null; }
    }
    function openInlinePopover(anchor, opts) {
        closeInlinePopover();
        const rect = (anchor && anchor.getBoundingClientRect)
            ? anchor.getBoundingClientRect()
            : { left: window.innerWidth / 2 - 160, bottom: window.innerHeight / 2, top: 0, right: 0, height: 0, width: 0 };
        const fields = opts.fields || [];
        const pop = document.createElement('div');
        pop.className = 'twp-inline-popover';
        pop.style.left = Math.round(rect.left) + 'px';
        pop.style.top = Math.round(rect.bottom + 4) + 'px';
        pop.innerHTML = `
            <div class="twp-inline-popover-head">
                <span>${esc(opts.title || '')}</span>
                <button type="button" class="btn-close btn-close-sm" data-act="close" aria-label="Close"></button>
            </div>
            <div class="twp-inline-popover-body">
                ${fields.map((f, i) => `
                    <label class="twp-inline-popover-label">${esc(f.label || '')}</label>
                    <input type="text" class="form-control form-control-sm twp-inline-popover-input" data-key="${esc(f.key)}" value="${esc(f.value ?? '')}" data-idx="${i}" />
                `).join('')}
            </div>
            <div class="twp-inline-popover-foot">
                <button type="button" class="btn btn-sm btn-outline-secondary" data-act="cancel">Cancel</button>
                <button type="button" class="btn btn-sm btn-primary" data-act="ok">${esc(opts.okLabel || 'OK')}</button>
            </div>
        `;
        document.body.appendChild(pop);
        inlinePopoverEl = pop;
        const inputs = Array.from(pop.querySelectorAll('.twp-inline-popover-input'));
        const submit = () => {
            const vals = {};
            inputs.forEach(i => { vals[i.dataset.key] = i.value; });
            try { opts.onSubmit && opts.onSubmit(vals); } finally { closeInlinePopover(); }
        };
        pop.addEventListener('click', (e) => {
            const a = e.target.closest('[data-act]');
            if (!a) return;
            if (a.dataset.act === 'ok') submit();
            else closeInlinePopover();
        });
        inputs.forEach(i => {
            i.addEventListener('keydown', (e) => {
                if (e.key === 'Enter') { e.preventDefault(); submit(); }
                else if (e.key === 'Escape') { e.preventDefault(); closeInlinePopover(); }
            });
        });
        const focusIdx = fields.findIndex(f => f.autofocus);
        const focusEl = inputs[focusIdx >= 0 ? focusIdx : 0];
        if (focusEl) setTimeout(() => { focusEl.focus(); focusEl.select(); }, 0);
        requestAnimationFrame(() => {
            const r = pop.getBoundingClientRect();
            if (r.right > window.innerWidth - 8) {
                pop.style.left = Math.max(8, window.innerWidth - r.width - 8) + 'px';
            }
            if (r.bottom > window.innerHeight - 8) {
                pop.style.top = Math.max(8, rect.top - r.height - 4) + 'px';
            }
        });
    }

    // ── Dynamic AI suggestions (replace hardcoded list) ──────────────
    // Computed from the live preview each time it renders, so prompts are
    // grounded in the actual data the user is looking at instead of a
    // canned demo list.
    function rebuildAiSuggestionsFromData(detail) {
        const list = document.querySelector('.twp-ai-caps .twp-ai-caps-list');
        if (!list) return;
        const tbl = document.getElementById('twpPreviewTable');
        const cols = (detail && detail.columns) || [];
        const types = (detail && detail.types) || (() => {
            try { return JSON.parse((tbl && tbl.dataset.colTypes) || '{}'); } catch (_) { return {}; }
        })();
        const rows = tbl ? Array.from(tbl.querySelectorAll('tbody tr[data-rowidx]')) : [];
        const sample = rows.slice(0, 200).map(tr => {
            try { return JSON.parse(tr.dataset.rowsig || '{}'); } catch (_) { return {}; }
        });

        const out = [];
        // 1) Numeric values stuck in text columns
        cols.forEach(c => {
            if ((types[c] || 'text') !== 'text') return;
            let n = 0, total = 0;
            for (const r of sample) {
                const v = r[c];
                if (v == null || String(v).trim() === '') continue;
                total++;
                if (/^-?\d+(\.\d+)?$/.test(String(v).trim())) n++;
            }
            if (total >= 5 && n / total >= 0.85) {
                out.push({ icon: 'bi-shuffle', title: `Convert "${c}" to number`, hint: `change type of ${c} to decimal` });
            }
        });
        // 2) ISO-date values stuck in text columns
        cols.forEach(c => {
            if ((types[c] || 'text') !== 'text') return;
            let n = 0, total = 0;
            for (const r of sample) {
                const v = r[c];
                if (v == null || String(v).trim() === '') continue;
                total++;
                if (/^\d{4}-\d{2}-\d{2}/.test(String(v).trim())) n++;
            }
            if (total >= 5 && n / total >= 0.85) {
                out.push({ icon: 'bi-calendar-event', title: `Parse "${c}" as a date`, hint: `change type of ${c} to date` });
            }
        });
        // 3) High-blank columns
        cols.forEach(c => {
            let blanks = 0;
            for (const r of sample) {
                const v = r[c];
                if (v == null || String(v).trim() === '') blanks++;
            }
            if (sample.length >= 10 && blanks / sample.length >= 0.3) {
                out.push({ icon: 'bi-eraser', title: `"${c}" is ${Math.round(100 * blanks / sample.length)}% blank`, hint: `remove blanks in ${c}` });
            }
        });
        // 4) Duplicate ids
        const idCol = cols.find(c => /^id$|id$|^guid$/i.test(c));
        if (idCol && sample.length >= 5) {
            const seen = new Set(); let dup = 0;
            for (const r of sample) {
                const k = r[idCol];
                if (k == null || k === '') continue;
                if (seen.has(k)) dup++; else seen.add(k);
            }
            if (dup > 0) {
                out.push({ icon: 'bi-files', title: `${dup} duplicate ${idCol} value${dup === 1 ? '' : 's'}`, hint: `remove duplicates by ${idCol}` });
            }
        }
        // 5) Wide tables — drop columns hint
        if (cols.length >= 12) {
            out.push({ icon: 'bi-columns-gap', title: `${cols.length} columns is wide — drop unused`, hint: 'remove columns I do not need' });
        }
        // 6) DAX hint anchored on first numeric column
        const firstNumeric = cols.find(c => ['integer', 'decimal'].includes(types[c]));
        if (firstNumeric) {
            out.push({
                icon: 'bi-calculator',
                title: `Author DAX on [${firstNumeric}]`,
                hint: `add column ${firstNumeric}Pct = DIVIDE([${firstNumeric}], SUM([${firstNumeric}]))`
            });
        }
        // 7) Always-on generic prompts
        out.push({ icon: 'bi-shield-check', title: 'Audit my pipeline for issues', hint: 'review my pipeline for issues' });
        out.push({ icon: 'bi-magic',        title: 'Suggest the next step',       hint: 'suggest the next transform step' });

        const capped = out.slice(0, 8);
        list.innerHTML = capped.map(c => `
            <button type="button" class="twp-ai-cap" data-prompt="${esc(c.hint)}" title="Click to load this prompt">
                <span class="twp-ai-cap-icon"><i class="bi ${esc(c.icon)}"></i></span>
                <span class="twp-ai-cap-text">
                    <span class="twp-ai-cap-title">${esc(c.title)}</span>
                    <span class="twp-ai-cap-hint">${esc(c.hint)}</span>
                </span>
                <i class="bi bi-arrow-right-short twp-ai-cap-go"></i>
            </button>
        `).join('');
    }

    // ── Last-step review panel ───────────────────────────────────────
    // Examines the most-recently-applied step against the rendered preview
    // and surfaces problems (e.g. change_type that left non-coercible
    // values behind, filter that wiped the table, derived field that did
    // not appear). Offers a one-click "Delete this step" so users don't
    // have to hunt through the steps list to roll back a bad action.
    function rebuildLastStepReview(detail) {
        const host = document.getElementById('twpPreviewWrap') || document.getElementById('twpViewPreview');
        if (!host || !host.parentNode) return;
        let bar = document.getElementById('twpStepReview');
        const q = window.TWP && window.TWP.activeQuery && window.TWP.activeQuery();
        const steps = (q && Array.isArray(q.steps)) ? q.steps : [];
        let lastIdx = -1;
        for (let i = steps.length - 1; i >= 0; i--) {
            if (steps[i] && steps[i].enabled !== false) { lastIdx = i; break; }
        }
        const last = lastIdx >= 0 ? steps[lastIdx] : null;
        if (!last) { if (bar) bar.remove(); return; }

        const issues = computeStepIssues(last, detail);
        if (issues.length === 0) { if (bar) bar.remove(); return; }

        if (!bar) {
            bar = document.createElement('div');
            bar.id = 'twpStepReview';
            bar.className = 'twp-step-review';
            host.parentNode.insertBefore(bar, host);
        }
        const stepLabel = (last.op || 'step') + (last.params && last.params.column ? ` · [${last.params.column}]` : '');
        bar.innerHTML = `
            <div class="twp-step-review-icon"><i class="bi bi-exclamation-triangle-fill"></i></div>
            <div class="twp-step-review-text">
                <div class="twp-step-review-title">Last step may have issues — ${esc(stepLabel)}</div>
                <ul class="twp-step-review-issues">
                    ${issues.slice(0, 3).map(m => `<li>${esc(m)}</li>`).join('')}
                </ul>
            </div>
            <div class="twp-step-review-actions">
                <button type="button" class="btn btn-sm btn-outline-secondary" data-act="edit-step">
                    <i class="bi bi-pencil me-1"></i>Edit
                </button>
                <button type="button" class="btn btn-sm btn-danger" data-act="delete-step">
                    <i class="bi bi-trash me-1"></i>Delete this step
                </button>
            </div>
        `;
        bar.onclick = (e) => {
            const a = e.target.closest('[data-act]');
            if (!a) return;
            if (a.dataset.act === 'delete-step') {
                if (q && Array.isArray(q.steps) && lastIdx >= 0) {
                    q.steps.splice(lastIdx, 1);
                    bar.remove();
                    if (window.TWP && typeof window.TWP.refresh === 'function') {
                        window.TWP.refresh();
                    } else {
                        document.dispatchEvent(new CustomEvent('twp:steps-changed'));
                    }
                }
            } else if (a.dataset.act === 'edit-step') {
                const editBtn = document.querySelector(
                    `#twpStepsList .twp-step-item[data-idx="${lastIdx}"] [data-act="edit"]`);
                if (editBtn) editBtn.click();
                else document.dispatchEvent(new CustomEvent('twp:edit-step', { detail: { index: lastIdx } }));
            }
        };
    }

    function computeStepIssues(step, detail) {
        const issues = [];
        if (!step || !step.params) return issues;
        const tbl = document.getElementById('twpPreviewTable');
        const types = (detail && detail.types) || (() => {
            try { return JSON.parse((tbl && tbl.dataset.colTypes) || '{}'); } catch (_) { return {}; }
        })();
        const rows = tbl ? Array.from(tbl.querySelectorAll('tbody tr[data-rowidx]')) : [];
        const sample = rows.slice(0, 200).map(tr => {
            try { return JSON.parse(tr.dataset.rowsig || '{}'); } catch (_) { return {}; }
        });
        const previewCols = (detail && detail.columns) || [];

        const checkCoerce = (col, target) => {
            if (!col) return;
            const tester = ({
                integer:  v => /^-?\d+$/.test(String(v).trim()),
                decimal:  v => /^-?\d+(\.\d+)?$/.test(String(v).trim()),
                boolean:  v => /^(true|false|0|1|yes|no)$/i.test(String(v).trim()),
                date:     v => /^\d{4}-\d{2}-\d{2}/.test(String(v).trim()) || !isNaN(Date.parse(v)),
                datetime: v => /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/.test(String(v).trim()) || !isNaN(Date.parse(v)),
                time:     v => /^\d{1,2}:\d{2}(:\d{2})?$/.test(String(v).trim())
            })[target];
            if (!tester) return;
            let bad = 0, total = 0;
            for (const r of sample) {
                const v = r[col];
                if (v == null || String(v).trim() === '') continue;
                total++;
                if (!tester(v)) bad++;
            }
            if (total > 0 && bad > 0) {
                const pct = Math.round((bad / total) * 100);
                issues.push(`${bad} of ${total} values (${pct}%) in [${col}] cannot be parsed as ${target}.`);
            }
        };

        switch (step.op) {
            case 'change_type':
                checkCoerce(step.params.column, step.params.type);
                break;
            case 'filter':
                if (rows.length === 0 && step.params.expression) {
                    issues.push(`Filter "${step.params.expression}" eliminated every row.`);
                }
                break;
            case 'remove_columns':
                if (rows.length > 0 && previewCols.length === 0) {
                    issues.push('All columns were removed — preview is empty.');
                }
                break;
            case 'rename_column':
                if (step.params.to && !previewCols.includes(step.params.to)) {
                    issues.push(`Renamed column "${step.params.to}" did not appear in the preview — source column may not exist.`);
                }
                break;
            case 'derived_field':
                if (step.params.target_field && !previewCols.includes(step.params.target_field)) {
                    issues.push(`Derived field "${step.params.target_field}" did not appear in the preview.`);
                }
                break;
        }
        return issues;
    }
})();
