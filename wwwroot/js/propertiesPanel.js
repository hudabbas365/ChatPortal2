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
            this._wireAggMenu();
            this._renderTableFields(chartDef.mapping?.tableFields || []);
            this.updateTypeSpecificFields(chartDef.chartType);
            this.initFieldDropTargets();
            this.updateFieldHighlights();
            this.populateNavigationProps(chartDef);
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
                    // Populate the dataset/table select from real schema tables
                    this._populateDatasetSelect(tables.map(t => t.name || ''), datasetName);
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
        // No datasource — leave fields empty (no sample data fallback)
        this.fields = [];
        this._schemaColumns = [];
        this._schemaTables = null;
        this.updateFieldSelects(datasetName);
        this.renderDataFields();
    }

    /** Populate the prop-dataset select with real table names from the datasource schema. */
    _populateDatasetSelect(tableNames, currentValue) {
        const sel = document.getElementById('prop-dataset');
        if (!sel) return;
        // Fallback to globally-loaded table names if schema returned empty
        let names = (tableNames && tableNames.filter(n => n).length > 0)
            ? tableNames.filter(n => n)
            : (window._realTableNames || []);
        const existingValue = currentValue || sel.value || '';
        sel.innerHTML = '<option value="">-- Select table --</option>' +
            names.map(n => `<option value="${typeof escapeHtml === 'function' ? escapeHtml(n) : n}">${typeof escapeHtml === 'function' ? escapeHtml(n) : n}</option>`).join('');
        // Restore previously selected value if it exists in the new list
        if (existingValue && names.includes(existingValue)) {
            sel.value = existingValue;
        } else if (existingValue) {
            const lower = existingValue.toLowerCase();
            // Try case-insensitive match
            const match = names.find(n => n.toLowerCase() === lower);
            if (match) {
                sel.value = match;
            } else {
                // Try matching without schema prefix (e.g. 'Sales' matches 'dbo.Sales')
                const suffixMatch = names.find(n => n.toLowerCase().endsWith('.' + lower) || lower.endsWith('.' + n.toLowerCase()));
                if (suffixMatch) sel.value = suffixMatch;
            }
        }
    }


    updateFieldSelects(filterTable) {
        let fieldList = this.fields;
        if (filterTable && this._schemaColumns && this._schemaColumns.length > 0) {
            const ft = filterTable.toLowerCase();
            // Match exact table name or schema-prefixed name (e.g. 'dbo.Sales' matches 'Sales')
            const tableFields = this._schemaColumns
                .filter(c => {
                    if (!c.table) return false;
                    const ct = c.table.toLowerCase();
                    if (ct === ft) return true;
                    // Support schema.table matching: 'dbo.sales' ends with '.sales'
                    if (ct.endsWith('.' + ft)) return true;
                    // Also match if filterTable has schema prefix but stored without
                    if (ft.endsWith('.' + ct)) return true;
                    return false;
                })
                .map(c => c.name)
                .filter((v, i, a) => a.indexOf(v) === i); // deduplicate
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
        // Per-field aggregation for primary value field
        const primaryAgg = c.mapping?.valueFieldAgg || c.aggregation?.function || (c.aggregation?.enabled ? 'SUM' : 'SUM');
        this.setVal('prop-value-field-agg', primaryAgg);
        // Legacy compat
        this.setVal('prop-agg-enabled', primaryAgg !== 'None', 'checkbox');
        this.setVal('prop-agg-function', primaryAgg);
        this._updateAggVisibility(c.chartType);
        this.setVal('prop-row-limit', c.rowLimit || 15);
        // Populate condition builder from filterWhere
        this._populateConditions(c.filterWhere || '');
        // Show existing dataQuery in SQL area
        const sqlArea = document.getElementById('pp-sql-area');
        if (sqlArea) sqlArea.value = c.dataQuery || '';
        this.setVal('prop-show-legend', c.style?.showLegend !== false, 'checkbox');
        this.setVal('prop-legend-position', c.style?.legendPosition || 'top');
        this.setVal('prop-fill-area', c.style?.fillArea || false, 'checkbox');
        this.setVal('prop-show-tooltips', c.style?.showTooltips !== false, 'checkbox');
        this.setVal('prop-animated', c.style?.animated !== false, 'checkbox');
        this.setVal('prop-title-font-size', c.style?.titleFontSize || 14);
        this.setVal('prop-border-radius', c.style?.borderRadius || '4');
        // Card visual customization
        this.setVal('prop-box-shadow', c.style?.boxShadow || 'none');
        this.setVal('prop-card-bg-color', c.style?.cardBackgroundColor || '#ffffff');
        this.setVal('prop-font-color', c.style?.fontColor || '#1E2D3D');
        this.setVal('prop-kpi-halign', c.style?.kpiHAlign || 'center');
        this.setVal('prop-kpi-valign', c.style?.kpiVAlign || 'middle');
        this.setVal('prop-bg-image', c.style?.backgroundImage || '');
        this.setVal('prop-icon-image', c.style?.iconImage || '');
        this._updateCardStylePreviews();
        this._bindCardStyleUploads();
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

    /** Read an image file as a base64 data URL; rejects if larger than maxBytes. */
    _fileToDataUrl(file, maxBytes = 500 * 1024) {
        return new Promise((resolve, reject) => {
            if (!file) return reject(new Error('No file'));
            if (file.size > maxBytes) {
                return reject(new Error(`Image is too large (max ${Math.round(maxBytes/1024)} KB).`));
            }
            const reader = new FileReader();
            reader.onload = () => resolve(String(reader.result || ''));
            reader.onerror = () => reject(reader.error || new Error('Read failed'));
            reader.readAsDataURL(file);
        });
    }

    _updateCardStylePreviews() {
        const bgImg = this.getVal('prop-bg-image') || '';
        const bgPreview = document.getElementById('prop-bg-image-preview');
        if (bgPreview) {
            if (bgImg) {
                bgPreview.style.display = 'block';
                bgPreview.style.backgroundImage = `url("${bgImg}")`;
            } else {
                bgPreview.style.display = 'none';
                bgPreview.style.backgroundImage = '';
            }
        }
        const iconImg = this.getVal('prop-icon-image') || '';
        const iconPreview = document.getElementById('prop-icon-image-preview');
        if (iconPreview) {
            if (iconImg) {
                iconPreview.style.display = 'inline-block';
                iconPreview.src = iconImg;
            } else {
                iconPreview.style.display = 'none';
                iconPreview.removeAttribute('src');
            }
        }
    }

    _bindCardStyleUploads() {
        if (this._cardStyleUploadsBound) return;
        this._cardStyleUploadsBound = true;

        const wire = (fileId, hiddenId, onChange) => {
            const fileEl = document.getElementById(fileId);
            if (!fileEl) return;
            fileEl.addEventListener('change', async (e) => {
                const file = e.target.files && e.target.files[0];
                if (!file) return;
                try {
                    const dataUrl = await this._fileToDataUrl(file);
                    this.setVal(hiddenId, dataUrl);
                    this._updateCardStylePreviews();
                    onChange && onChange();
                    this.apply();
                } catch (err) {
                    alert(err.message || 'Unable to read image.');
                    fileEl.value = '';
                }
            });
        };

        wire('prop-bg-image-file', 'prop-bg-image');
        wire('prop-icon-image-file', 'prop-icon-image');

        const clearBgImg = document.getElementById('prop-bg-image-clear');
        if (clearBgImg) clearBgImg.addEventListener('click', () => {
            this.setVal('prop-bg-image', '');
            const fileEl = document.getElementById('prop-bg-image-file');
            if (fileEl) fileEl.value = '';
            this._updateCardStylePreviews();
            this.apply();
        });
        const clearIconImg = document.getElementById('prop-icon-image-clear');
        if (clearIconImg) clearIconImg.addEventListener('click', () => {
            this.setVal('prop-icon-image', '');
            const fileEl = document.getElementById('prop-icon-image-file');
            if (fileEl) fileEl.value = '';
            this._updateCardStylePreviews();
            this.apply();
        });
        const clearBgColor = document.getElementById('prop-card-bg-color-clear');
        if (clearBgColor) clearBgColor.addEventListener('click', () => {
            this.setVal('prop-card-bg-color', '');
            this.apply();
        });
        const clearFontColor = document.getElementById('prop-font-color-clear');
        if (clearFontColor) clearFontColor.addEventListener('click', () => {
            this.setVal('prop-font-color', '');
            this.apply();
        });
    }

    setVal(id, val, type = 'value') {
        const el = document.getElementById(id);
        if (!el) return;
        if (type === 'checkbox') { el.checked = !!val; return; }
        // <input type="color"> emits a harmless-but-noisy browser warning when
        // assigned an empty string ("The specified value \"\" does not conform
        // to the required format. The format is \"#rrggbb\""). Fall back to
        // black (the browser default) when clearing, and suppress the warning.
        const v = val ?? '';
        if (el.type === 'color' && (!v || !/^#[0-9a-fA-F]{6}$/.test(v))) {
            el.value = '#000000';
        } else {
            el.value = v;
        }
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
                valueFieldAgg: this.getVal('prop-value-field-agg') || 'SUM',
                multiValueFields: this._collectMultiValueFields(),
                tableFields: this._collectTableFields(),
            },
            aggregation: {
                enabled: (this.getVal('prop-value-field-agg') || 'None') !== 'None',
                function: this.getVal('prop-value-field-agg') || 'None',
            },
            rowLimit: parseInt(this.getVal('prop-row-limit')) || 15,
            filterWhere: this._collectConditionsSQL(),
            // Use SQL from the sql area if provided; otherwise clear so it rebuilds from mappings
            dataQuery: (document.getElementById('pp-sql-area')?.value.trim()) || '',
            style: {
                ...this.currentChart.style,
                showLegend: this.getVal('prop-show-legend', 'checkbox'),
                legendPosition: this.getVal('prop-legend-position'),
                fillArea: this.getVal('prop-fill-area', 'checkbox'),
                showTooltips: this.getVal('prop-show-tooltips', 'checkbox'),
                animated: this.getVal('prop-animated', 'checkbox'),
                titleFontSize: parseInt(this.getVal('prop-title-font-size')) || 14,
                borderRadius: this.getVal('prop-border-radius'),
                boxShadow: this.getVal('prop-box-shadow') || 'none',
                cardBackgroundColor: this.getVal('prop-card-bg-color') || '',
                fontColor: this.getVal('prop-font-color') || '',
                kpiHAlign: this.getVal('prop-kpi-halign') || 'center',
                kpiVAlign: this.getVal('prop-kpi-valign') || 'middle',
                backgroundImage: this.getVal('prop-bg-image') || '',
                iconImage: this.getVal('prop-icon-image') || '',
            },
            customJsonData: this.getVal('prop-custom-json'),
            navigation: this.currentChart.chartType === 'navigation'
                ? this.collectNavigationProps()
                : (this.currentChart.navigation || null),
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
        const aggSel = document.getElementById('prop-value-field-agg');
        if (!aggSel) return;
        if (this._isNumericField(valueFieldName)) {
            if (aggSel.value === 'None') aggSel.value = 'SUM';
        } else {
            aggSel.value = 'None';
        }
    }

    async datasetChanged() {
        const ds = this.getVal('prop-dataset');
        await this.loadFields(ds);
        if (this.currentChart) {
            this.currentChart.datasetName = ds;
            // Auto-select best label (text/date) and value (numeric) fields for the new table
            const cols = this._schemaColumns || [];
            const dsLower = (ds || '').toLowerCase();
            const tableCols = cols.filter(c => {
                if (!c.table) return true;
                const ct = c.table.toLowerCase();
                return ct === dsLower || ct.endsWith('.' + dsLower) || dsLower.endsWith('.' + ct);
            });
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
            if (el.id === 'prop-nav-target') {
                this._toggleNavigationTargetUrl();
            }
            applyNow();
        };

        // Debounced apply for text and number inputs on 'input'
        this._autoApplyInputHandler = (e) => {
            const el = e.target;
            if (!el.matches('input[type="text"], input[type="number"], textarea')) return;
            // Skip the SQL area — user edits it manually; apply only on explicit action
            if (el.id === 'pp-sql-area') return;
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
            this._chartTypeChangeHandler = () => {
                this.updateTypeSpecificFields(chartTypeEl.value);
            };
            chartTypeEl.addEventListener('change', this._chartTypeChangeHandler);
        }

        // Wire "Add condition" button
        this._wireConditionBuilder();

        // Wire AI Generate SQL button
        this._wireAISqlBtn();
    }

    updateTypeSpecificFields(chartType) {
        document.querySelectorAll('.chart-type-field').forEach(el => {
            const types = (el.dataset.chartTypes || '').split(',').map(t => t.trim());
            el.style.display = types.includes(chartType) ? '' : 'none';
        });
        const navSection = document.getElementById('navigation-props-section');
        if (navSection) navSection.style.display = chartType === 'navigation' ? '' : 'none';
        document.querySelectorAll('.chart-only-section').forEach(el => {
            if (chartType === 'navigation') {
                el.style.display = 'none';
            } else if (window.ShapeManager && this.currentChart && ShapeManager.isShape(this.currentChart.chartType)) {
                el.style.display = 'none';
            } else {
                el.style.display = '';
            }
        });
        this._updateAggVisibility(chartType);
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
            // Remove old listeners by replacing with clone
            if (select._fieldDropWired) {
                const clone = select.cloneNode(true);
                select.parentNode.replaceChild(clone, select);
                select = clone;
            }
            select._fieldDropWired = true;
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

    // Initialize collapsible property sections with sessionStorage persistence
    initCollapsibleSections() {
        const STORAGE_KEY = 'cp_prop_sections';
        let savedState = {};
        try { savedState = JSON.parse(sessionStorage.getItem(STORAGE_KEY) || '{}'); } catch {}

        document.querySelectorAll('.prop-section').forEach(section => {
            const title = section.querySelector('.prop-section-title');
            if (!title) return;
            const sectionName = title.dataset.section || title.textContent.replace(/[\n\t]/g,'').trim().toLowerCase().replace(/\s+/g,'_');

            // Determine initial collapsed state: use saved state, fallback to default (Basic+Data open, Style collapsed)
            let isCollapsed;
            if (sectionName in savedState) {
                isCollapsed = savedState[sectionName];
            } else {
                const nameText = title.textContent.trim().toLowerCase();
                isCollapsed = !(nameText.startsWith('basic') || nameText.startsWith('data'));
            }

            // Apply state
            if (isCollapsed) {
                section.classList.add('collapsed');
            } else {
                section.classList.remove('collapsed');
            }

            // Add chevron icon if not present
            if (!title.querySelector('.prop-chevron')) {
                const chevron = document.createElement('i');
                chevron.className = isCollapsed
                    ? 'bi bi-chevron-right prop-chevron ms-auto'
                    : 'bi bi-chevron-down prop-chevron ms-auto';
                title.style.display = 'flex';
                title.style.alignItems = 'center';
                title.style.cursor = 'pointer';
                title.appendChild(chevron);
            } else {
                const chevron = title.querySelector('.prop-chevron');
                chevron.className = isCollapsed
                    ? 'bi bi-chevron-right prop-chevron ms-auto'
                    : 'bi bi-chevron-down prop-chevron ms-auto';
            }

            title.addEventListener('click', () => {
                section.classList.toggle('collapsed');
                const collapsed = section.classList.contains('collapsed');
                const chevron = title.querySelector('.prop-chevron');
                if (chevron) {
                    chevron.className = collapsed
                        ? 'bi bi-chevron-right prop-chevron ms-auto'
                        : 'bi bi-chevron-down prop-chevron ms-auto';
                }
                // Persist to sessionStorage
                try {
                    let st = {};
                    try { st = JSON.parse(sessionStorage.getItem(STORAGE_KEY) || '{}'); } catch {}
                    st[sectionName] = collapsed;
                    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(st));
                } catch {}
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
            // Include additional value fields (support string[] and {field,agg}[])
            (m.multiValueFields || []).filter(Boolean).forEach(f => {
                const name = typeof f === 'string' ? f : (f.field || '');
                if (name) mapped.add(name.toLowerCase());
            });
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

    // ── Condition Builder ────────────────────────────────────────────

    /** Add a condition row to the condition builder. */
    _addConditionRow(field, op, val) {
        const container = document.getElementById('pp-conditions');
        if (!container) return;
        const fieldList = this.fields.length > 0 ? [...new Set(this.fields)] : [];
        const fieldOpts = '<option value="">-- field --</option>' +
            fieldList.map(f => `<option value="${typeof escapeHtml === 'function' ? escapeHtml(f) : f}">${typeof escapeHtml === 'function' ? escapeHtml(f) : f}</option>`).join('');
        const ops = ['=','!=','>','<','>=','<=','LIKE','IN','IS NULL','IS NOT NULL'];
        const opOpts = ops.map(o => `<option value="${o}"${o === (op || '=') ? ' selected' : ''}>${o}</option>`).join('');

        const row = document.createElement('div');
        row.className = 'pp-condition-row d-flex gap-1 align-items-center mb-1';

        const fieldSel = document.createElement('select');
        fieldSel.className = 'pp-cond-field form-select form-select-sm';
        fieldSel.style.fontSize = '0.72rem';
        fieldSel.innerHTML = fieldOpts;
        if (field) fieldSel.value = field;

        const opSel = document.createElement('select');
        opSel.className = 'pp-cond-op form-select form-select-sm';
        opSel.style.cssText = 'font-size:0.72rem;max-width:80px;';
        opSel.innerHTML = opOpts;

        const valInput = document.createElement('input');
        valInput.type = 'text';
        valInput.className = 'pp-cond-val form-control form-control-sm';
        valInput.style.fontSize = '0.72rem';
        valInput.placeholder = 'value';
        valInput.value = val || '';

        const removeBtn = document.createElement('button');
        removeBtn.type = 'button';
        removeBtn.className = 'btn btn-xs';
        removeBtn.style.cssText = 'font-size:0.68rem;padding:1px 4px;border:1px solid #e2e8f0;border-radius:4px;color:#ef4444;background:#fff;flex-shrink:0;';
        removeBtn.innerHTML = '<i class="bi bi-x"></i>';
        removeBtn.addEventListener('click', () => { row.remove(); this.apply(); });

        // Hide value input for IS NULL / IS NOT NULL
        const toggleValInput = () => {
            const noVal = ['IS NULL', 'IS NOT NULL'].includes(opSel.value);
            valInput.style.display = noVal ? 'none' : '';
        };
        opSel.addEventListener('change', toggleValInput);
        toggleValInput();

        // Apply on change
        [fieldSel, opSel].forEach(el => el.addEventListener('change', () => this.apply()));
        valInput.addEventListener('input', () => {
            clearTimeout(this._condDebounce);
            this._condDebounce = setTimeout(() => this.apply(), 400);
        });

        row.appendChild(fieldSel);
        row.appendChild(opSel);
        row.appendChild(valInput);
        row.appendChild(removeBtn);
        container.appendChild(row);
    }

    /** Wire the "Add condition" button. */
    _wireConditionBuilder() {
        const btn = document.getElementById('pp-add-condition-btn');
        if (!btn) return;
        const newBtn = btn.cloneNode(true);
        btn.parentNode.replaceChild(newBtn, btn);
        newBtn.addEventListener('click', () => this._addConditionRow('', '=', ''));
    }

    /** Build WHERE clause string from the condition rows. */
    _collectConditionsSQL() {
        const container = document.getElementById('pp-conditions');
        if (!container) return '';
        // Allowed operators to prevent injection via operator field
        const allowedOps = new Set(['=','!=','>','<','>=','<=','LIKE','IN','IS NULL','IS NOT NULL']);
        const parts = [];
        container.querySelectorAll('.pp-condition-row').forEach(row => {
            const field = row.querySelector('.pp-cond-field')?.value || '';
            const op    = row.querySelector('.pp-cond-op')?.value || '=';
            const val   = row.querySelector('.pp-cond-val')?.value || '';
            if (!field) return;
            // Only use fields that are actually in the schema to prevent injection
            const knownField = this.fields.includes(field)
                ? field
                : (this.fields.find(f => f.toLowerCase() === field.toLowerCase()) || null);
            if (!knownField) return;
            // Only allow known operators
            const safeOp = allowedOps.has(op.toUpperCase()) ? op.toUpperCase() : '=';
            const quotedField = '[' + knownField.replace(/\]/g, ']]') + ']';
            if (safeOp === 'IS NULL' || safeOp === 'IS NOT NULL') {
                parts.push(`${quotedField} ${safeOp}`);
            } else if (safeOp === 'IN') {
                // val should be comma-separated; each entry is quoted if not numeric
                const inParts = val.split(',').map(v => {
                    const trimmed = v.trim();
                    const isNum = !isNaN(parseFloat(trimmed)) && isFinite(trimmed) && trimmed !== '';
                    return isNum ? trimmed : `'${trimmed.replace(/'/g, "''")}'`;
                });
                parts.push(`${quotedField} IN (${inParts.join(', ')})`);
            } else if (safeOp === 'LIKE') {
                parts.push(`${quotedField} LIKE '${val.replace(/'/g, "''")}'`);
            } else {
                const isNum = !isNaN(parseFloat(val)) && isFinite(val) && val !== '';
                const quotedVal = isNum ? val : `'${val.replace(/'/g, "''")}'`;
                parts.push(`${quotedField} ${safeOp} ${quotedVal}`);
            }
        });
        return parts.join(' AND ');
    }

    /** Populate condition builder from an existing filterWhere string. */
    _populateConditions(filterWhere) {
        const container = document.getElementById('pp-conditions');
        if (!container) return;
        container.innerHTML = '';
        if (!filterWhere) return;
        // Try to parse simple conditions of the form: [field] op value AND ...
        const condRegex = /\[([^\]]+)\]\s*(IS NULL|IS NOT NULL|LIKE|IN|>=|<=|!=|=|>|<)\s*(?:'([^']*)'|(\S+))?/gi;
        let match;
        let hasMatch = false;
        while ((match = condRegex.exec(filterWhere)) !== null) {
            hasMatch = true;
            const field = match[1];
            const op = match[2].toUpperCase();
            const val = match[3] !== undefined ? match[3] : (match[4] || '');
            this._addConditionRow(field, op, val);
        }
        // Fallback: if parsing failed, show as a single raw condition in a text input
        if (!hasMatch && filterWhere.trim()) {
            const row = document.createElement('div');
            row.className = 'd-flex gap-1 align-items-center mb-1';
            const raw = document.createElement('input');
            raw.type = 'text';
            raw.className = 'form-control form-control-sm pp-cond-raw';
            raw.style.fontSize = '0.72rem';
            raw.placeholder = 'Raw WHERE clause';
            raw.value = filterWhere;
            raw.addEventListener('input', () => {
                clearTimeout(this._condDebounce);
                this._condDebounce = setTimeout(() => this.apply(), 400);
            });
            const removeBtn = document.createElement('button');
            removeBtn.type = 'button';
            removeBtn.className = 'btn btn-xs';
            removeBtn.style.cssText = 'font-size:0.68rem;padding:1px 4px;border:1px solid #e2e8f0;border-radius:4px;color:#ef4444;background:#fff;flex-shrink:0;';
            removeBtn.innerHTML = '<i class="bi bi-x"></i>';
            removeBtn.addEventListener('click', () => { row.remove(); this.apply(); });
            row.appendChild(raw);
            row.appendChild(removeBtn);
            container.appendChild(row);
        }
    }

    // ── AI Generate SQL ──────────────────────────────────────────────

    /** Wire the AI Generate SQL button and the "Use this SQL" apply button. */
    _wireAISqlBtn() {
        const btn = document.getElementById('pp-ai-sql-btn');
        if (btn) {
            const newBtn = btn.cloneNode(true);
            btn.parentNode.replaceChild(newBtn, btn);
            newBtn.addEventListener('click', () => this._aiGenerateSQL());
        }

        const useBtn = document.getElementById('pp-use-sql-btn');
        if (useBtn) {
            const newUseBtn = useBtn.cloneNode(true);
            useBtn.parentNode.replaceChild(newUseBtn, useBtn);
            newUseBtn.addEventListener('click', () => {
                const sqlArea = document.getElementById('pp-sql-area');
                if (!sqlArea || !this.currentChart) return;
                this.currentChart.dataQuery = sqlArea.value.trim();
                this.apply();
            });
        }

        // Phase 30-B4: Fix-with-AI button (visible only when #pp-sql-err-note has content)
        const fixBtn = document.getElementById('pp-fix-sql-btn');
        if (fixBtn) {
            const newFixBtn = fixBtn.cloneNode(true);
            fixBtn.parentNode.replaceChild(newFixBtn, fixBtn);
            newFixBtn.addEventListener('click', () => this._aiFixSQL());
        }
        this._wireFixSqlVisibility();
    }

    /** Phase 30-B4: Observe the SQL error note and toggle the Fix-with-AI button accordingly. */
    _wireFixSqlVisibility() {
        if (this._fixSqlObs) return; // wire once
        const errNote = document.getElementById('pp-sql-err-note');
        const fixBtn  = document.getElementById('pp-fix-sql-btn');
        if (!errNote || !fixBtn) return;
        const sync = () => {
            const visible = errNote.style.display !== 'none' && (errNote.textContent || '').trim().length > 0;
            fixBtn.style.display = visible ? '' : 'none';
        };
        this._fixSqlObs = new MutationObserver(sync);
        this._fixSqlObs.observe(errNote, {
            childList: true, characterData: true, subtree: true,
            attributes: true, attributeFilter: ['style']
        });
        sync();
    }

    /** Phase 30-B4: Ask AI to fix the current SQL using the error message + schema context. */
    async _aiFixSQL() {
        const btn     = document.getElementById('pp-fix-sql-btn');
        const sqlArea = document.getElementById('pp-sql-area');
        const errNote = document.getElementById('pp-sql-err-note');
        if (!sqlArea) return;

        const currentSql = (sqlArea.value || '').trim();
        const errMsg     = (errNote?.textContent || '').trim();
        if (!currentSql) return;

        const tableName = this.getVal('prop-dataset') || '';
        const dsId      = this.currentChart?.datasourceId || window.currentDatasourceId || null;
        const previousSql = currentSql;

        let prompt = `The following SQL query failed to execute:\n\n${currentSql}\n\n`;
        if (errMsg) prompt += `Error message:\n${errMsg}\n\n`;
        if (tableName) prompt += `Target table: [${tableName}].\n`;
        prompt += `Please return a corrected version of the SQL. `;
        prompt += `Use only columns that actually exist in the schema. `;
        prompt += `ALWAYS wrap every column and table name in square brackets. `;
        prompt += `Return ONLY the corrected SQL statement, no explanation.`;

        if (btn) { btn.disabled = true; btn.innerHTML = '<span class="spinner-border spinner-border-sm"></span>'; }

        try {
            const token = localStorage.getItem('cp_token') || '';
            const response = await fetch('/api/chat/send', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer ' + token
                },
                body: JSON.stringify({
                    message      : prompt,
                    workspaceId  : (new URLSearchParams(window.location.search)).get('workspace') || window.currentWorkspaceGuid || null,
                    datasourceId : dsId,
                    userId       : (JSON.parse(localStorage.getItem('cp_user') || 'null') || {}).id || '',
                    reportGuid   : window._currentReportGuid || null,
                    pageIndex    : window.canvasManager?.activePageIndex ?? null,
                    agentId      : window._dashboardWsData?.agentId || null
                })
            });

            if (!response.ok) {
                if (errNote) {
                    errNote.textContent = 'AI fix failed: HTTP ' + response.status;
                    errNote.style.display = '';
                }
                return;
            }

            let fullText = '';
            await window.aiStream.readSseText(response, function (chunk) { fullText += chunk; });

            const sqlMatch = fullText.match(/```(?:sql)?\s*([\s\S]*?)```/i);
            const fixed = (sqlMatch ? sqlMatch[1] : fullText).trim();
            if (fixed) {
                sqlArea.value = fixed;
                if (errNote) { errNote.textContent = ''; errNote.style.display = 'none'; }
            } else {
                sqlArea.value = previousSql;
            }
        } catch (e) {
            sqlArea.value = previousSql;
            if (errNote) {
                errNote.textContent = 'AI fix failed: ' + (e?.message || 'network error');
                errNote.style.display = '';
            }
        } finally {
            if (btn) { btn.disabled = false; btn.innerHTML = '<i class="bi bi-wrench-adjustable me-1"></i>Fix with AI'; }
        }
    }

    /** Call the AI to generate a SQL statement from current field selections. */
    async _aiGenerateSQL() {
        const btn = document.getElementById('pp-ai-sql-btn');
        const sqlArea = document.getElementById('pp-sql-area');
        if (!sqlArea) return;

        // Preserve current SQL so we can restore it on failure
        const previousSql = sqlArea.value;

        const tableName = this.getVal('prop-dataset') || '';
        const labelField = this.getVal('prop-label-field') || '';
        const valueField = this.getVal('prop-value-field') || '';
        const aggFn = this.getVal('prop-value-field-agg') || 'None';
        const aggEnabled = aggFn !== 'None';
        const rowLimit = this.getVal('prop-row-limit') || '15';
        const whereClause = this._collectConditionsSQL();
        const mvFields = this._collectMultiValueFields();

        const dsId = this.currentChart?.datasourceId || window.currentDatasourceId || null;

        // Build a prompt describing what SQL to generate
        let prompt = `Generate a SQL SELECT statement for table [${tableName}].`;
        if (labelField) prompt += ` Label/group by [${labelField}].`;
        if (valueField) {
            if (aggEnabled) {
                prompt += ` Apply ${aggFn}([${valueField}]) as the value.`;
            } else {
                prompt += ` Select [${valueField}] as the value.`;
            }
        }
        if (mvFields.length > 0) {
            const mvDescriptions = mvFields.map(f => {
                const name = typeof f === 'object' ? f.field : f;
                const agg = typeof f === 'object' ? f.agg : 'SUM';
                return agg !== 'None' ? `${agg}([${name}])` : `[${name}]`;
            });
            prompt += ` Also include these value fields: ${mvDescriptions.join(', ')}.`;
        }
        if (whereClause) prompt += ` Add WHERE clause: ${whereClause}.`;
        prompt += ` ALWAYS wrap every column name in square brackets (e.g., [Database Version], [ModifiedDate]).`;
        prompt += ` Limit to ${rowLimit} rows. Return only the SQL statement, no explanation.`;

        // Show spinner on button
        if (btn) { btn.disabled = true; btn.innerHTML = '<span class="spinner-border spinner-border-sm"></span>'; }
        sqlArea.value = '-- Generating SQL...';

        try {
            const token = localStorage.getItem('cp_token') || '';
            const response = await fetch('/api/chat/send', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer ' + token
                },
                body: JSON.stringify({
                    message      : prompt,
                    workspaceId  : (new URLSearchParams(window.location.search)).get('workspace') || window.currentWorkspaceGuid || null,
                    datasourceId : dsId,
                    userId       : (JSON.parse(localStorage.getItem('cp_user') || 'null') || {}).id || '',
                    reportGuid   : window._currentReportGuid || null,
                    pageIndex    : window.canvasManager?.activePageIndex ?? null,
                    agentId      : window._dashboardWsData?.agentId || null
                })
            });

            if (!response.ok) {
                const errText = await response.text().catch(function () { return ''; });
                const errMsg = '-- AI error ' + response.status + (errText ? ': ' + errText.substring(0, 100) : '') + '. Please type SQL manually.';
                sqlArea.value = errMsg;
                return;
            }

            let fullText = '';
            await window.aiStream.readSseText(response, function (chunk) {
                fullText += chunk;
            });

            // Extract SQL from response (strip markdown code fences if present)
            const sqlMatch = fullText.match(/```(?:sql)?\s*([\s\S]*?)```/i);
            const sql = (sqlMatch ? sqlMatch[1] : fullText).trim();
            sqlArea.value = sql || fullText.trim() || previousSql;
        } catch (e) {
            // Restore previous SQL on error; show error info below
            sqlArea.value = previousSql;
            const errNote = document.getElementById('pp-sql-err-note');
            if (errNote) {
                errNote.textContent = 'AI generation failed: ' + (e?.message || 'network error');
                errNote.style.display = '';
                setTimeout(function () { errNote.style.display = 'none'; }, 6000);
            }
        } finally {
            if (btn) { btn.disabled = false; btn.innerHTML = '<i class="bi bi-stars me-1"></i>AI Generate'; }
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

    /** Render the additional value field selects from an array of field names or {field,agg} objects. */
    _renderMultiValueFields(fields) {
        const container = document.getElementById('multi-value-fields-container');
        if (!container) return;
        container.innerHTML = '';
        (fields || []).forEach(f => {
            if (typeof f === 'string') {
                this._addMultiValueFieldSelect(f, 'SUM');
            } else {
                this._addMultiValueFieldSelect(f.field || '', f.agg || 'SUM');
            }
        });
    }

    /** Add a single additional value field select to the container. */
    _addMultiValueFieldSelect(selectedValue, selectedAgg) {
        const container = document.getElementById('multi-value-fields-container');
        if (!container) return;
        // Support legacy string format or new {field, agg} format
        let fieldVal = selectedValue;
        let aggVal = selectedAgg || 'SUM';
        if (typeof selectedValue === 'object' && selectedValue !== null) {
            fieldVal = selectedValue.field || '';
            aggVal = selectedValue.agg || 'SUM';
        }
        const wrapper = document.createElement('div');
        wrapper.className = 'd-flex align-items-center gap-1 mb-1 multi-value-field-row';
        const sel = document.createElement('select');
        sel.className = 'form-select form-select-sm field-select multi-value-field-select';
        sel.style.cssText = 'font-size:0.75rem;flex:1;';
        sel.innerHTML = '<option value="">-- none --</option>' +
            this.fields.map(f => '<option value="' + (typeof escapeHtml === 'function' ? escapeHtml(f) : f) + '">' + (typeof escapeHtml === 'function' ? escapeHtml(f) : f) + '</option>').join('');
        if (fieldVal) {
            this.setFieldValOnEl(sel, fieldVal);
        }
        // Per-field aggregation dropdown
        const aggSel = document.createElement('select');
        aggSel.className = 'form-select form-select-sm multi-value-agg-select';
        aggSel.style.cssText = 'width:auto;min-width:80px;font-size:0.72rem;';
        aggSel.title = 'Aggregation';
        aggSel.innerHTML = '<option value="None">No Agg</option><option value="SUM">Sum</option><option value="AVG">Average</option><option value="COUNT">Count</option><option value="COUNT_DISTINCT">Count (Distinct)</option><option value="MIN">Minimum</option><option value="MAX">Maximum</option><option value="STDEV">Std Dev</option><option value="VAR">Variance</option><option value="MEDIAN">Median</option>';
        aggSel.value = aggVal;
        // Hide agg for table charts
        const isTable = this.currentChart?.chartType === 'table';
        aggSel.style.display = isTable ? 'none' : '';
        const removeBtn = document.createElement('button');
        removeBtn.type = 'button';
        removeBtn.className = 'btn btn-xs';
        removeBtn.style.cssText = 'font-size:0.68rem;padding:1px 4px;border:1px solid #e2e8f0;border-radius:4px;color:#ef4444;background:#fff;flex-shrink:0;';
        removeBtn.title = 'Remove field';
        removeBtn.innerHTML = '<i class="bi bi-x"></i>';
        removeBtn.addEventListener('click', () => {
            wrapper.remove();
            this.apply();
        });
        sel.addEventListener('change', () => this.apply());
        aggSel.addEventListener('change', () => this.apply());
        wrapper.appendChild(sel);
        wrapper.appendChild(aggSel);
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

    /** Collect all additional value fields with per-field aggregation from the UI. */
    _collectMultiValueFields() {
        const container = document.getElementById('multi-value-fields-container');
        if (!container) return [];
        const values = [];
        container.querySelectorAll('.multi-value-field-row').forEach(row => {
            const sel = row.querySelector('.multi-value-field-select');
            const aggSel = row.querySelector('.multi-value-agg-select');
            if (sel && sel.value) {
                values.push({
                    field: this._resolveFieldName(sel.value),
                    agg: aggSel?.value || 'SUM'
                });
            }
        });
        return values;
    }

    _wireAggMenu() {
        // Per-field agg is now inline selects; wire change on primary agg select
        const aggSel = document.getElementById('prop-value-field-agg');
        if (aggSel) {
            aggSel.addEventListener('change', () => {
                this.setVal('prop-agg-function', aggSel.value);
                this.setVal('prop-agg-enabled', aggSel.value !== 'None', 'checkbox');
                this.apply();
            });
        }
        this._updateAggVisibility(this.currentChart?.chartType);
    }

    /** Show/hide aggregation selects based on chart type (table = no agg). */
    _updateAggVisibility(chartType) {
        const isTable = chartType === 'table';
        const primaryAgg = document.getElementById('prop-value-field-agg');
        if (primaryAgg) primaryAgg.style.display = isTable ? 'none' : '';
        document.querySelectorAll('.multi-value-agg-select').forEach(el => {
            el.style.display = isTable ? 'none' : '';
        });
    }

    _renderTableFields(fields) {
        const container = document.getElementById('table-fields-container');
        const addBtn = document.getElementById('add-table-field-btn');
        if (!container) return;
        container.innerHTML = '';
        (fields || []).forEach(f => this._addTableFieldRow(f));
        if (addBtn && !addBtn._tableFieldsWired) {
            addBtn._tableFieldsWired = true;
            addBtn.addEventListener('click', () => this._addTableFieldRow({ fieldName: '', label: '', visible: true, width: '' }));
        }
        if (!fields || fields.length === 0) {
            // Auto-report-generator and AI-built tables don't populate mapping.tableFields,
            // but the rendered table already displays every column returned by the SQL.
            // Seed the Properties panel with those bound columns so the user sees and can
            // tweak/reorder them instead of just one placeholder field.
            const seeded = this._seedTableFieldsFromContext();
            if (seeded.length > 0) {
                seeded.forEach(name => this._addTableFieldRow({
                    fieldName: name, label: name, visible: true, width: ''
                }));
            } else {
                const firstField = Array.isArray(this.fields) && this.fields.length > 0 ? this.fields[0] : '';
                this._addTableFieldRow({ fieldName: firstField, label: firstField, visible: true, width: '' });
            }
        }
    }

    /**
     * Derive the table chart's currently displayed columns when mapping.tableFields is empty.
     * Order of resolution:
     *   1. Read the rendered table's <thead> cells for the current chart card.
     *   2. Fall back to parsing column names from the dataQuery SELECT list.
     */
    _seedTableFieldsFromContext() {
        if (!this.currentChart || this.currentChart.chartType !== 'table') return [];

        // 1) Rendered DOM is the most reliable source — works for SQL, DAX and REST API.
        const chartId = this.currentChart.id;
        if (chartId) {
            const card = document.querySelector(`.chart-card[data-chart-id="${chartId}"]`);
            const headers = card ? card.querySelectorAll('table thead th') : null;
            if (headers && headers.length > 0) {
                const names = Array.from(headers)
                    .map(th => (th.textContent || '').trim())
                    .filter(Boolean);
                if (names.length > 0) return names;
            }
        }

        // 2) Best-effort parse of the SELECT clause.
        const sql = (this.currentChart.dataQuery || '').trim();
        if (!sql) return [];
        const m = /^\s*select\s+(?:top\s+\d+\s+)?([\s\S]+?)\s+from\s/i.exec(sql);
        if (!m) return [];

        // Split on commas that aren't inside parentheses (so SUM([a],[b]) stays intact).
        const parts = [];
        let depth = 0, buf = '';
        for (const ch of m[1]) {
            if (ch === '(') { depth++; buf += ch; }
            else if (ch === ')') { depth--; buf += ch; }
            else if (ch === ',' && depth === 0) { parts.push(buf); buf = ''; }
            else { buf += ch; }
        }
        if (buf.trim()) parts.push(buf);

        return parts.map(part => {
            let col = part.trim();
            // "expr AS [Alias]" / "expr AS Alias"
            const asMatch = /\s+as\s+\[?([^\]\s]+)\]?\s*$/i.exec(col);
            if (asMatch) return asMatch[1];
            // Trailing bracketed alias: "SUM([x]) [Alias]"
            const trailingBracket = /\]\s*\[([^\]]+)\]\s*$/.exec(col);
            if (trailingBracket) return trailingBracket[1];
            // Standalone "[Column]"
            const standalone = /^\[([^\]]+)\]$/.exec(col);
            if (standalone) return standalone[1];
            // Innermost bracketed identifier (e.g. SUM([Revenue]) → Revenue)
            const inner = /\[([^\]]+)\]/.exec(col);
            if (inner) return inner[1];
            // Bare identifier (strip table prefix if any)
            return col.replace(/^.*\./, '').trim();
        }).filter(Boolean);
    }

    _addTableFieldRow(fieldDef) {
        const container = document.getElementById('table-fields-container');
        if (!container) return;
        const esc = (v) => typeof escapeHtml === 'function' ? escapeHtml(v) : String(v ?? '');
        const optionsHtml = this.fields.map(f => `<option value="${esc(f)}">${esc(f)}</option>`).join('');
        const row = document.createElement('div');
        row.className = 'border rounded p-2 mb-1 table-field-row';
        row.innerHTML = `
            <div class="row g-1 align-items-end">
                <div class="col-4">
                    <label class="form-label mb-0" style="font-size:0.68rem">Field</label>
                    <select class="form-select form-select-sm table-field-name">
                        <option value="">--</option>
                        ${optionsHtml}
                    </select>
                </div>
                <div class="col-4">
                    <label class="form-label mb-0" style="font-size:0.68rem">Display Label</label>
                    <input type="text" class="form-control form-control-sm table-field-label" value="${esc(fieldDef?.label || '')}">
                </div>
                <div class="col-2">
                    <label class="form-label mb-0" style="font-size:0.68rem">Width</label>
                    <input type="number" class="form-control form-control-sm table-field-width" min="40" max="800" value="${fieldDef?.width ?? ''}">
                </div>
                <div class="col-2 d-flex align-items-center gap-1">
                    <input type="checkbox" class="form-check-input table-field-visible" ${fieldDef?.visible !== false ? 'checked' : ''} title="Visible">
                    <button type="button" class="btn btn-xs btn-outline-secondary table-field-up" title="Move up"><i class="bi bi-arrow-up"></i></button>
                    <button type="button" class="btn btn-xs btn-outline-secondary table-field-down" title="Move down"><i class="bi bi-arrow-down"></i></button>
                    <button type="button" class="btn btn-xs btn-outline-danger table-field-remove" title="Remove"><i class="bi bi-x"></i></button>
                </div>
            </div>`;
        container.appendChild(row);

        const sel = row.querySelector('.table-field-name');
        if (fieldDef?.fieldName) this.setFieldValOnEl(sel, fieldDef.fieldName);
        if (!row.querySelector('.table-field-label').value && sel.value) row.querySelector('.table-field-label').value = sel.value;
        sel.addEventListener('change', () => {
            const lbl = row.querySelector('.table-field-label');
            if (!lbl.value) lbl.value = sel.value;
            this.apply();
        });
        row.querySelectorAll('input').forEach(i => i.addEventListener('input', () => this.apply()));
        row.querySelector('.table-field-visible')?.addEventListener('change', () => this.apply());
        row.querySelector('.table-field-remove')?.addEventListener('click', () => { row.remove(); this.apply(); });
        row.querySelector('.table-field-up')?.addEventListener('click', () => {
            const prev = row.previousElementSibling;
            if (prev) container.insertBefore(row, prev);
            this.apply();
        });
        row.querySelector('.table-field-down')?.addEventListener('click', () => {
            const next = row.nextElementSibling;
            if (next) container.insertBefore(next, row);
            this.apply();
        });
    }

    _collectTableFields() {
        const container = document.getElementById('table-fields-container');
        if (!container) return [];
        return Array.from(container.querySelectorAll('.table-field-row')).map(row => ({
            fieldName: this._resolveFieldName(row.querySelector('.table-field-name')?.value || ''),
            label: row.querySelector('.table-field-label')?.value || '',
            visible: !!row.querySelector('.table-field-visible')?.checked,
            width: (() => {
                const parsed = parseInt(row.querySelector('.table-field-width')?.value || '', 10);
                return Number.isNaN(parsed) ? null : parsed;
            })()
        })).filter(f => f.fieldName);
    }

    populateNavigationProps(chartDef) {
        const nav = chartDef.navigation || {};
        this.setVal('prop-nav-label', nav.label || chartDef.title || 'Open Link');
        this.setVal('prop-nav-target', nav.target || 'current');
        this.setVal('prop-nav-url', nav.customUrl || '');
        this.setVal('prop-nav-border-enabled', nav.borderEnabled !== false, 'checkbox');
        this.setVal('prop-nav-border-color', nav.borderColor || '#4A90D9');
        this.setVal('prop-nav-border-radius', nav.borderRadius ?? 8);
        this.setVal('prop-nav-bg-color', nav.backgroundColor || '#ffffff');
        this.setVal('prop-nav-text-color', nav.textColor || '#4A90D9');
        this.setVal('prop-nav-font-size', nav.fontSize ?? 13);
        this._populateNavPageOptions(nav.targetPageIndex);
        this._toggleNavigationTargetUrl();
    }

    collectNavigationProps() {
        return {
            label: this.getVal('prop-nav-label') || this.getVal('prop-title') || 'Open Link',
            target: this.getVal('prop-nav-target') || 'current',
            customUrl: this.getVal('prop-nav-url') || '',
            targetPageIndex: parseInt(this.getVal('prop-nav-page')) || 0,
            borderEnabled: this.getVal('prop-nav-border-enabled', 'checkbox'),
            borderColor: this.getVal('prop-nav-border-color') || '#4A90D9',
            borderRadius: parseInt(this.getVal('prop-nav-border-radius')) || 8,
            backgroundColor: this.getVal('prop-nav-bg-color') || '#ffffff',
            textColor: this.getVal('prop-nav-text-color') || '#4A90D9',
            fontSize: parseInt(this.getVal('prop-nav-font-size')) || 13
        };
    }

    _toggleNavigationTargetUrl() {
        const wrap = document.getElementById('prop-nav-url-wrap');
        const pageWrap = document.getElementById('prop-nav-page-wrap');
        const target = this.getVal('prop-nav-target') || 'current';
        if (wrap) wrap.classList.toggle('d-none', target !== 'url');
        if (pageWrap) pageWrap.style.display = target === 'page' ? '' : 'none';
    }

    _populateNavPageOptions(selectedIndex) {
        const sel = document.getElementById('prop-nav-page');
        const targetSel = document.getElementById('prop-nav-target');
        if (!sel) return;
        sel.innerHTML = '';
        const pages = window.canvasManager?.pages || [];
        pages.forEach((p, i) => {
            const opt = document.createElement('option');
            opt.value = i;
            opt.textContent = p.name || ('Page ' + (i + 1));
            sel.appendChild(opt);
        });
        if (selectedIndex !== undefined && selectedIndex !== null) sel.value = selectedIndex;
        // Add 'page' option to target select if not present
        if (targetSel && !targetSel.querySelector('option[value="page"]')) {
            const opt = document.createElement('option');
            opt.value = 'page';
            opt.textContent = 'Navigate to Page';
            targetSel.insertBefore(opt, targetSel.querySelector('option[value="url"]'));
        }
        // Wire target change to toggle page/url visibility
        if (targetSel && !targetSel._navWired) {
            targetSel._navWired = true;
            targetSel.addEventListener('change', () => this._toggleNavigationTargetUrl());
        }
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

// Phase 29-B3: Filter the properties panel by label text.
// Phase 29-B1 extra: Expand/Collapse all sections.
PropertiesPanel.prototype.initPropertySearch = function () {
    const input = document.getElementById('pp-search-input');
    const clearBtn = document.getElementById('pp-search-clear');
    if (!input) return;

    const norm = (s) => (s || '').toLowerCase().trim();

    function rowLabelText(row) {
        // Prefer <label> text; fall back to any text or placeholder.
        const label = row.querySelector('label');
        if (label) return label.textContent;
        const input = row.querySelector('input,select,textarea');
        if (input && input.placeholder) return input.placeholder;
        return row.textContent || '';
    }

    function apply(q) {
        const query = norm(q);
        const showAll = query.length === 0;
        const sections = document.querySelectorAll('#properties-tab-content .prop-section');
        sections.forEach(sec => {
            // Treat each direct child (except the title) as a filterable row.
            const rows = Array.from(sec.children).filter(el => !el.classList.contains('prop-section-title'));
            let anyVisible = false;
            rows.forEach(row => {
                if (showAll) {
                    row.style.display = '';
                    anyVisible = true;
                    return;
                }
                const match = norm(rowLabelText(row)).includes(query);
                row.style.display = match ? '' : 'none';
                if (match) anyVisible = true;
            });
            // Hide the whole section when nothing matches; auto-expand matches.
            sec.style.display = (showAll || anyVisible) ? '' : 'none';
            if (!showAll && anyVisible) sec.classList.remove('collapsed');
        });
        if (clearBtn) clearBtn.style.display = showAll ? 'none' : '';
    }

    input.addEventListener('input', (e) => apply(e.target.value));
    if (clearBtn) {
        clearBtn.addEventListener('click', () => {
            input.value = '';
            apply('');
            input.focus();
        });
    }
    input.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') { input.value = ''; apply(''); input.blur(); }
    });
};

PropertiesPanel.prototype.initExpandAllToggle = function () {
    const btn = document.getElementById('pp-expand-all-btn');
    if (!btn) return;
    btn.style.display = '';
    btn.addEventListener('click', () => {
        const sections = document.querySelectorAll('#properties-tab-content .prop-section');
        // If any section is collapsed, expand all; otherwise collapse all.
        const anyCollapsed = Array.from(sections).some(s => s.classList.contains('collapsed'));
        sections.forEach(sec => {
            const title = sec.querySelector('.prop-section-title');
            if (anyCollapsed) sec.classList.remove('collapsed');
            else sec.classList.add('collapsed');
            const chev = title && title.querySelector('.prop-chevron');
            if (chev) {
                chev.className = anyCollapsed
                    ? 'bi bi-chevron-down prop-chevron ms-auto'
                    : 'bi bi-chevron-right prop-chevron ms-auto';
            }
        });
        const icon = btn.querySelector('i');
        if (icon) icon.className = anyCollapsed ? 'bi bi-arrows-collapse' : 'bi bi-arrows-expand';
    });
};

// Init collapsible sections, data field resize, and tabs after DOM ready
document.addEventListener('DOMContentLoaded', function() {
    propertiesPanel.initCollapsibleSections();
    propertiesPanel.initDataFieldResize();
    propertiesPanel.initTabs();
    propertiesPanel.initPropertySearch();
    propertiesPanel.initExpandAllToggle();
});
