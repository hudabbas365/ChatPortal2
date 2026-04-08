// Properties panel - handles the right panel
class PropertiesPanel {
    constructor() {
        this.currentChart = null;
        this.fields = [];
    }

    clear() {
        this.currentChart = null;
        const panel = document.getElementById('properties-empty');
        const form = document.getElementById('properties-form');
        if (panel) panel.style.display = 'flex';
        if (form) form.style.display = 'none';
    }

    async load(chartDef) {
        this.currentChart = JSON.parse(JSON.stringify(chartDef));
        const panel = document.getElementById('properties-empty');
        const form = document.getElementById('properties-form');
        if (panel) panel.style.display = 'none';
        if (form) form.style.display = 'block';

        await this.loadFields(chartDef.datasetName);
        this.populate();
        this.bindAutoApply();
        this.updateTypeSpecificFields(chartDef.chartType);
        this.initFieldDropTargets();
    }

    async loadFields(datasetName) {
        try {
            const resp = await fetch(`/api/data/${datasetName}/fields`);
            this.fields = await resp.json();
            this.updateFieldSelects();
            this.renderDataFields();
        } catch(e) {
            this.fields = [];
        }
    }

    updateFieldSelects() {
        const selects = document.querySelectorAll('.field-select');
        selects.forEach(sel => {
            const current = sel.value;
            sel.innerHTML = '<option value="">-- none --</option>' +
                this.fields.map(f => `<option value="${escapeHtml(f)}">${escapeHtml(f)}</option>`).join('');
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
        this.setVal('prop-label-field', c.mapping?.labelField || '');
        this.setVal('prop-value-field', c.mapping?.valueField || '');
        this.setVal('prop-line-value-field', c.mapping?.lineValueField || '');
        this.setVal('prop-x-field', c.mapping?.xField || '');
        this.setVal('prop-y-field', c.mapping?.yField || '');
        this.setVal('prop-r-field', c.mapping?.rField || '');
        this.setVal('prop-group-by-field', c.mapping?.groupByField || '');
        this.setVal('prop-agg-enabled', c.aggregation?.enabled || false, 'checkbox');
        this.setVal('prop-agg-function', c.aggregation?.function || 'SUM');
        this.setVal('prop-color-palette', this._resolveColorHex(c.style?.colorPalette));
        this.setVal('prop-show-legend', c.style?.showLegend !== false, 'checkbox');
        this.setVal('prop-legend-position', c.style?.legendPosition || 'top');
        this.setVal('prop-fill-area', c.style?.fillArea || false, 'checkbox');
        this.setVal('prop-show-tooltips', c.style?.showTooltips !== false, 'checkbox');
        this.setVal('prop-animated', c.style?.animated !== false, 'checkbox');
        this.setVal('prop-title-font-size', c.style?.titleFontSize || 14);
        this.setVal('prop-border-radius', c.style?.borderRadius || '4');
        this.setVal('prop-custom-json', c.customJsonData || '');
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
                labelField: this.getVal('prop-label-field'),
                valueField: this.getVal('prop-value-field'),
                lineValueField: this.getVal('prop-line-value-field'),
                xField: this.getVal('prop-x-field'),
                yField: this.getVal('prop-y-field'),
                rField: this.getVal('prop-r-field'),
                groupByField: this.getVal('prop-group-by-field'),
            },
            aggregation: {
                enabled: this.getVal('prop-agg-enabled', 'checkbox'),
                function: this.getVal('prop-agg-function'),
            },
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
    }

    async datasetChanged() {
        const ds = this.getVal('prop-dataset');
        await this.loadFields(ds);
        if (this.currentChart) this.currentChart.datasetName = ds;
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
        if (this.fields.length === 0) {
            container.innerHTML = '';
            return;
        }
        container.innerHTML = this.fields.map(f =>
            `<div class="data-field-item" draggable="true" data-field="${escapeHtml(f)}">
                <i class="bi bi-grip-vertical"></i>
                <i class="bi bi-hash"></i>
                <span>${escapeHtml(f)}</span>
            </div>`
        ).join('');

        container.querySelectorAll('.data-field-item').forEach(item => {
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
}

window.propertiesPanel = new PropertiesPanel();
