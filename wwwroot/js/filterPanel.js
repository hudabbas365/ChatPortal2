// Filter Panel - per-visual and per-page filtering for the dashboard
class FilterPanel {
    constructor() {
        this.pageFilters = [];
        this.visualFilters = {};
        this.activeTab = 'visual';
    }

    init() {
        this.bindEvents();
        this.render();
    }

    bindEvents() {
        document.getElementById('filter-panel-toggle')?.addEventListener('click', () => this.togglePanel());
        document.getElementById('filter-close-btn')?.addEventListener('click', () => this.closePanel());
        document.getElementById('filter-tab-visual')?.addEventListener('click', () => this.switchTab('visual'));
        document.getElementById('filter-tab-page')?.addEventListener('click', () => this.switchTab('page'));
        document.getElementById('add-filter-btn')?.addEventListener('click', () => this.addFilter());

        document.addEventListener('chart:selected', () => {
            if (this.activeTab === 'visual') this.renderFilters();
        });
    }

    togglePanel() {
        const panel = document.querySelector('.filter-panel');
        if (panel) {
            panel.classList.toggle('open');
            if (panel.classList.contains('open')) this.renderFilters();
        }
    }

    closePanel() {
        const panel = document.querySelector('.filter-panel');
        if (panel) panel.classList.remove('open');
    }

    switchTab(tab) {
        this.activeTab = tab;
        document.querySelectorAll('.filter-tab-btn').forEach(b => b.classList.remove('active'));
        document.getElementById(`filter-tab-${tab}`)?.classList.add('active');
        this.renderFilters();
    }

    getActiveFilters() {
        if (this.activeTab === 'page') return this.pageFilters;
        const chartId = window.canvasManager?.selectedChartId;
        if (!chartId) return [];
        if (!this.visualFilters[chartId]) this.visualFilters[chartId] = [];
        return this.visualFilters[chartId];
    }

    setActiveFilters(filters) {
        if (this.activeTab === 'page') {
            this.pageFilters = filters;
        } else {
            const chartId = window.canvasManager?.selectedChartId;
            if (chartId) this.visualFilters[chartId] = filters;
        }
    }

    addFilter() {
        const filters = this.getActiveFilters();
        filters.push({ field: '', operator: 'equals', value: '' });
        this.setActiveFilters(filters);
        this.renderFilters();
    }

    removeFilter(index) {
        const filters = this.getActiveFilters();
        filters.splice(index, 1);
        this.setActiveFilters(filters);
        this.renderFilters();
        this.applyFilters();
    }

    updateFilter(index, prop, value) {
        const filters = this.getActiveFilters();
        if (filters[index]) {
            filters[index][prop] = value;
            this.setActiveFilters(filters);
            // Auto-apply on field/operator change, but not on value typing
            if (prop !== 'value') this.applyFilters();
        }
    }

    getAvailableFields() {
        if (window.propertiesPanel && window.propertiesPanel.fields.length > 0) {
            return window.propertiesPanel.fields;
        }
        return [];
    }

    renderFilters() {
        const container = document.getElementById('filter-list');
        if (!container) return;

        const filters = this.getActiveFilters();
        const fields = this.getAvailableFields();

        if (this.activeTab === 'visual' && !window.canvasManager?.selectedChartId) {
            container.innerHTML = '<div class="filter-empty"><i class="bi bi-cursor-fill"></i><small>Select a chart first, then add filters<br>to control what data it displays.</small></div>';
            return;
        }

        if (filters.length === 0) {
            container.innerHTML = '<div class="filter-empty"><i class="bi bi-funnel"></i><small>No filters yet.<br>Click <b>Add Filter</b> above to get started.</small></div>';
            return;
        }

        container.innerHTML = filters.map((f, i) => `
            <div class="filter-item" data-index="${i}">
                <div class="filter-item-header">
                    <span class="filter-item-number"><i class="bi bi-funnel-fill me-1"></i>Filter ${i + 1}</span>
                    <button class="btn btn-xs filter-remove-btn" data-index="${i}" title="Remove filter">
                        <i class="bi bi-trash"></i>
                    </button>
                </div>
                <div class="filter-row">
                    <select class="form-select form-select-sm filter-field" data-index="${i}" data-prop="field">
                        <option value="">-- Select Field --</option>
                        ${fields.map(fld => `<option value="${escapeHtml(fld)}" ${f.field === fld ? 'selected' : ''}>${escapeHtml(fld)}</option>`).join('')}
                    </select>
                </div>
                <div class="filter-row">
                    <select class="form-select form-select-sm filter-operator" data-index="${i}" data-prop="operator">
                        <option value="equals" ${f.operator === 'equals' ? 'selected' : ''}>Equals</option>
                        <option value="notEquals" ${f.operator === 'notEquals' ? 'selected' : ''}>Not Equals</option>
                        <option value="contains" ${f.operator === 'contains' ? 'selected' : ''}>Contains</option>
                        <option value="gt" ${f.operator === 'gt' ? 'selected' : ''}>Greater Than (&gt;)</option>
                        <option value="gte" ${f.operator === 'gte' ? 'selected' : ''}>Greater or Equal (&gt;=)</option>
                        <option value="lt" ${f.operator === 'lt' ? 'selected' : ''}>Less Than (&lt;)</option>
                        <option value="lte" ${f.operator === 'lte' ? 'selected' : ''}>Less or Equal (&lt;=)</option>
                    </select>
                </div>
                <div class="filter-row">
                    <input type="text" class="form-control form-control-sm filter-value" data-index="${i}" data-prop="value" value="${escapeHtml(f.value || '')}" placeholder="Enter value...">
                </div>
                <div class="filter-item-actions">
                    <button class="filter-apply-btn" data-index="${i}" title="Apply this filter">
                        <i class="bi bi-check-lg me-1"></i>Apply
                    </button>
                    <button class="filter-clear-btn" data-index="${i}" title="Clear this filter's value">
                        <i class="bi bi-eraser me-1"></i>Clear
                    </button>
                </div>
            </div>`).join('');

        container.querySelectorAll('.filter-field, .filter-operator').forEach(sel => {
            sel.addEventListener('change', (e) => {
                this.updateFilter(parseInt(e.target.dataset.index), e.target.dataset.prop, e.target.value);
            });
        });

        container.querySelectorAll('.filter-value').forEach(input => {
            input.addEventListener('keydown', (e) => {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    this.applyFilters();
                }
            });
        });

        container.querySelectorAll('.filter-remove-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                this.removeFilter(parseInt(e.currentTarget.dataset.index));
            });
        });

        container.querySelectorAll('.filter-apply-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const idx = parseInt(e.currentTarget.dataset.index);
                const filters = this.getActiveFilters();
                const f = filters[idx];
                // Read latest value from input
                const input = container.querySelector(`.filter-value[data-index="${idx}"]`);
                if (input && f) {
                    f.value = input.value;
                    this.setActiveFilters(filters);
                }
                this.applyFilters();
            });
        });

        container.querySelectorAll('.filter-clear-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const idx = parseInt(e.currentTarget.dataset.index);
                const filters = this.getActiveFilters();
                if (filters[idx]) {
                    filters[idx].value = '';
                    this.setActiveFilters(filters);
                    this.renderFilters();
                    this.applyFilters();
                }
            });
        });
    }

    applyFilters() {
        document.dispatchEvent(new CustomEvent('filters:change', {
            detail: { pageFilters: this.pageFilters, visualFilters: this.visualFilters }
        }));
    }

    getFiltersForChart(chartId) {
        const visual = this.visualFilters[chartId] || [];
        return [...this.pageFilters, ...visual].filter(f => f.field && f.value);
    }

    static filterData(data, filters) {
        if (!filters || filters.length === 0) return data;
        return data.filter(row => {
            return filters.every(f => {
                const val = row[f.field];
                if (val === undefined || val === null) return false;
                const strVal = String(val).toLowerCase();
                const filterVal = String(f.value).toLowerCase();
                const numVal = parseFloat(val);
                const numFilter = parseFloat(f.value);
                switch (f.operator) {
                    case 'equals': return strVal === filterVal;
                    case 'notEquals': return strVal !== filterVal;
                    case 'contains': return strVal.includes(filterVal);
                    case 'gt': return !isNaN(numVal) && !isNaN(numFilter) && numVal > numFilter;
                    case 'gte': return !isNaN(numVal) && !isNaN(numFilter) && numVal >= numFilter;
                    case 'lt': return !isNaN(numVal) && !isNaN(numFilter) && numVal < numFilter;
                    case 'lte': return !isNaN(numVal) && !isNaN(numFilter) && numVal <= numFilter;
                    default: return true;
                }
            });
        });
    }

    render() {
        this.renderFilters();
    }
}

window.filterPanel = new FilterPanel();
