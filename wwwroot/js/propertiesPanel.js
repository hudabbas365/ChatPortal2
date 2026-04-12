// Properties panel - handles the right panel
class PropertiesPanel {
    constructor() {
        this.currentChart = null;
        this.fields = [];
        this.activeTab = 'fields';
        this._loadSeq = 0;
    }

    clear() {
        this.currentChart = null;
        const form = document.getElementById('properties-form');
        const empty = document.getElementById('properties-empty');
        if (form) form.style.display = 'none';
        if (empty) empty.style.display = '';
        this.showShapeProps(false);
        this.updateFieldHighlights();
    }

    async load(chartDef) {
        const loadId = ++this._loadSeq;
        this.currentChart = JSON.parse(JSON.stringify(chartDef));
        const form = document.getElementById('properties-form');
        const empty = document.getElementById('properties-empty');
        if (form) form.style.display = 'block';
        if (empty) empty.style.display = 'none';

        const isShape = window.ShapeManager && ShapeManager.isShape(chartDef.chartType);
        this.showShapeProps(isShape);

        if (isShape) {
            this.populateShapeProps(chartDef);
            this.bindShapeAutoApply();
        } else {
            await this.loadFields(chartDef.datasetName);
            // Guard: if another load() started while we were awaiting, abort
            if (this._loadSeq !== loadId) return;
            this.populate();
            this.bindAutoApply();
            this._wireMultiValueBtn();
            this.updateTypeSpecificFields(chartDef.chartType);
            this.initFieldDropTargets();
            this.updateFieldHighlights();
        }
        this.switchTab(this.activeTab);
    }

    async loadFields(datasetName) {
        // Try real datasource fields first
        const dsId = this.currentChart?.datasourceId || window.currentDatasourceId || null;
        if (dsId) {
            try {
                const resp = await fetch('/api/datasources/' + dsId + '/schema');
                if (resp.ok) {
                    const schema = await resp.json();
                    const tables = schema.tables || [];
                    // Store full table structure for grouped rendering
                    this._schemaTables = tables.map(t => ({
                        name: t.name || '',
                        columns: (t.columns || []).map(c => ({ name: c.name, dataType: c.dataType || '', isPrimaryKey: c.isPrimaryKey || false }))
                    }));
                    // Flatten all column names for field selects
                    this.fields = [];
                    this._schemaColumns = [];
                    tables.forEach(t => {
                        (t.columns || []).forEach(c => {
                            if (c.name) {
                                this._schemaColumns.push({ name: c.name, dataType: c.dataType || '', table: t.name || '', isPrimaryKey: c.isPrimaryKey || false });
                                if (!this.fields.includes(c.name)) {
                                    this.fields.push(c.name);
                                }
                            }
                        });
                    });
                    if (this.fields.length > 0) {
                        this.updateFieldSelects(datasetName);
                        this.renderDataFields();
                        return;
                    }
                }
            } catch(e) {}
            // Fallback: try plain fields endpoint
            try {
                const resp = await fetch('/api/datasources/' + dsId + '/fields');
                if (resp.ok) {
                    this.fields = await resp.json();
                    this._schemaColumns = this.fields.map(f => ({ name: f, dataType: '', table: '', isPrimaryKey: false }));
                    this._schemaTables = null;
                    if (this.fields.length > 0) {
                        this.updateFieldSelects(datasetName);
                        this.renderDataFields();
                        return;
                    }
                }
            } catch(e) {}
        }
        try {
            const resp = await fetch(`/api/data/${datasetName}/fields`);
            this.fields = await resp.json();
            this._schemaColumns = this.fields.map(f => ({ name: f, dataType: '', table: '', isPrimaryKey: false }));
            this._schemaTables = null;
            this.updateFieldSelects(datasetName);
            this.renderDataFields();
        } catch(e) {
            this.fields = [];
            this._schemaColumns = [];
            this._schemaTables = null;
        }
    }

    updateFieldSelects(filterTable) {
        let fieldList = this.fields;
        if (filterTable && this._schemaColumns && this._schemaColumns.length > 0) {
            const ft = filterTable.toLowerCase();
            const tableFields = this._schemaColumns
                .filter(c => c.table && c.table.toLowerCase() === ft)
                .map(c => c.name);
            if (tableFields.length > 0) fieldList = tableFields;
        }
        const selects = document.querySelectorAll('.field-select');
        selects.forEach(sel => {
            const current = sel.value;
            sel.innerHTML = '<option value="">-- none --</option>' +
                fieldList.map(f => `<option value="${escapeHtml(f)}">${escapeHtml(f)}</option>`).join('');
            if (current) sel.value = current;
        });
    }

    populate() {
        if (!this.currentChart) return;
        const c = this.currentChart;
        this.setVal('prop-title', c.title);
        this.setVal('prop-chart-type', c.chartType);
        this.setVal('prop-dataset', c.datasetName);
        this.setVal('prop-width', c.width);
        this.setVal('prop-height', c.height);
        // Resolve field names case-insensitively against available fields
        this.setFieldVal('prop-label-field', c.mapping?.labelField || '');
        this.setFieldVal('prop-value-field', c.mapping?.valueField || '');
        this.setFieldVal('prop-line-value-field', c.mapping?.lineValueField || '');
        this.setFieldVal('prop-x-field', c.mapping?.xField || '');
        this.setFieldVal('prop-y-field', c.mapping?.yField || '');
        this.setFieldVal('prop-r-field', c.mapping?.rField || '');
        this.setFieldVal('prop-group-by-field', c.mapping?.groupByField || '');
        this.setVal('prop-agg-enabled', c.aggregation?.enabled || false, 'checkbox');
        this.setVal('prop-agg-function', c.aggregation?.function || 'SUM');
        this.setVal('prop-row-limit', c.rowLimit || 100);
        this.setVal('prop-filter-where', c.filterWhere || '');
        this.setVal('prop-color-palette', this._resolveColorHex(c.style?.colorPalette));
        this.setVal('prop-show-legend', c.style?.showLegend !== false, 'checkbox');
        this.setVal('prop-legend-position', c.style?.legendPosition || 'top');
        this.setVal('prop-fill-area', c.style?.fillArea || false, 'checkbox');
        this.setVal('prop-show-tooltips', c.style?.showTooltips !== false, 'checkbox');
        this.setVal('prop-animated', c.style?.animated !== false, 'checkbox');
        this.setVal('prop-title-font-size', c.style?.titleFontSize || 14);
        this.setVal('prop-border-radius', c.style?.borderRadius || '4');
        this.setVal('prop-custom-json', c.customJsonData || '');
        // Render additional value fields
        this._renderMultiValueFields(c.mapping?.multiValueFields || []);
    }

    _resolveColorHex(colorPalette) {
        if (!colorPalette) return '#4A90D9';
        if (colorPalette.startsWith('#')) return colorPalette;
        // Map legacy named palettes to their primary hex color
        const paletteMap = {
            default: '#4A90D9',
            ocean:   '#006994',
            sunset:  '#FF6B6B',
            forest:  '#2D6A4F',
            rainbow: '#E63946',
            pastel:  '#FFB3BA',
        };
        return paletteMap[colorPalette] || '#4A90D9';
    }

    setVal(id, val, type = 'value') {
        const el = document.getElementById(id);
        if (!el) return;
        if (type === 'checkbox') el.checked = !!val;
        else el.value = val ?? '';
    }

    getVal(id, type = 'value') {
        const el = document.getElementById(id);
        if (!el) return undefined;
        if (type === 'checkbox') return el.checked;
        return el.value;
    }

    /** Set a field-select value with case-insensitive matching against available options. */
    setFieldVal(id, val) {
        const el = document.getElementById(id);
        if (!el) return;
        if (!val) { el.value = ''; return; }
        // Try exact match first
        if ([...el.options].some(o => o.value === val)) {
            el.value = val;
            return;
        }
        // Case-insensitive fallback
        const lowerVal = val.toLowerCase();
        const match = [...el.options].find(o => o.value.toLowerCase() === lowerVal);
        if (match) {
            el.value = match.value;
        } else {
            el.value = val; // keep the original even if no match
        }
    }

    /** Resolve a field name against available fields (case-insensitive). */
    _resolveFieldName(name) {
        if (!name || !this.fields.length) return name;
        // Exact match
        if (this.fields.includes(name)) return name;
        // Case-insensitive
        const lower = name.toLowerCase();
        return this.fields.find(f => f.toLowerCase() === lower) || name;
    }

    collect() {
        if (!this.currentChart) return null;
        return {
            ...this.currentChart,
            title: this.getVal('prop-title'),
            chartType: this.getVal('prop-chart-type'),
            datasetName: this.getVal('prop-dataset'),
            width: parseInt(this.getVal('prop-width')) || 6,
            height: parseInt(this.getVal('prop-height')) || 300,
            mapping: {
                ...this.currentChart.mapping,
                labelField: this._resolveFieldName(this.getVal('prop-label-field')),
                valueField: this._resolveFieldName(this.getVal('prop-value-field')),
                lineValueField: this._resolveFieldName(this.getVal('prop-line-value-field')),
                xField: this._resolveFieldName(this.getVal('prop-x-field')),
                yField: this._resolveFieldName(this.getVal('prop-y-field')),
                rField: this._resolveFieldName(this.getVal('prop-r-field')),
                groupByField: this._resolveFieldName(this.getVal('prop-group-by-field')),
                multiValueFields: this._collectMultiValueFields(),
            },
            aggregation: {
                enabled: this.getVal('prop-agg-enabled', 'checkbox'),
                function: this.getVal('prop-agg-function'),
            },
            rowLimit: parseInt(this.getVal('prop-row-limit')) || 100,
            filterWhere: this.getVal('prop-filter-where') || '',
            dataQuery: '', // Clear cached query so it rebuilds from current mappings, WHERE filter, etc.
            style: {
                ...this.currentChart.style,
                colorPalette: this.getVal('prop-color-palette'),
                showLegend: this.getVal('prop-show-legend', 'checkbox'),
                legendPosition: this.getVal('prop-legend-position'),
                fillArea: this.getVal('prop-fill-area', 'checkbox'),
                showTooltips: this.getVal('prop-show-tooltips', 'checkbox'),
                animated: this.getVal('prop-animated', 'checkbox'),
                titleFontSize: parseInt(this.getVal('prop-title-font-size')) || 14,
                borderRadius: this.getVal('prop-border-radius'),
            },
            customJsonData: this.getVal('prop-custom-json'),
        };
    }

    async apply() {
        const updated = this.collect();
        if (!updated) return;
        this.currentChart = updated;
        if (window.canvasManager) await window.canvasManager.updateChart(updated);
        this.updateFieldHighlights();
    }

    /** Check if a field name corresponds to a numeric data type in the current schema. */
    _isNumericField(fieldName) {
        if (!fieldName || !this._schemaColumns) return false;
        const col = this._schemaColumns.find(c => c.name.toLowerCase() === fieldName.toLowerCase());
        if (!col) return false;
        const t = (col.dataType || '').toLowerCase();
        return t.includes('int') || t.includes('decimal') || t.includes('float') ||
               t.includes('numeric') || t.includes('money') || t.includes('double') || t.includes('real');
    }

    /** Auto-enable aggregation if the value field is numeric, otherwise disable it. */
    _autoAggregation(valueFieldName) {
        const aggCheckbox = document.getElementById('prop-agg-enabled');
        if (!aggCheckbox) return;
        if (this._isNumericField(valueFieldName)) {
            aggCheckbox.checked = true;
        } else {
            aggCheckbox.checked = false;
        }
    }

    async datasetChanged() {
        const ds = this.getVal('prop-dataset');
        await this.loadFields(ds);
        if (this.currentChart) {
            this.currentChart.datasetName = ds;
            // Auto-select best label (text/date) and value (numeric) fields for the new table
            const cols = this._schemaColumns || [];
            const tableCols = cols.filter(c => !c.table || c.table.toLowerCase() === (ds || '').toLowerCase());
            const effective = tableCols.length > 0 ? tableCols : cols;
            const isNumeric = (dt) => {
                const t = (dt || '').toLowerCase();
                return t.includes('int') || t.includes('decimal') || t.includes('float') ||
                       t.includes('numeric') || t.includes('money') || t.includes('double') || t.includes('real');
            };
            const textCol = effective.find(c => !isNumeric(c.dataType) && !c.isPrimaryKey);
            const numericCol = effective.find(c => isNumeric(c.dataType));
            const newLabel = textCol?.name || effective[0]?.name || '';
            const newValue = numericCol?.name || (effective.length > 1 ? effective[1].name : effective[0]?.name) || '';
            if (!this.currentChart.mapping) this.currentChart.mapping = {};
            this.currentChart.mapping.labelField = newLabel;
            this.currentChart.mapping.valueField = newValue;
            this.setFieldVal('prop-label-field', newLabel);
            this.setFieldVal('prop-value-field', newValue);
            // Auto-toggle aggregation based on whether value field is numeric
            this._autoAggregation(newValue);
        }
        await this.apply();
        this.updateFieldHighlights();
    }

    bindAutoApply() {
        const DEBOUNCE_DELAY_MS = 300;
        const form = document.getElementById('properties-form');
        if (!form) return;

        // Remove previous listeners before re-binding
        if (this._autoApplyHandler) {
            form.removeEventListener('change', this._autoApplyHandler);
        }
        if (this._autoApplyInputHandler) {
            form.removeEventListener('input', this._autoApplyInputHandler);
        }

        let debounceTimer = null;
        const applyNow = () => this.apply();
        const applyDebounced = () => {
            clearTimeout(debounceTimer);
            debounceTimer = setTimeout(applyNow, DEBOUNCE_DELAY_MS);
        };

        // Immediate apply for select, checkbox, color inputs on 'change'
        this._autoApplyHandler = (e) => {
            const el = e.target;
            if (!el.matches('select, input[type="checkbox"], input[type="color"]')) return;
            // Skip dataset select — datasetChanged() handles its own apply after async field loading
            if (el.id === 'prop-dataset') return;
            // Auto-toggle aggregation when value field changes
            if (el.id === 'prop-value-field') {
                this._autoAggregation(el.value);
            }
            applyNow();
        };

        // Debounced apply for text and number inputs on 'input'
        this._autoApplyInputHandler = (e) => {
            const el = e.target;
            if (!el.matches('input[type="text"], input[type="number"], textarea')) return;
            applyDebounced();
        };

        form.addEventListener('change', this._autoApplyHandler);
        form.addEventListener('input', this._autoApplyInputHandler);

        // Show/hide type-specific fields when chart type changes
        const chartTypeEl = document.getElementById('prop-chart-type');
        if (chartTypeEl) {
            if (this._chartTypeChangeHandler) {
                chartTypeEl.removeEventListener('change', this._chartTypeChangeHandler);
            }
            this._chartTypeChangeHandler = () => this.updateTypeSpecificFields(chartTypeEl.value);
            chartTypeEl.addEventListener('change', this._chartTypeChangeHandler);
        }
    }

    updateTypeSpecificFields(chartType) {
        document.querySelectorAll('.chart-type-field').forEach(el => {
            const types = (el.dataset.chartTypes || '').split(',').map(t => t.trim());
            el.style.display = types.includes(chartType) ? '' : 'none';
        });
    }

    renderDataFields() {
        const container = document.getElementById('data-fields-list');
        if (!container) return;
        const cols = this._schemaColumns || this.fields.map(f => ({ name: f, dataType: '', table: '', isPrimaryKey: false }));
        if (cols.length === 0) {
            container.innerHTML = '<div class="text-muted small p-2">No fields available</div>';
            return;
        }
        const typeIcon = (dt) => {
            const t = (dt || '').toLowerCase();
            if (t.includes('int') || t.includes('decimal') || t.includes('float') || t.includes('numeric') || t.includes('money') || t.includes('double') || t.includes('real'))
                return 'bi-123';
            if (t.includes('date') || t.includes('time'))
                return 'bi-calendar-event';
            if (t.includes('bit') || t.includes('bool'))
                return 'bi-toggle-on';
            return 'bi-fonts';
        };

        const renderFieldRows = (columns) => columns.map(c =>
            `<tr class="data-field-row" draggable="true" data-field="${escapeHtml(c.name)}">
                <td class="data-field-check"></td>
                <td class="data-field-name"><i class="bi ${c.isPrimaryKey ? 'bi-key-fill text-warning' : typeIcon(c.dataType)} me-1" style="font-size:0.7rem"></i>${escapeHtml(c.name)}</td>
                <td class="data-field-type">${escapeHtml(c.dataType || '—')}</td>
            </tr>`
        ).join('');

        // Grouped by table when schema tables are available
        if (this._schemaTables && this._schemaTables.length > 0) {
            container.innerHTML = this._schemaTables.map((tbl, idx) => `
                <div class="data-fields-group" data-table="${escapeHtml(tbl.name)}">
                    <div class="data-fields-group-header" data-group-idx="${idx}">
                        <i class="bi bi-chevron-down data-fields-group-chevron"></i>
                        <i class="bi bi-table me-1" style="font-size:0.7rem;color:var(--cp-primary)"></i>
                        <span class="data-fields-group-name">${escapeHtml(tbl.name)}</span>
                        <span class="data-fields-group-count">${tbl.columns.length}</span>
                    </div>
                    <table class="data-fields-table">
                        <thead><tr><th></th><th>Field</th><th>Type</th></tr></thead>
                        <tbody>${renderFieldRows(tbl.columns)}</tbody>
                    </table>
                </div>`).join('');
        } else {
            // Flat list (no table grouping)
            container.innerHTML = `
                <table class="data-fields-table">
                    <thead><tr><th></th><th>Field</th><th>Type</th></tr></thead>
                    <tbody>${renderFieldRows(cols)}</tbody>
                </table>`;
        }

        // Collapsible table groups
        container.querySelectorAll('.data-fields-group-header').forEach(header => {
            header.addEventListener('click', () => {
                const group = header.closest('.data-fields-group');
                group.classList.toggle('collapsed');
                const chevron = header.querySelector('.data-fields-group-chevron');
                if (chevron) {
                    chevron.className = group.classList.contains('collapsed')
                        ? 'bi bi-chevron-right data-fields-group-chevron'
                        : 'bi bi-chevron-down data-fields-group-chevron';
                }
            });
        });

        // Drag support
        container.querySelectorAll('.data-field-row').forEach(item => {
            item.addEventListener('dragstart', (e) => {
                e.dataTransfer.setData('fieldName', item.dataset.field);
                e.dataTransfer.effectAllowed = 'copy';
                item.classList.add('dragging');
            });
            item.addEventListener('dragend', () => item.classList.remove('dragging'));
        });
    }

    initFieldDropTargets() {
        document.querySelectorAll('.field-select').forEach(select => {
            select.addEventListener('dragover', (e) => { e.preventDefault(); select.style.outline = '2px solid var(--primary)'; });
            select.addEventListener('dragleave', () => { select.style.outline = ''; });
            select.addEventListener('drop', (e) => {
                e.preventDefault();
                select.style.outline = '';
                const fieldName = e.dataTransfer.getData('fieldName');
                if (fieldName && [...select.options].some(o => o.value === fieldName)) {
                    select.value = fieldName;
                    select.dispatchEvent(new Event('change', { bubbles: true }));
                }
            });
        });
    }

    // Initialize collapsible property sections (Data and Style collapsed by default)
    initCollapsibleSections() {
        document.querySelectorAll('.prop-section').forEach(section => {
            const title = section.querySelector('.prop-section-title');
            if (!title) return;
            const sectionName = title.textContent.trim().toLowerCase();
            // Collapse Data and Style sections by default
            if (sectionName !== 'basic') {
                section.classList.add('collapsed');
            }
            // Add chevron icon if not present
            if (!title.querySelector('.prop-chevron')) {
                const chevron = document.createElement('i');
                chevron.className = 'bi bi-chevron-down prop-chevron ms-auto';
                title.style.display = 'flex';
                title.style.alignItems = 'center';
                title.style.cursor = 'pointer';
                title.appendChild(chevron);
            }
            title.addEventListener('click', () => {
                section.classList.toggle('collapsed');
                const chevron = title.querySelector('.prop-chevron');
                if (chevron) {
                    chevron.className = section.classList.contains('collapsed')
                        ? 'bi bi-chevron-right prop-chevron ms-auto'
                        : 'bi bi-chevron-down prop-chevron ms-auto';
                }
            });
        });
    }

    // Initialize resizable data-fields panel
    initDataFieldResize() {
        const panel = document.querySelector('.data-fields-tab-content');
        const scroll = document.getElementById('data-fields-list');
        if (!panel || !scroll) return;

        let isResizing = false;
        let startY = 0;
        let startH = 0;

        scroll.style.cursor = '';

        // Create resize handle
        let handle = panel.querySelector('.data-fields-resize-handle');
        if (!handle) {
            handle = document.createElement('div');
            handle.className = 'data-fields-resize-handle';
            handle.title = 'Drag to resize';
            panel.insertBefore(handle, scroll);
        }

        handle.addEventListener('mousedown', (e) => {
            isResizing = true;
            startY = e.clientY;
            startH = scroll.offsetHeight;
            document.body.style.cursor = 'ns-resize';
            e.preventDefault();
        });

        document.addEventListener('mousemove', (e) => {
            if (!isResizing) return;
            const delta = e.clientY - startY;
            const newH = Math.max(80, Math.min(600, startH + delta));
            scroll.style.height = newH + 'px';
        });

        document.addEventListener('mouseup', () => {
            if (isResizing) {
                isResizing = false;
                document.body.style.cursor = '';
            }
        });
    }

    // Tab switching replaced by collapsible sections — both visible at once
    switchTab(tab) {
        // Legacy no-op: both sections are now always visible as collapsible panels
        this.activeTab = tab;
    }

    initTabs() {
        // Initialize collapsible section toggles (replacing old tab bar)
        const initSection = (toggleId, bodySelector) => {
            const toggle = document.getElementById(toggleId);
            if (!toggle) return;
            const section = toggle.closest('.right-panel-section');
            const body = section ? section.querySelector('.right-panel-section-body') : null;
            if (!section || !body) return;
            toggle.addEventListener('click', () => {
                section.classList.toggle('collapsed');
                const chevron = toggle.querySelector('.right-panel-chevron');
                if (chevron) {
                    chevron.className = section.classList.contains('collapsed')
                        ? 'bi bi-chevron-right right-panel-chevron'
                        : 'bi bi-chevron-down right-panel-chevron';
                }
            });
        };
        initSection('data-fields-toggle');
        initSection('properties-toggle');
    }

    /** Update checkbox highlights in Data Fields list to show which fields are currently mapped. */
    updateFieldHighlights() {
        const container = document.getElementById('data-fields-list');
        if (!container) return;
        // Collect all currently-mapped field names (case-insensitive)
        const mapped = new Set();
        if (this.currentChart?.mapping) {
            const m = this.currentChart.mapping;
            [m.labelField, m.valueField, m.lineValueField, m.xField, m.yField, m.rField, m.groupByField]
                .filter(Boolean)
                .forEach(f => mapped.add(f.toLowerCase()));
            // Include additional value fields
            (m.multiValueFields || []).filter(Boolean).forEach(f => mapped.add(f.toLowerCase()));
        }
        container.querySelectorAll('.data-field-row').forEach(row => {
            const fieldName = (row.dataset.field || '').toLowerCase();
            const isSelected = mapped.has(fieldName);
            row.classList.toggle('field-selected', isSelected);
            const checkCell = row.querySelector('.data-field-check');
            if (checkCell) {
                checkCell.innerHTML = isSelected ? '<i class="bi bi-check-circle-fill"></i>' : '';
            }
        });
        // Update badge count
        const badge = document.getElementById('data-fields-count');
        if (badge) {
            badge.textContent = mapped.size > 0 ? mapped.size : '';
        }
    }

    // Shape property methods
    showShapeProps(show) {
        const shapeSection = document.getElementById('shape-props-section');
        const chartSections = document.querySelectorAll('.chart-only-section');
        if (shapeSection) shapeSection.style.display = show ? 'block' : 'none';
        chartSections.forEach(s => s.style.display = show ? 'none' : '');
    }

    populateShapeProps(chartDef) {
        const p = chartDef.shapeProps || (window.ShapeManager ? ShapeManager.getDefaultShapeProps(chartDef.chartType) : {});
        this.setVal('prop-title', chartDef.title);
        this.setVal('prop-width', chartDef.width);
        this.setVal('prop-height', chartDef.height);
        this.setVal('prop-shape-fill', p.fillColor || '#5B9BD5');
        this.setVal('prop-shape-stroke', p.strokeColor || '#3A7BBF');
        this.setVal('prop-shape-stroke-width', p.strokeWidth ?? 2);
        this.setVal('prop-shape-opacity', p.opacity ?? 1);
        this.setVal('prop-shape-text', p.text || '');
        this.setVal('prop-shape-font-size', p.fontSize || 16);
        this.setVal('prop-shape-font-color', p.fontColor || '#1E2D3D');
        this.setVal('prop-shape-text-align', p.textAlign || 'center');
        this.setVal('prop-shape-corner-radius', p.cornerRadius || 0);

        // Show/hide textbox-specific fields
        const textFields = document.querySelectorAll('.shape-textbox-field');
        const isTextbox = chartDef.chartType === 'shape-textbox';
        textFields.forEach(el => el.style.display = isTextbox ? '' : 'none');
    }

    collectShapeProps() {
        if (!this.currentChart) return null;
        return {
            ...this.currentChart,
            title: this.getVal('prop-title'),
            width: parseInt(this.getVal('prop-width')) || 3,
            height: parseInt(this.getVal('prop-height')) || 180,
            shapeProps: {
                ...this.currentChart.shapeProps,
                fillColor: this.getVal('prop-shape-fill'),
                strokeColor: this.getVal('prop-shape-stroke'),
                strokeWidth: parseInt(this.getVal('prop-shape-stroke-width')) || 2,
                opacity: parseFloat(this.getVal('prop-shape-opacity')) || 1,
                text: this.getVal('prop-shape-text'),
                fontSize: parseInt(this.getVal('prop-shape-font-size')) || 16,
                fontColor: this.getVal('prop-shape-font-color'),
                textAlign: this.getVal('prop-shape-text-align'),
                cornerRadius: parseInt(this.getVal('prop-shape-corner-radius')) || 0,
            }
        };
    }

    bindShapeAutoApply() {
        const form = document.getElementById('properties-form');
        if (!form) return;
        if (this._shapeApplyHandler) form.removeEventListener('change', this._shapeApplyHandler);
        if (this._shapeInputHandler) form.removeEventListener('input', this._shapeInputHandler);

        let timer;
        const applyShape = () => {
            const updated = this.collectShapeProps();
            if (!updated) return;
            this.currentChart = updated;
            if (window.canvasManager) window.canvasManager.updateChart(updated);
        };

        this._shapeApplyHandler = (e) => {
            if (e.target.matches('select, input[type="checkbox"], input[type="color"]')) applyShape();
        };
        this._shapeInputHandler = (e) => {
            if (e.target.matches('input[type="text"], input[type="number"], input[type="range"], textarea')) {
                clearTimeout(timer);
                timer = setTimeout(applyShape, 300);
            }
        };
        form.addEventListener('change', this._shapeApplyHandler);
        form.addEventListener('input', this._shapeInputHandler);
    }
}

    /** Render the additional value field selects from an array of field names. */
    _renderMultiValueFields(fields) {
        const container = document.getElementById('multi-value-fields-container');
        if (!container) return;
        container.innerHTML = '';
        (fields || []).forEach(f => this._addMultiValueFieldSelect(f));
    }

    /** Add a single additional value field select to the container. */
    _addMultiValueFieldSelect(selectedValue) {
        const container = document.getElementById('multi-value-fields-container');
        if (!container) return;
        const wrapper = document.createElement('div');
        wrapper.className = 'd-flex align-items-center gap-1 mb-1 multi-value-field-row';
        const sel = document.createElement('select');
        sel.className = 'form-select form-select-sm field-select multi-value-field-select';
        sel.style.fontSize = '0.75rem';
        // Populate options from current fields
        sel.innerHTML = '<option value="">-- none --</option>' +
            this.fields.map(f => '<option value="' + (typeof escapeHtml === 'function' ? escapeHtml(f) : f) + '">' + (typeof escapeHtml === 'function' ? escapeHtml(f) : f) + '</option>').join('');
        if (selectedValue) {
            this.setFieldValOnEl(sel, selectedValue);
        }
        const removeBtn = document.createElement('button');
        removeBtn.type = 'button';
        removeBtn.className = 'btn btn-xs';
        removeBtn.style.cssText = 'font-size:0.68rem;padding:1px 4px;border:1px solid #e2e8f0;border-radius:4px;color:#ef4444;background:#fff;';
        removeBtn.title = 'Remove field';
        removeBtn.innerHTML = '<i class="bi bi-x"></i>';
        removeBtn.addEventListener('click', () => {
            wrapper.remove();
            this.apply();
        });
        sel.addEventListener('change', () => this.apply());
        wrapper.appendChild(sel);
        wrapper.appendChild(removeBtn);
        container.appendChild(wrapper);
    }

    /** Set value on a specific select element with case-insensitive matching. */
    setFieldValOnEl(sel, val) {
        if (!sel || !val) return;
        if ([...sel.options].some(o => o.value === val)) { sel.value = val; return; }
        const lower = val.toLowerCase();
        const match = [...sel.options].find(o => o.value.toLowerCase() === lower);
        if (match) sel.value = match.value;
        else sel.value = val;
    }

    /** Collect all additional value field names from the UI. */
    _collectMultiValueFields() {
        const container = document.getElementById('multi-value-fields-container');
        if (!container) return [];
        const values = [];
        container.querySelectorAll('.multi-value-field-select').forEach(sel => {
            if (sel.value) values.push(this._resolveFieldName(sel.value));
        });
        return values;
    }

    /** Wire the "Add" button for multi-value fields. */
    _wireMultiValueBtn() {
        const btn = document.getElementById('add-value-field-btn');
        if (!btn) return;
        // Replace to remove old listeners
        const newBtn = btn.cloneNode(true);
        btn.parentNode.replaceChild(newBtn, btn);
        newBtn.addEventListener('click', () => {
            this._addMultiValueFieldSelect('');
        });
    }
}

window.propertiesPanel = new PropertiesPanel();

// Init collapsible sections, data field resize, and tabs after DOM ready
document.addEventListener('DOMContentLoaded', function() {
    propertiesPanel.initCollapsibleSections();
    propertiesPanel.initDataFieldResize();
    propertiesPanel.initTabs();
});
