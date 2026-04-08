// Canvas manager - manages charts on a free-form absolute-positioned canvas
class CanvasManager {
    constructor() {
        this.charts = [];
        this.pages = [];
        this.activePageIndex = 0;
        this.selectedChartId = null;
        this._maxZIndex = 1;
        this._dragState = null;
    }

    init(initialCharts, pages, activePageIndex) {
        if (pages && pages.length > 0) {
            this.pages = pages;
            this.activePageIndex = activePageIndex || 0;
            this.charts = this.pages[this.activePageIndex].charts || [];
        } else {
            this.pages = [{ name: 'Page 1', charts: initialCharts || [] }];
            this.activePageIndex = 0;
            this.charts = this.pages[0].charts;
        }
        this.renderAll();
        this.renderPageTabs();
        this.initDropZone();

        const addBtn = document.getElementById('add-page-btn');
        if (addBtn) addBtn.addEventListener('click', () => this.addPage());
    }

    initDropZone() {
        const dropZone = document.getElementById('chart-canvas-drop');
        if (!dropZone) return;
        dropZone.addEventListener('dragover', (e) => {
            e.preventDefault();
            dropZone.classList.add('drag-over');
        });
        dropZone.addEventListener('dragleave', (e) => {
            if (!dropZone.contains(e.relatedTarget)) dropZone.classList.remove('drag-over');
        });
        dropZone.addEventListener('drop', (e) => {
            e.preventDefault();
            dropZone.classList.remove('drag-over');
            const chartType = e.dataTransfer.getData('chartType');
            const chartName = e.dataTransfer.getData('chartName');
            if (chartType) {
                const rect = dropZone.getBoundingClientRect();
                const x = Math.max(0, e.clientX - rect.left - 100);
                const y = Math.max(0, e.clientY - rect.top - 20);
                this.addChart({ chartType, title: chartName, posX: Math.round(x), posY: Math.round(y) });
            }
        });
    }

    async addChart(partial) {
        const defaultX = 20 + (this.charts.length % 5) * 30;
        const defaultY = 20 + (this.charts.length % 5) * 30;
        const chart = {
            id: 'c' + Date.now(),
            chartType: partial.chartType || 'bar',
            title: partial.title || 'New Chart',
            datasetName: partial.datasetName || 'sales',
            width: partial.width || 6,
            height: partial.height || 300,
            gridCol: 0,
            gridRow: 0,
            posX: partial.posX !== undefined ? partial.posX : defaultX,
            posY: partial.posY !== undefined ? partial.posY : defaultY,
            zIndex: ++this._maxZIndex,
            mapping: partial.mapping || { labelField: 'month', valueField: 'revenue', groupByField: '', xField: '', yField: '', rField: '', multiValueFields: [] },
            aggregation: partial.aggregation || { function: 'SUM', enabled: true },
            style: partial.style || { backgroundColor: '#4A90D9', borderColor: '#2C6FAC', showLegend: true, legendPosition: 'top', showTooltips: true, fillArea: false, colorPalette: 'default', showDataLabels: false, fontFamily: 'Inter, sans-serif', titleFontSize: 14, animated: true, responsive: true, borderRadius: '4' },
            customJsonData: partial.customJsonData || ''
        };

        const resp = await fetch('/api/chart/add', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(chart)
        });
        const saved = await resp.json();
        chart.id = saved.id;
        this.charts.push(chart);
        this.renderChart(chart);
        this.selectChart(chart.id);
        this.updateEmptyState();
        return chart;
    }

    async deleteChart(chartId) {
        await fetch(`/api/chart/${chartId}`, { method: 'DELETE' });
        this.charts = this.charts.filter(c => c.id !== chartId);
        const card = document.querySelector(`.chart-card[data-chart-id="${chartId}"]`);
        if (card) card.remove();
        if (this.selectedChartId === chartId) {
            this.selectedChartId = null;
            if (window.propertiesPanel) window.propertiesPanel.clear();
        }
        this.updateEmptyState();
    }

    async duplicateChart(chartId) {
        const original = this.charts.find(c => c.id === chartId);
        if (!original) return;
        const copy = JSON.parse(JSON.stringify(original));
        copy.title = original.title + ' (Copy)';
        copy.posX = (original.posX || 0) + 30;
        copy.posY = (original.posY || 0) + 30;
        await this.addChart(copy);
    }

    selectChart(chartId) {
        this.selectedChartId = chartId;
        document.querySelectorAll('.chart-card').forEach(c => c.classList.remove('selected'));
        const card = document.querySelector(`.chart-card[data-chart-id="${chartId}"]`);
        if (card) {
            card.classList.add('selected');
            // Bring to front
            const chart = this.charts.find(c => c.id === chartId);
            if (chart) {
                chart.zIndex = ++this._maxZIndex;
                card.style.zIndex = chart.zIndex;
            }
        }
        const chart = this.charts.find(c => c.id === chartId);
        if (chart && window.propertiesPanel) window.propertiesPanel.load(chart);
    }

    async updateChart(chartDef) {
        const idx = this.charts.findIndex(c => c.id === chartDef.id);
        if (idx >= 0) this.charts[idx] = chartDef;

        await fetch(`/api/chart/${chartDef.id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(chartDef)
        });

        const card = document.querySelector(`.chart-card[data-chart-id="${chartDef.id}"]`);
        if (card) {
            const titleEl = card.querySelector('.chart-title');
            if (titleEl) titleEl.textContent = chartDef.title;
            // Update card width
            const cardWidth = this.colsToPixels(chartDef.width || 6);
            card.style.width = cardWidth + 'px';
            const canvasWrap = card.querySelector('.chart-canvas-wrap');
            if (canvasWrap) canvasWrap.style.height = (parseInt(chartDef.height) || 300) + 'px';
            const canvasEl = card.querySelector('canvas');
            if (canvasEl) await window.chartRenderer.render(chartDef, canvasEl);
        }
    }

    renderAll() {
        const container = document.getElementById('chart-canvas-drop');
        if (!container) return;
        container.innerHTML = '';
        this._maxZIndex = 0;
        this.charts.forEach(c => {
            if (c.zIndex > this._maxZIndex) this._maxZIndex = c.zIndex;
        });
        this.charts.forEach(c => this.renderChart(c));
        this.updateEmptyState();
    }

    colsToPixels(width) {
        // Convert Bootstrap cols (2-12) to pixel width
        const baseWidth = Math.min(window.innerWidth - 600, 900);
        const pct = (parseInt(width) || 6) / 12;
        return Math.round(Math.max(200, baseWidth * pct));
    }

    renderChart(chartDef) {
        const container = document.getElementById('chart-canvas-drop');
        if (!container) return;

        const safeId = escapeHtml(chartDef.id);
        const safeTitle = escapeHtml(chartDef.title || 'Chart');
        const posX = chartDef.posX !== undefined ? chartDef.posX : 20;
        const posY = chartDef.posY !== undefined ? chartDef.posY : 20;
        const cardWidth = this.colsToPixels(chartDef.width || 6);
        const zIdx = chartDef.zIndex || 1;

        const card = document.createElement('div');
        card.className = 'chart-card';
        card.dataset.chartId = chartDef.id;
        card.style.cssText = `left:${posX}px;top:${posY}px;width:${cardWidth}px;z-index:${zIdx};`;

        card.innerHTML = `
            <div class="chart-card-header">
                <i class="bi bi-grip-vertical chart-drag-handle text-muted me-2" title="Drag to reposition"></i>
                <span class="chart-title">${safeTitle}</span>
                <div class="chart-card-actions ms-auto">
                    <button class="btn btn-xs btn-icon" data-action="edit" title="Edit">
                        <i class="bi bi-pencil"></i>
                    </button>
                    <button class="btn btn-xs btn-icon" data-action="duplicate" title="Duplicate">
                        <i class="bi bi-copy"></i>
                    </button>
                    <button class="btn btn-xs btn-icon text-danger" data-action="delete" title="Delete">
                        <i class="bi bi-trash"></i>
                    </button>
                </div>
            </div>
            <div class="chart-canvas-wrap" style="height: ${parseInt(chartDef.height) || 300}px">
                <canvas id="canvas-${safeId}"></canvas>
            </div>
            <div class="chart-resize-handle" title="Drag to resize"></div>
        `;

        card.querySelector('[data-action="edit"]').addEventListener('click', (e) => {
            e.stopPropagation();
            this.selectChart(chartDef.id);
        });
        card.querySelector('[data-action="duplicate"]').addEventListener('click', (e) => {
            e.stopPropagation();
            this.duplicateChart(chartDef.id);
        });
        card.querySelector('[data-action="delete"]').addEventListener('click', (e) => {
            e.stopPropagation();
            this.deleteChart(chartDef.id);
        });

        card.addEventListener('mousedown', (e) => {
            if (!e.target.closest('button')) this.selectChart(chartDef.id);
        });

        container.appendChild(card);

        // Make draggable
        this._makeCardDraggable(card, chartDef);
        // Make resizable
        this._makeCardResizable(card, chartDef);

        const canvasEl = card.querySelector('canvas');
        requestAnimationFrame(() => window.chartRenderer.render(chartDef, canvasEl));
    }

    _makeCardDraggable(card, chartDef) {
        const handle = card.querySelector('.chart-drag-handle');
        if (!handle) return;

        handle.addEventListener('mousedown', (e) => {
            e.preventDefault();
            e.stopPropagation();

            this.selectChart(chartDef.id);

            const container = document.getElementById('chart-canvas-drop');
            const scrollEl = container.parentElement; // .canvas-scroll
            // Snapshot rects at drag start; adjust for current scroll offset
            const containerBase = container.getBoundingClientRect();
            const scrollLeft = scrollEl ? scrollEl.scrollLeft : 0;
            const scrollTop  = scrollEl ? scrollEl.scrollTop  : 0;
            const cardRect = card.getBoundingClientRect();

            // Offset from mouse to card top-left
            const offsetX = e.clientX - cardRect.left;
            const offsetY = e.clientY - cardRect.top;

            card.classList.add('dragging');

            const onMouseMove = (ev) => {
                const sl = scrollEl ? scrollEl.scrollLeft : 0;
                const st = scrollEl ? scrollEl.scrollTop  : 0;
                const x = Math.max(0, ev.clientX - containerBase.left + (sl - scrollLeft) - offsetX);
                const y = Math.max(0, ev.clientY - containerBase.top  + (st - scrollTop)  - offsetY);
                card.style.left = x + 'px';
                card.style.top  = y + 'px';
            };

            const onMouseUp = (ev) => {
                card.classList.remove('dragging');
                document.removeEventListener('mousemove', onMouseMove);
                document.removeEventListener('mouseup', onMouseUp);

                const sl = scrollEl ? scrollEl.scrollLeft : 0;
                const st = scrollEl ? scrollEl.scrollTop  : 0;
                const x = Math.max(0, ev.clientX - containerBase.left + (sl - scrollLeft) - offsetX);
                const y = Math.max(0, ev.clientY - containerBase.top  + (st - scrollTop)  - offsetY);
                chartDef.posX = Math.round(x);
                chartDef.posY = Math.round(y);

                const chart = this.charts.find(c => c.id === chartDef.id);
                if (chart) {
                    chart.posX = chartDef.posX;
                    chart.posY = chartDef.posY;
                    // Persist position change
                    fetch(`/api/chart/${chartDef.id}`, {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(chart)
                    }).catch(err => console.warn('Could not persist chart position:', err));
                }
            };

            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
        });
    }

    _makeCardResizable(card, chartDef) {
        const handle = card.querySelector('.chart-resize-handle');
        if (!handle) return;

        handle.addEventListener('mousedown', (e) => {
            e.preventDefault();
            e.stopPropagation();

            const startX = e.clientX;
            const startY = e.clientY;
            const startW = card.offsetWidth;
            const canvasWrap = card.querySelector('.chart-canvas-wrap');
            const startH = canvasWrap ? canvasWrap.offsetHeight : (parseInt(chartDef.height) || 300);

            card.classList.add('resizing');

            const onMouseMove = (ev) => {
                const newW = Math.max(200, startW + (ev.clientX - startX));
                const newH = Math.max(150, startH + (ev.clientY - startY));
                card.style.width = newW + 'px';
                if (canvasWrap) canvasWrap.style.height = newH + 'px';
            };

            const onMouseUp = (ev) => {
                card.classList.remove('resizing');
                document.removeEventListener('mousemove', onMouseMove);
                document.removeEventListener('mouseup', onMouseUp);

                const newW = Math.max(200, startW + (ev.clientX - startX));
                const newH = Math.max(150, startH + (ev.clientY - startY));

                chartDef.width = this.pixelsToCols(newW);
                chartDef.height = Math.round(newH);

                const chart = this.charts.find(c => c.id === chartDef.id);
                if (chart) {
                    chart.width = chartDef.width;
                    chart.height = chartDef.height;
                    fetch(`/api/chart/${chartDef.id}`, {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(chart)
                    }).then(() => {
                        const canvasEl = card.querySelector('canvas');
                        if (canvasEl) window.chartRenderer.render(chart, canvasEl);
                    }).catch(err => console.warn('Could not persist chart resize:', err));
                }
            };

            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
        });
    }

    pixelsToCols(px) {
        const baseWidth = Math.min(window.innerWidth - 600, 900);
        return Math.max(2, Math.min(12, Math.round((px / baseWidth) * 12)));
    }

    // ── Page management ────────────────────────────────────────────

    renderPageTabs() {
        const container = document.getElementById('page-tabs');
        if (!container) return;
        container.innerHTML = '';
        this.pages.forEach((page, index) => {
            const tab = document.createElement('div');
            tab.className = 'page-tab' + (index === this.activePageIndex ? ' active' : '');
            tab.textContent = page.name;
            tab.title = 'Double-click to rename';
            tab.addEventListener('click', () => this.switchPage(index));
            tab.addEventListener('dblclick', (e) => { e.stopPropagation(); this.renamePage(index); });
            if (this.pages.length > 1) {
                const closeBtn = document.createElement('span');
                closeBtn.className = 'page-tab-close';
                closeBtn.innerHTML = '&times;';
                closeBtn.title = 'Delete page';
                closeBtn.addEventListener('click', (e) => { e.stopPropagation(); this.deletePage(index); });
                tab.appendChild(closeBtn);
            }
            container.appendChild(tab);
        });
    }

    switchPage(index) {
        if (index === this.activePageIndex) return;
        // Save current charts back to the current page
        this.pages[this.activePageIndex].charts = this.charts;
        this.activePageIndex = index;
        this.charts = this.pages[index].charts || [];
        this.renderAll();
        this.renderPageTabs();
        fetch(`/api/page/switch/${index}`, { method: 'POST' })
            .catch(err => console.warn('Could not persist page switch:', err));
    }

    async addPage() {
        const newPage = { name: this._getNextPageName(), charts: [] };
        this.pages.push(newPage);
        try {
            const resp = await fetch('/api/page/add', { method: 'POST' });
            if (resp.ok) {
                const data = await resp.json();
                newPage.name = data.name || newPage.name;
            }
        } catch (err) {
            console.warn('Could not persist page add:', err);
        }
        this.switchPage(this.pages.length - 1);
    }

    _getNextPageName() {
        const usedNumbers = new Set();
        this.pages.forEach(p => {
            const match = p.name.match(/^Page\s+(\d+)$/i);
            if (match) usedNumbers.add(parseInt(match[1]));
        });
        let n = 1;
        while (usedNumbers.has(n)) n++;
        return `Page ${n}`;
    }

    renamePage(index) {
        const current = this.pages[index]?.name || `Page ${index + 1}`;
        const name = prompt('Rename page:', current);
        if (!name || !name.trim()) return;
        this.pages[index].name = name.trim();
        this.renderPageTabs();
        fetch('/api/page/rename', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ index, name: name.trim() })
        }).catch(err => console.warn('Could not persist page rename:', err));
    }

    deletePage(index) {
        if (this.pages.length <= 1) { alert('Cannot delete the only page.'); return; }
        if (!confirm(`Delete "${this.pages[index].name}"? All charts on this page will be lost.`)) return;
        this.pages.splice(index, 1);
        if (this.activePageIndex >= this.pages.length) this.activePageIndex = this.pages.length - 1;
        this.charts = this.pages[this.activePageIndex].charts || [];
        this.renderAll();
        this.renderPageTabs();
        fetch(`/api/page/${index}`, { method: 'DELETE' })
            .catch(err => console.warn('Could not persist page delete:', err));
    }

    updateEmptyState() {
        const empty = document.getElementById('canvas-empty-state');
        if (empty) empty.style.display = this.charts.length === 0 ? 'flex' : 'none';
    }

    async resetCanvas() {
        if (!confirm('Reset canvas to default charts?')) return;
        const resp = await fetch('/api/chart/reset', { method: 'POST' });
        const canvas = await resp.json();
        this.charts = canvas.charts || [];
        this.renderAll();
    }
}

window.canvasManager = new CanvasManager();
