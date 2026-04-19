// Canvas manager - manages charts on a free-form absolute-positioned canvas

// Helper: append ?ctx= to chart/page API URLs for session key isolation
function _chartApiUrl(path) {
    const p = new URLSearchParams(window.location.search);
    const ctx = p.get('report') || p.get('workspace') || p.get('ws') || '';
    return ctx ? path + (path.includes('?') ? '&' : '?') + 'ctx=' + encodeURIComponent(ctx) : path;
}

class CanvasManager {
    constructor() {
        this.charts = [];
        this.pages = [];
        this.activePageIndex = 0;
        this.selectedChartId = null;
        this._maxZIndex = 1;
        this._dragState = null;
        this._addingPage = false;
        this._undoStack = [];
        this._maxUndo = 30;
    }

    init(initialCharts, pages, activePageIndex) {
        if (pages && pages.length > 0) {
            this.pages = pages;
            this.activePageIndex = activePageIndex || 0;
            // Ensure the page has a charts array so this.charts is always a reference to it
            if (!this.pages[this.activePageIndex].charts) this.pages[this.activePageIndex].charts = [];
            this.charts = this.pages[this.activePageIndex].charts;
        } else {
            this.pages = [{ name: 'Page 1', charts: initialCharts || [] }];
            this.activePageIndex = 0;
            this.charts = this.pages[0].charts;
        }
        this.renderAll();
        this.renderPageTabs();
        this.initDropZone();

        // Restore group outlines from chart definitions
        if (window.groupManager) window.groupManager.restoreGroups(this.charts);

        const addBtn = document.getElementById('add-page-btn');
        if (addBtn) addBtn.addEventListener('click', () => this.addPage());

        // Ctrl+Z undo support
        document.addEventListener('keydown', (e) => {
            if ((e.ctrlKey || e.metaKey) && e.key === 'z' && !e.shiftKey) {
                e.preventDefault();
                this.undo();
            }
        });
    }

    // ── Undo support ──────────────────────────────────────────────────
    _pushUndo() {
        const snapshot = JSON.stringify(this.pages);
        this._undoStack.push({ pages: snapshot, activePageIndex: this.activePageIndex });
        if (this._undoStack.length > this._maxUndo) this._undoStack.shift();
    }

    undo() {
        if (this._undoStack.length === 0) return;
        const state = this._undoStack.pop();
        try {
            this.pages = JSON.parse(state.pages);
            this.activePageIndex = state.activePageIndex;
            if (!this.pages[this.activePageIndex]) this.activePageIndex = 0;
            this.charts = this.pages[this.activePageIndex].charts || [];
            this.renderAll();
            this.renderPageTabs();
        } catch (e) { console.warn('Undo failed:', e); }
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
        if (this._pageSwitchPromise) await this._pageSwitchPromise;
        this._pushUndo();
        const isShape = window.ShapeManager && ShapeManager.isShape(partial.chartType);
        const isNavigation = partial.chartType === 'navigation';
        const defaultX = 20 + (this.charts.length % 5) * 30;
        const defaultY = 20 + (this.charts.length % 5) * 30;
        // Resolve default dataset: prefer first real table from datasource dropdown, else 'sales'
        // When a dataQuery is provided (e.g. from Chat transfer), skip the default so chartRenderer uses the query path
        let defaultDataset = partial.datasetName || (partial.dataQuery ? '' : 'sales');
        if (isNavigation) defaultDataset = '';
        if (!isNavigation && !partial.datasetName && !partial.dataQuery && window.currentDatasourceId) {
            const dsSel = document.getElementById('prop-dataset');
            if (dsSel && dsSel.options.length > 0) defaultDataset = dsSel.options[0].value;
        }

        const chart = {
            id: 'c' + Date.now(),
            chartType: partial.chartType || 'bar',
            title: partial.title || (isShape ? partial.chartType.replace('shape-', '').replace(/-/g, ' ') : isNavigation ? 'Navigation Link' : 'New Chart'),
            datasetName: defaultDataset,
            datasourceId: partial.datasourceId || window.currentDatasourceId || null,
            dataQuery: partial.dataQuery || null,
            width: partial.width || (isShape || isNavigation ? 3 : 6),
            height: partial.height || (isShape ? 180 : isNavigation ? 80 : 300),
            gridCol: 0,
            gridRow: 0,
            posX: partial.posX !== undefined ? partial.posX : defaultX,
            posY: partial.posY !== undefined ? partial.posY : defaultY,
            zIndex: ++this._maxZIndex,
            mapping: partial.mapping || { labelField: '', valueField: '', groupByField: '', xField: '', yField: '', rField: '', multiValueFields: [], tableFields: [] },
            aggregation: partial.aggregation || { function: isNavigation ? 'None' : 'SUM', enabled: isNavigation ? false : !partial.customJsonData },
            style: partial.style || { backgroundColor: '#4A90D9', borderColor: '#2C6FAC', showLegend: true, legendPosition: 'top', showTooltips: true, fillArea: false, colorPalette: 'default', showDataLabels: false, fontFamily: 'Inter, sans-serif', titleFontSize: 14, animated: true, responsive: true, borderRadius: '4' },
            customJsonData: partial.customJsonData || '',
            rowLimit: partial.rowLimit || 100,
            filterWhere: partial.filterWhere || '',
            shapeProps: partial.shapeProps || (isShape ? ShapeManager.getDefaultShapeProps(partial.chartType) : null),
            navigation: partial.navigation || (isNavigation ? {
                label: partial.title || 'Open Link',
                target: 'current',
                customUrl: '',
                borderEnabled: true,
                borderColor: '#4A90D9',
                borderRadius: 8,
                backgroundColor: '#ffffff'
            } : null)
        };

        const resp = await fetch(_chartApiUrl('/api/chart/add'), {
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
        if (this._pageSwitchPromise) await this._pageSwitchPromise;
        this._pushUndo();
        await fetch(_chartApiUrl(`/api/chart/${chartId}`), { method: 'DELETE' });
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

    selectChart(chartId, ctrlKey) {
        if (ctrlKey && window.groupManager) {
            window.groupManager.toggleSelect(chartId);
            this.selectedChartId = chartId;
            document.dispatchEvent(new CustomEvent('chart:selected', { detail: { chartId } }));
            return;
        }
        this.selectedChartId = chartId;
        document.querySelectorAll('.chart-card').forEach(c => c.classList.remove('selected'));
        const card = document.querySelector(`.chart-card[data-chart-id="${chartId}"]`);
        if (card) {
            card.classList.add('selected');
            const chart = this.charts.find(c => c.id === chartId);
            if (chart) {
                chart.zIndex = ++this._maxZIndex;
                card.style.zIndex = chart.zIndex;
            }
        }
        const chart = this.charts.find(c => c.id === chartId);
        if (chart && window.propertiesPanel) window.propertiesPanel.load(chart);
        if (window.groupManager) window.groupManager.selectOne(chartId);
        document.dispatchEvent(new CustomEvent('chart:selected', { detail: { chartId } }));
    }

    async updateChart(chartDef) {
        if (this._pageSwitchPromise) await this._pageSwitchPromise;
        const idx = this.charts.findIndex(c => c.id === chartDef.id);
        if (idx >= 0) this.charts[idx] = chartDef;

        await fetch(_chartApiUrl(`/api/chart/${chartDef.id}`), {
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
            if (window.ShapeManager && ShapeManager.isShape(chartDef.chartType)) {
                if (canvasWrap) ShapeManager.render(canvasWrap, chartDef);
            } else {
                const canvasEl = card.querySelector('canvas');
                if (canvasEl) await window.chartRenderer.render(chartDef, canvasEl);
            }
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

        const isShape = window.ShapeManager && ShapeManager.isShape(chartDef.chartType);
        const isNavigation = chartDef.chartType === 'navigation';

        if (isShape) {
            card.classList.add('shape-card');
            card.innerHTML = `
                <div class="shape-hover-actions">
                    <button class="btn btn-xs btn-icon" data-action="edit" title="Edit"><i class="bi bi-pencil"></i></button>
                    <button class="btn btn-xs btn-icon" data-action="duplicate" title="Duplicate"><i class="bi bi-copy"></i></button>
                    <button class="btn btn-xs btn-icon text-danger" data-action="delete" title="Delete"><i class="bi bi-trash"></i></button>
                </div>
                <div class="chart-canvas-wrap" style="height: ${parseInt(chartDef.height) || 300}px"></div>
                <div class="chart-resize-handle" title="Drag to resize"></div>
            `;
        } else if (isNavigation) {
            card.classList.add('navigation-card');
            card.innerHTML = `
                <div class="shape-hover-actions">
                    <button class="btn btn-xs btn-icon" data-action="edit" title="Edit"><i class="bi bi-pencil"></i></button>
                    <button class="btn btn-xs btn-icon" data-action="duplicate" title="Duplicate"><i class="bi bi-copy"></i></button>
                    <button class="btn btn-xs btn-icon text-danger" data-action="delete" title="Delete"><i class="bi bi-trash"></i></button>
                </div>
                <div class="chart-canvas-wrap" style="height: ${parseInt(chartDef.height) || 80}px"></div>
                <div class="chart-resize-handle" title="Drag to resize"></div>
            `;
        } else {
            card.innerHTML = `
                <div class="chart-card-header">
                    <i class="bi bi-grip-vertical chart-drag-handle text-muted me-2" title="Drag to reposition"></i>
                    <span class="chart-title" title="Double-click to edit title">${safeTitle}</span>
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
        }

        // ── Inline title editing on double-click ──
        const titleEl = card.querySelector('.chart-title');
        if (titleEl) {
            titleEl.addEventListener('dblclick', (e) => {
                e.stopPropagation();
                if (titleEl.contentEditable === 'true') return;
                titleEl.contentEditable = 'true';
                titleEl.classList.add('editing');
                titleEl.focus();
                // Select all text
                const range = document.createRange();
                range.selectNodeContents(titleEl);
                const sel = window.getSelection();
                sel.removeAllRanges();
                sel.addRange(range);
            });
            const commitTitle = () => {
                if (titleEl.contentEditable !== 'true') return;
                titleEl.contentEditable = 'false';
                titleEl.classList.remove('editing');
                const newTitle = titleEl.textContent.trim() || chartDef.title;
                titleEl.textContent = newTitle;
                if (newTitle !== chartDef.title) {
                    chartDef.title = newTitle;
                    const idx = this.charts.findIndex(c => c.id === chartDef.id);
                    if (idx >= 0) this.charts[idx].title = newTitle;
                    this.updateChart(chartDef);
                    if (window.propertiesPanel && this.selectedChartId === chartDef.id) {
                        window.propertiesPanel.load(chartDef);
                    }
                }
            };
            titleEl.addEventListener('blur', commitTitle);
            titleEl.addEventListener('keydown', (e) => {
                if (e.key === 'Enter') { e.preventDefault(); titleEl.blur(); }
                if (e.key === 'Escape') { titleEl.textContent = chartDef.title; titleEl.blur(); }
            });
        }

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
            if (!e.target.closest('button')) this.selectChart(chartDef.id, e.ctrlKey || e.metaKey);
        });

        container.appendChild(card);

        // Make draggable
        this._makeCardDraggable(card, chartDef);
        // Make resizable
        this._makeCardResizable(card, chartDef);

        if (isShape) {
            const canvasWrap = card.querySelector('.chart-canvas-wrap');
            requestAnimationFrame(() => ShapeManager.render(canvasWrap, chartDef));
        } else if (isNavigation) {
            const canvasWrap = card.querySelector('.chart-canvas-wrap');
            requestAnimationFrame(() => window.chartRenderer.render(chartDef, canvasWrap));
        } else {
            const canvasEl = card.querySelector('canvas');
            requestAnimationFrame(() => window.chartRenderer.render(chartDef, canvasEl));
        }
    }

    _makeCardDraggable(card, chartDef) {
        const isShape = window.ShapeManager && ShapeManager.isShape(chartDef.chartType);
        const isNavigation = chartDef.chartType === 'navigation';
        const useWholeCard = isShape || isNavigation;
        const handle = useWholeCard ? card : card.querySelector('.chart-drag-handle');
        if (!handle) return;

        handle.addEventListener('mousedown', (e) => {
            // For shapes/navigation, ignore clicks on action buttons and resize handle
            if (useWholeCard && (e.target.closest('button') || e.target.closest('.chart-resize-handle'))) return;
            e.preventDefault();
            e.stopPropagation();

            // Preserve multi-selection: only re-select if this card is NOT already selected
            const gm = window.groupManager;
            if (gm && gm.isSelected(chartDef.id) && gm.selectedCount > 1) {
                // Already part of a multi-selection — keep it, just set as primary
                this.selectedChartId = chartDef.id;
            } else {
                this.selectChart(chartDef.id);
            }

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

            // Snapshot start position for drag (handles multi-select + group)
            const startPosX = chartDef.posX;
            const startPosY = chartDef.posY;
            if (gm) gm.snapshotDragPositions(chartDef.id);

            card.classList.add('dragging');

            const onMouseMove = (ev) => {
                const sl = scrollEl ? scrollEl.scrollLeft : 0;
                const st = scrollEl ? scrollEl.scrollTop  : 0;
                let x = Math.max(0, ev.clientX - containerBase.left + (sl - scrollLeft) - offsetX);
                let y = Math.max(0, ev.clientY - containerBase.top  + (st - scrollTop)  - offsetY);
                // Snap to grid
                if (window.layoutManager && window.layoutManager.snapToGrid) {
                    x = window.layoutManager.snapValue(x);
                    y = window.layoutManager.snapValue(y);
                }
                card.style.left = x + 'px';
                card.style.top  = y + 'px';
                // Move all drag siblings (multi-selected + group members)
                if (gm) {
                    const dx = x - startPosX;
                    const dy = y - startPosY;
                    gm.moveDragSiblings(chartDef.id, dx, dy);
                }
            };

            const onMouseUp = (ev) => {
                card.classList.remove('dragging');
                document.removeEventListener('mousemove', onMouseMove);
                document.removeEventListener('mouseup', onMouseUp);

                const sl = scrollEl ? scrollEl.scrollLeft : 0;
                const st = scrollEl ? scrollEl.scrollTop  : 0;
                let x = Math.max(0, ev.clientX - containerBase.left + (sl - scrollLeft) - offsetX);
                let y = Math.max(0, ev.clientY - containerBase.top  + (st - scrollTop)  - offsetY);
                if (window.layoutManager && window.layoutManager.snapToGrid) {
                    x = window.layoutManager.snapValue(x);
                    y = window.layoutManager.snapValue(y);
                }
                chartDef.posX = Math.round(x);
                chartDef.posY = Math.round(y);

                const chart = this.charts.find(c => c.id === chartDef.id);
                if (chart) {
                    chart.posX = chartDef.posX;
                    chart.posY = chartDef.posY;
                    // Persist position change
                    fetch(_chartApiUrl(`/api/chart/${chartDef.id}`), {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(chart)
                    }).catch(err => console.warn('Could not persist chart position:', err));
                }
                // Finalize all drag sibling positions (multi-selected + group)
                if (gm) {
                    const dx = Math.round(x) - startPosX;
                    const dy = Math.round(y) - startPosY;
                    gm.finalizeDragSiblings(chartDef.id, dx, dy);
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
                    fetch(_chartApiUrl(`/api/chart/${chartDef.id}`), {
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
        // Ensure the target page has a charts array so this.charts references it
        if (!this.pages[index].charts) this.pages[index].charts = [];
        this.charts = this.pages[index].charts;
        this.renderAll();
        this.renderPageTabs();
        this._pageSwitchPromise = fetch(_chartApiUrl(`/api/page/switch/${index}`), { method: 'POST' })
            .catch(err => console.warn('Could not persist page switch:', err))
            .finally(() => { this._pageSwitchPromise = null; });
    }

    async addPage() {
        if (this._addingPage) return;
        this._addingPage = true;
        try {
            const newPage = { name: this._getNextPageName(), charts: [] };
            this.pages.push(newPage);
            try {
                const resp = await fetch(_chartApiUrl('/api/page/add'), { method: 'POST' });
                if (resp.ok) {
                    const data = await resp.json();
                    newPage.name = data.name || newPage.name;
                }
            } catch (err) {
                console.warn('Could not persist page add:', err);
            }
            this.switchPage(this.pages.length - 1);
        } finally {
            this._addingPage = false;
        }
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
        fetch(_chartApiUrl('/api/page/rename'), {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ index, name: name.trim() })
        }).catch(err => console.warn('Could not persist page rename:', err));
    }

    async deletePage(index) {
        if (this.pages.length <= 1) { alert('Cannot delete the only page.'); return; }
        var confirmed = await this._showDeletePageConfirm(this.pages[index].name);
        if (!confirmed) return;
        this.pages.splice(index, 1);
        if (this.activePageIndex >= this.pages.length) this.activePageIndex = this.pages.length - 1;
        this.charts = this.pages[this.activePageIndex].charts || [];
        this.renderAll();
        this.renderPageTabs();
        fetch(_chartApiUrl(`/api/page/${index}`), { method: 'DELETE' })
            .catch(err => console.warn('Could not persist page delete:', err));
    }

    _showDeletePageConfirm(pageName) {
        return new Promise(function (resolve) {
            var overlay = document.createElement('div');
            overlay.className = 'ar-overlay';
            overlay.innerHTML =
                '<div class="ar-modal" style="width:420px">' +
                    '<div class="ar-modal-header" style="background:linear-gradient(135deg,#dc3545 0%,#c82333 100%)">' +
                        '<i class="bi bi-trash3-fill"></i>Delete Page' +
                    '</div>' +
                    '<div class="ar-modal-body" style="text-align:center;padding:24px 20px">' +
                        '<i class="bi bi-file-earmark-x" style="font-size:2.2rem;color:#dc3545;display:block;margin-bottom:12px"></i>' +
                        '<div style="font-size:0.88rem;font-weight:600;color:var(--cp-text,#1e2d3d);margin-bottom:6px">Delete \u201c' + (typeof escapeHtml === 'function' ? escapeHtml(pageName) : pageName.replace(/</g,'&lt;').replace(/>/g,'&gt;')) + '\u201d?</div>' +
                        '<div style="font-size:0.78rem;color:var(--cp-text-secondary,#6c757d)">All charts on this page will be permanently removed.<br>This action cannot be undone.</div>' +
                    '</div>' +
                    '<div class="ar-modal-footer" style="justify-content:center;gap:12px">' +
                        '<button class="ar-cancel-btn" id="arDelPageNo" style="min-width:90px">Cancel</button>' +
                        '<button class="ar-generate-btn" id="arDelPageYes" style="min-width:90px;background:#dc3545"><i class="bi bi-trash3 me-1"></i>Delete</button>' +
                    '</div>' +
                '</div>';
            document.body.appendChild(overlay);
            requestAnimationFrame(function () { overlay.classList.add('ar-open'); });

            function close(result) {
                overlay.classList.remove('ar-open');
                setTimeout(function () { overlay.remove(); }, 260);
                resolve(result);
            }

            document.getElementById('arDelPageYes').addEventListener('click', function () { close(true); });
            document.getElementById('arDelPageNo').addEventListener('click', function () { close(false); });
            overlay.addEventListener('click', function (e) { if (e.target === overlay) close(false); });
        });
    }

    async deleteAllPages() {
        // Clear all pages on server and reset to single empty page
        try { await fetch(_chartApiUrl('/api/chart/reset-all'), { method: 'POST' }); } catch (e) {}
        this.pages = [{ name: 'Page 1', charts: [] }];
        this.activePageIndex = 0;
        this.charts = this.pages[0].charts;
        this.renderAll();
        this.renderPageTabs();
    }

    updateEmptyState() {
        const empty = document.getElementById('canvas-empty-state');
        if (empty) empty.style.display = this.charts.length === 0 ? 'flex' : 'none';
    }

    async resetCanvas(skipConfirm) {
        if (!skipConfirm) {
            var confirmed = await this._showResetConfirm();
            if (!confirmed) return;
        }
        this._pushUndo();
        const resp = await fetch(_chartApiUrl('/api/chart/reset'), { method: 'POST' });
        const canvas = await resp.json();
        this.charts = canvas.charts || [];
        this.pages[this.activePageIndex].charts = this.charts;
        this.renderAll();
    }

    _showResetConfirm() {
        return new Promise(function (resolve) {
            var overlay = document.createElement('div');
            overlay.className = 'ar-overlay';
            overlay.innerHTML =
                '<div class="ar-modal" style="width:400px">' +
                    '<div class="ar-modal-header" style="background:linear-gradient(135deg,#dc3545 0%,#c82333 100%)">' +
                        '<i class="bi bi-exclamation-triangle-fill"></i>Reset Canvas' +
                    '</div>' +
                    '<div class="ar-modal-body" style="text-align:center;padding:24px 20px">' +
                        '<i class="bi bi-arrow-counterclockwise" style="font-size:2.2rem;color:#dc3545;display:block;margin-bottom:12px"></i>' +
                        '<div style="font-size:0.88rem;font-weight:600;color:var(--cp-text,#1e2d3d);margin-bottom:6px">Reset canvas to default?</div>' +
                        '<div style="font-size:0.78rem;color:var(--cp-text-secondary,#6c757d)">All charts on the current page will be removed.<br>This action can be undone with <kbd style="background:var(--cp-bg-alt,#e9ecef);padding:1px 5px;border-radius:3px;font-size:0.72rem">Ctrl+Z</kbd></div>' +
                    '</div>' +
                    '<div class="ar-modal-footer" style="justify-content:center;gap:12px">' +
                        '<button class="ar-cancel-btn" id="arResetNo" style="min-width:90px">Cancel</button>' +
                        '<button class="ar-generate-btn" id="arResetYes" style="min-width:90px;background:#dc3545"><i class="bi bi-arrow-counterclockwise me-1"></i>Reset</button>' +
                    '</div>' +
                '</div>';
            document.body.appendChild(overlay);
            requestAnimationFrame(function () { overlay.classList.add('ar-open'); });

            function close(result) {
                overlay.classList.remove('ar-open');
                setTimeout(function () { overlay.remove(); }, 260);
                resolve(result);
            }

            document.getElementById('arResetYes').addEventListener('click', function () { close(true); });
            document.getElementById('arResetNo').addEventListener('click', function () { close(false); });
            overlay.addEventListener('click', function (e) { if (e.target === overlay) close(false); });
        });
    }
}

window.canvasManager = new CanvasManager();

// ---- Cross-filtering global state ----
window.CrossFilter = {
    activeFilter: null,  // { field, value, label }

    apply(field, value, label) {
        this.activeFilter = { field, value, label };
        this._renderBadge();
        document.dispatchEvent(new CustomEvent('crossfilter:change', { detail: this.activeFilter }));
    },

    clear() {
        this.activeFilter = null;
        this._renderBadge();
        document.dispatchEvent(new CustomEvent('crossfilter:change', { detail: null }));
    },

    _renderBadge() {
        let badge = document.getElementById('crossfilter-badge');
        if (!this.activeFilter) {
            if (badge) badge.remove();
            return;
        }
        if (!badge) {
            badge = document.createElement('div');
            badge.id = 'crossfilter-badge';
            badge.className = 'crossfilter-badge';
            const toolbar = document.querySelector('.canvas-toolbar');
            if (toolbar) toolbar.appendChild(badge);
        }
        badge.innerHTML = `<i class="bi bi-funnel-fill me-1"></i>Filter: <strong>${this.activeFilter.field}</strong> = <strong>${this.activeFilter.label ?? this.activeFilter.value}</strong>
            <button class="btn btn-xs btn-ghost ms-2" id="crossfilter-clear-btn" title="Clear filter"><i class="bi bi-x-lg"></i></button>`;
        document.getElementById('crossfilter-clear-btn')?.addEventListener('click', () => this.clear());
    }
};
