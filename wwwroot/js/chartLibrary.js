// Shared HTML escape utility (loaded first, used by all modules)
function escapeHtml(str) {
    const div = document.createElement('div');
    div.appendChild(document.createTextNode(String(str ?? '')));
    return div.innerHTML;
}

// Chart type library manager - handles the left panel
class ChartLibrary {
    constructor() {
        this.charts = [];              // original groups from server
        this.filteredCharts = [];      // after search + category filter
        this.searchTerm = '';
        this.activeCategory = 'all';   // 'all' | 'recent' | <group name>
        this.recentKey = 'cp.chartLibrary.recent.v1';
        this.recentMax = 8;
        // Phase 32-B2: pinned favorites (chart-type ids).
        this.pinnedKey = 'cp.chartLibrary.pinned.v1';
        // Lightweight keyword synonyms — makes search forgiving across the 72 charts.
        this.synonyms = {
            bar: ['column', 'histogram', 'bar'],
            line: ['trend', 'time', 'series', 'line'],
            pie: ['donut', 'doughnut', 'pie', 'ring'],
            map: ['geo', 'choropleth', 'region', 'map'],
            kpi: ['metric', 'card', 'tile', 'number', 'value', 'kpi'],
            stat: ['box', 'violin', 'distribution', 'stat'],
            scatter: ['point', 'dot', 'bubble', 'scatter'],
            compare: ['compare', 'diff', 'dumbbell', 'slope', 'paired'],
            progress: ['gauge', 'progress', 'radial', 'bullet'],
            flow: ['sankey', 'chord', 'arc', 'network', 'flow']
        };
    }

    init(chartGroups) {
        this.charts = Array.isArray(chartGroups) ? chartGroups : [];
        this._ensureChrome();
        this.applyFilters();
        this.bindSearch();
        this.bindChips();
        this._bindKeyboard();
    }

    // ---- Chrome (chips + count + search clear button) ----
    _ensureChrome() {
        const aside = document.querySelector('.library-panel');
        if (!aside) return;

        // Add clear (x) button inside the existing search input group (once)
        const searchInput = document.getElementById('chart-search');
        if (searchInput && !searchInput.parentElement.querySelector('.clear-search-btn')) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'clear-search-btn';
            btn.setAttribute('aria-label', 'Clear search');
            btn.innerHTML = '<i class="bi bi-x-lg"></i>';
            btn.addEventListener('click', () => {
                searchInput.value = '';
                this.searchTerm = '';
                btn.classList.remove('visible');
                this.applyFilters();
                searchInput.focus();
            });
            searchInput.parentElement.appendChild(btn);
        }

        // Chips row (once)
        if (!document.getElementById('cl-chips')) {
            const chips = document.createElement('div');
            chips.className = 'cl-chips';
            chips.id = 'cl-chips';
            const scroll = aside.querySelector('.library-scroll');
            if (scroll) aside.insertBefore(chips, scroll);
        }
    }

    bindSearch() {
        const searchInput = document.getElementById('chart-search');
        if (!searchInput || searchInput.dataset.clBound === '1') return;
        searchInput.dataset.clBound = '1';
        searchInput.addEventListener('input', (e) => {
            this.searchTerm = (e.target.value || '').toLowerCase().trim();
            const clear = searchInput.parentElement.querySelector('.clear-search-btn');
            if (clear) clear.classList.toggle('visible', !!this.searchTerm);
            this.applyFilters();
        });
        searchInput.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                searchInput.value = '';
                this.searchTerm = '';
                const clear = searchInput.parentElement.querySelector('.clear-search-btn');
                if (clear) clear.classList.remove('visible');
                this.applyFilters();
            } else if (e.key === 'Enter') {
                const first = document.querySelector('#chart-library-container .library-item');
                if (first) {
                    e.preventDefault();
                    this._addItemToCanvas(first);
                }
            }
        });
    }

    bindChips() {
        const chips = document.getElementById('cl-chips');
        if (!chips || chips.dataset.clBound === '1') return;
        chips.dataset.clBound = '1';
        chips.addEventListener('click', (e) => {
            const chip = e.target.closest('.cl-chip');
            if (!chip) return;
            this.activeCategory = chip.dataset.category || 'all';
            this.applyFilters();
        });
    }

    _bindKeyboard() {
        const container = document.getElementById('chart-library-container');
        if (!container || container.dataset.clKbBound === '1') return;
        container.dataset.clKbBound = '1';
        // Simple click-to-add on the inline "+" button
        container.addEventListener('click', (e) => {
            const pinBtn = e.target.closest('.cl-pin-btn');
            if (pinBtn) {
                e.preventDefault();
                e.stopPropagation();
                const item = pinBtn.closest('.library-item');
                if (item) this._togglePin(item.dataset.chartType);
                return;
            }
            const addBtn = e.target.closest('.cl-add-btn');
            if (!addBtn) return;
            e.preventDefault();
            e.stopPropagation();
            const item = addBtn.closest('.library-item');
            if (item) this._addItemToCanvas(item);
        });
    }

    // ---- Filtering ----
    _matches(chart, group, term) {
        if (!term) return true;
        const hay = (chart.name + ' ' + (chart.description || '') + ' ' + (group || '') + ' ' + (chart.id || '')).toLowerCase();
        if (hay.includes(term)) return true;
        // Synonym expansion: if user typed a keyword that maps to synonyms, match any of them.
        for (const key of Object.keys(this.synonyms)) {
            if (key.includes(term) || term.includes(key)) {
                if (this.synonyms[key].some(s => hay.includes(s))) return true;
            }
        }
        return false;
    }

    applyFilters() {
        const term = this.searchTerm;
        let groups = this.charts;

        // Category filter
        if (this.activeCategory === 'recent') {
            const recent = this._buildRecentGroup();
            groups = recent ? [recent] : [];
        } else if (this.activeCategory === 'favorites') {
            const fav = this._buildPinnedGroup();
            groups = fav ? [fav] : [];
        } else if (this.activeCategory !== 'all') {
            groups = groups.filter(g => g.group === this.activeCategory);
        } else {
            // On "all", prepend pinned + recent groups if populated and there's no active search
            const pinned = this._buildPinnedGroup();
            const recent = this._buildRecentGroup();
            const prepend = [];
            if (pinned && !term) prepend.push(pinned);
            if (recent && !term) prepend.push(recent);
            if (prepend.length) groups = [...prepend, ...groups];
        }

        // Text filter
        if (term) {
            groups = groups.map(g => ({
                ...g,
                charts: (g.charts || []).filter(c => this._matches(c, g.group, term))
            })).filter(g => g.charts.length > 0);
        }

        this.filteredCharts = groups;
        this.render();
    }

    // ---- Recent tracking ----
    _loadRecent() {
        try { return JSON.parse(localStorage.getItem(this.recentKey)) || []; }
        catch { return []; }
    }
    _saveRecent(list) {
        try { localStorage.setItem(this.recentKey, JSON.stringify(list.slice(0, this.recentMax))); }
        catch { /* ignore quota */ }
    }
    _trackRecent(chartTypeId) {
        if (!chartTypeId) return;
        const list = this._loadRecent().filter(id => id !== chartTypeId);
        list.unshift(chartTypeId);
        this._saveRecent(list);
    }
    _findChartById(id) {
        for (const g of this.charts) {
            const hit = (g.charts || []).find(c => c.id === id);
            if (hit) return { chart: hit, group: g.group };
        }
        return null;
    }
    _buildRecentGroup() {
        const ids = this._loadRecent();
        if (!ids.length) return null;
        const charts = [];
        for (const id of ids) {
            const found = this._findChartById(id);
            if (found) charts.push(found.chart);
        }
        if (!charts.length) return null;
        return { group: 'Recent', charts, _isRecent: true };
    }

    // Phase 32-B2: pinned favorites ----------------------------------
    _loadPinned() {
        try { return JSON.parse(localStorage.getItem(this.pinnedKey)) || []; }
        catch { return []; }
    }
    _savePinned(list) {
        try { localStorage.setItem(this.pinnedKey, JSON.stringify(list)); }
        catch { /* ignore quota */ }
    }
    _isPinned(id) {
        return this._loadPinned().includes(id);
    }
    _togglePin(id) {
        if (!id) return;
        const list = this._loadPinned();
        const i = list.indexOf(id);
        if (i >= 0) list.splice(i, 1);
        else list.unshift(id);
        this._savePinned(list);
        this.applyFilters();
    }
    _buildPinnedGroup() {
        const ids = this._loadPinned();
        if (!ids.length) return null;
        const charts = [];
        for (const id of ids) {
            const found = this._findChartById(id);
            if (found) charts.push(found.chart);
        }
        if (!charts.length) return null;
        return { group: 'Favorites', charts, _isPinned: true };
    }

    _addItemToCanvas(item) {
        const id = item.dataset.chartType;
        this._trackRecent(id);
        if (window.canvasManager) {
            window.canvasManager.addChart({
                chartType: id,
                title: item.dataset.chartName
            });
        }
    }

    // ---- Rendering ----
    _renderChips() {
        const chips = document.getElementById('cl-chips');
        if (!chips) return;
        const totalCount = this.charts.reduce((s, g) => s + (g.charts ? g.charts.length : 0), 0);
        const recentCount = this._loadRecent().length;
        const pinnedCount = this._loadPinned().length;
        const items = [];
        items.push({ key: 'all', label: 'All', count: totalCount });
        if (pinnedCount) items.push({ key: 'favorites', label: '★ Favorites', count: pinnedCount });
        if (recentCount) items.push({ key: 'recent', label: 'Recent', count: recentCount });
        this.charts.forEach(g => {
            if (!g.charts || !g.charts.length) return;
            items.push({ key: g.group, label: g.group, count: g.charts.length });
        });
        chips.innerHTML = items.map(it => `
            <button class="cl-chip ${this.activeCategory === it.key ? 'active' : ''}" data-category="${escapeHtml(it.key)}" type="button">
                <span>${escapeHtml(it.label)}</span>
                <span class="cl-chip-count">${it.count}</span>
            </button>
        `).join('');
    }

    _highlight(text) {
        const safe = escapeHtml(text);
        const term = this.searchTerm;
        if (!term) return safe;
        try {
            const rx = new RegExp('(' + term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + ')', 'ig');
            return safe.replace(rx, '<mark class="cl-hl">$1</mark>');
        } catch { return safe; }
    }

    render() {
        const container = document.getElementById('chart-library-container');
        if (!container) return;

        this._renderChips();
        container.innerHTML = '';

        // Total matched count (skip when all + no search to avoid noise)
        const matched = this.filteredCharts.reduce((s, g) => s + g.charts.length, 0);
        if (this.searchTerm || this.activeCategory !== 'all') {
            const summary = document.createElement('div');
            summary.className = 'cl-result-count';
            summary.innerHTML = `<strong>${matched}</strong> chart${matched === 1 ? '' : 's'}`
                + (this.searchTerm ? ` matching “${escapeHtml(this.searchTerm)}”` : '')
                + (this.activeCategory !== 'all' && this.activeCategory !== 'recent' ? ` in ${escapeHtml(this.activeCategory)}` : '');
            container.appendChild(summary);
        }

        if (!this.filteredCharts.length) {
            const empty = document.createElement('div');
            empty.className = 'cl-empty';
            empty.innerHTML = `
                <i class="bi bi-search"></i>
                <div>No charts match your search.</div>
                <button type="button" class="cl-empty-reset">Clear filters</button>
            `;
            empty.querySelector('.cl-empty-reset').addEventListener('click', () => {
                this.searchTerm = '';
                this.activeCategory = 'all';
                const s = document.getElementById('chart-search');
                if (s) s.value = '';
                const clear = document.querySelector('.library-panel .clear-search-btn');
                if (clear) clear.classList.remove('visible');
                this.applyFilters();
            });
            container.appendChild(empty);
            return;
        }

        const expandAll = !!this.searchTerm; // auto-expand all groups during search
        this.filteredCharts.forEach((group, idx) => {
            if (!group.charts || group.charts.length === 0) return;
            const isBasic = group.group === 'Basic' || group._isRecent || group._isPinned;
            const open = expandAll || isBasic;
            const groupEl = document.createElement('div');
            groupEl.className = 'library-group mb-2' + (group._isRecent ? ' is-recent' : '') + (group._isPinned ? ' is-pinned' : '');
            const gid = 'group-' + idx + '-' + (group.group || '').replace(/\W+/g, '');
            const headerIcon = group._isPinned ? 'bi-star-fill' : (group._isRecent ? 'bi-clock-history' : 'bi-folder2-open');
            groupEl.innerHTML = `
                <div class="library-group-header${open ? '' : ' collapsed'}" data-bs-toggle="collapse" data-bs-target="#${gid}">
                    <i class="bi ${headerIcon} group-icon me-2"></i>
                    <span class="group-name">${escapeHtml(group.group)}</span>
                    <span class="badge bg-secondary">${group.charts.length}</span>
                    <i class="bi bi-chevron-down ms-auto"></i>
                </div>
                <div class="collapse${open ? ' show' : ''}" id="${gid}">
                    <div class="library-items">
                        ${group.charts.map(c => {
                            const pinned = this._isPinned(c.id);
                            return `
                            <div class="library-item"
                                 data-chart-type="${escapeHtml(c.id)}"
                                 data-chart-name="${escapeHtml(c.name)}"
                                 data-chartjs-type="${escapeHtml(c.chartJsType || '')}"
                                 draggable="true"
                                 title="${escapeHtml(c.description || c.name)}">
                                <span class="cl-item-icon"><i class="bi ${escapeHtml(c.icon || 'bi-bar-chart')}"></i></span>
                                <span class="cl-item-text">${this._highlight(c.name)}</span>
                                <button type="button" class="cl-pin-btn${pinned ? ' pinned' : ''}" title="${pinned ? 'Unpin from favorites' : 'Pin to favorites'}" aria-label="${pinned ? 'Unpin' : 'Pin'} ${escapeHtml(c.name)}">
                                    <i class="bi ${pinned ? 'bi-star-fill' : 'bi-star'}"></i>
                                </button>
                                <button type="button" class="cl-add-btn" title="Add to canvas" aria-label="Add ${escapeHtml(c.name)} to canvas">
                                    <i class="bi bi-plus-lg"></i>
                                </button>
                            </div>`;
                        }).join('')}
                    </div>
                </div>
            `;
            container.appendChild(groupEl);
        });

        this.bindDragEvents();
        this.bindClickEvents();
    }

    bindDragEvents() {
        document.querySelectorAll('#chart-library-container .library-item').forEach(item => {
            item.addEventListener('dragstart', (e) => {
                e.dataTransfer.setData('chartType', item.dataset.chartType);
                e.dataTransfer.setData('chartName', item.dataset.chartName);
                e.dataTransfer.setData('chartJsType', item.dataset.chartjsType);
                item.classList.add('dragging');
                this._trackRecent(item.dataset.chartType);
                const drop = document.getElementById('chart-canvas-drop');
                if (drop) drop.classList.add('drop-active');
            });
            item.addEventListener('dragend', () => {
                item.classList.remove('dragging');
                const drop = document.getElementById('chart-canvas-drop');
                if (drop) drop.classList.remove('drop-active');
            });
        });
    }

    bindClickEvents() {
        document.querySelectorAll('#chart-library-container .library-item').forEach(item => {
            item.addEventListener('dblclick', () => this._addItemToCanvas(item));
        });
    }
}

window.chartLibrary = new ChartLibrary();
