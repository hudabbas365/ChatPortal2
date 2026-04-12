// Layout Manager — alignment, distribution, snap-to-grid, z-order, auto-arrange
class LayoutManager {
    constructor() {
        this._snapToGrid = false;
        this._gridSize = 24; // matches CSS background-size of chart-grid
    }

    init() {
        this._bindButtons();
    }

    // ── Snap to grid ────────────────────────────────────────────

    get snapToGrid() { return this._snapToGrid; }
    get gridSize() { return this._gridSize; }

    toggleSnap() {
        this._snapToGrid = !this._snapToGrid;
        const btn = document.getElementById('btn-snap-grid');
        if (btn) btn.classList.toggle('snap-active', this._snapToGrid);
        if (this._snapToGrid) this._snapAllToGrid();
    }

    /** Snap a position value to the nearest grid line. */
    snapValue(val) {
        if (!this._snapToGrid) return val;
        return Math.round(val / this._gridSize) * this._gridSize;
    }

    _snapAllToGrid() {
        const charts = window.canvasManager?.charts || [];
        charts.forEach(chart => {
            chart.posX = this.snapValue(chart.posX);
            chart.posY = this.snapValue(chart.posY);
            const card = document.querySelector(`.chart-card[data-chart-id="${chart.id}"]`);
            if (card) {
                card.style.left = chart.posX + 'px';
                card.style.top = chart.posY + 'px';
            }
            fetch(`/api/chart/${chart.id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(chart)
            }).catch(err => console.warn('Could not persist snap:', err));
        });
        if (window.groupManager) window.groupManager._renderGroupOutlines();
    }

    // ── Alignment ───────────────────────────────────────────────

    _getSelectedCards() {
        const ids = window.groupManager?.selectedIds || [];
        return ids.map(id => {
            const card = document.querySelector(`.chart-card[data-chart-id="${id}"]`);
            const chart = window.canvasManager?.charts.find(c => c.id === id);
            return card && chart ? { card, chart, id } : null;
        }).filter(Boolean);
    }

    alignLeft() {
        const items = this._getSelectedCards();
        if (items.length < 2) return;
        const minX = Math.min(...items.map(i => i.chart.posX));
        items.forEach(i => this._setPos(i, minX, i.chart.posY));
    }

    alignCenter() {
        const items = this._getSelectedCards();
        if (items.length < 2) return;
        const centers = items.map(i => i.chart.posX + i.card.offsetWidth / 2);
        const avgCenter = centers.reduce((a, b) => a + b, 0) / centers.length;
        items.forEach(i => this._setPos(i, Math.round(avgCenter - i.card.offsetWidth / 2), i.chart.posY));
    }

    alignRight() {
        const items = this._getSelectedCards();
        if (items.length < 2) return;
        const maxRight = Math.max(...items.map(i => i.chart.posX + i.card.offsetWidth));
        items.forEach(i => this._setPos(i, maxRight - i.card.offsetWidth, i.chart.posY));
    }

    alignTop() {
        const items = this._getSelectedCards();
        if (items.length < 2) return;
        const minY = Math.min(...items.map(i => i.chart.posY));
        items.forEach(i => this._setPos(i, i.chart.posX, minY));
    }

    alignMiddle() {
        const items = this._getSelectedCards();
        if (items.length < 2) return;
        const middles = items.map(i => i.chart.posY + i.card.offsetHeight / 2);
        const avgMid = middles.reduce((a, b) => a + b, 0) / middles.length;
        items.forEach(i => this._setPos(i, i.chart.posX, Math.round(avgMid - i.card.offsetHeight / 2)));
    }

    alignBottom() {
        const items = this._getSelectedCards();
        if (items.length < 2) return;
        const maxBottom = Math.max(...items.map(i => i.chart.posY + i.card.offsetHeight));
        items.forEach(i => this._setPos(i, i.chart.posX, maxBottom - i.card.offsetHeight));
    }

    // ── Distribution ────────────────────────────────────────────

    distributeHorizontal() {
        const items = this._getSelectedCards();
        if (items.length < 3) return;
        items.sort((a, b) => a.chart.posX - b.chart.posX);

        const first = items[0];
        const last = items[items.length - 1];
        const totalWidth = items.reduce((sum, i) => sum + i.card.offsetWidth, 0);
        const totalSpace = (last.chart.posX + last.card.offsetWidth) - first.chart.posX - totalWidth;
        const gap = totalSpace / (items.length - 1);

        let currentX = first.chart.posX;
        items.forEach(i => {
            this._setPos(i, Math.round(currentX), i.chart.posY);
            currentX += i.card.offsetWidth + gap;
        });
    }

    distributeVertical() {
        const items = this._getSelectedCards();
        if (items.length < 3) return;
        items.sort((a, b) => a.chart.posY - b.chart.posY);

        const first = items[0];
        const last = items[items.length - 1];
        const totalHeight = items.reduce((sum, i) => sum + i.card.offsetHeight, 0);
        const totalSpace = (last.chart.posY + last.card.offsetHeight) - first.chart.posY - totalHeight;
        const gap = totalSpace / (items.length - 1);

        let currentY = first.chart.posY;
        items.forEach(i => {
            this._setPos(i, i.chart.posX, Math.round(currentY));
            currentY += i.card.offsetHeight + gap;
        });
    }

    // ── Same Size ───────────────────────────────────────────────

    sameWidth() {
        const items = this._getSelectedCards();
        if (items.length < 2) return;
        const maxW = Math.max(...items.map(i => i.card.offsetWidth));
        items.forEach(i => {
            i.chart.width = window.canvasManager?.pixelsToCols(maxW) || i.chart.width;
            i.card.style.width = maxW + 'px';
            this._persist(i.chart);
        });
    }

    sameHeight() {
        const items = this._getSelectedCards();
        if (items.length < 2) return;
        const maxH = Math.max(...items.map(i => {
            const wrap = i.card.querySelector('.chart-canvas-wrap');
            return wrap ? wrap.offsetHeight : 300;
        }));
        items.forEach(i => {
            i.chart.height = maxH;
            const wrap = i.card.querySelector('.chart-canvas-wrap');
            if (wrap) wrap.style.height = maxH + 'px';
            this._persist(i.chart);
        });
    }

    // ── Z-Order ─────────────────────────────────────────────────

    bringForward() {
        const items = this._getSelectedCards();
        items.forEach(i => {
            i.chart.zIndex = ++window.canvasManager._maxZIndex;
            i.card.style.zIndex = i.chart.zIndex;
            this._persist(i.chart);
        });
    }

    sendBackward() {
        const items = this._getSelectedCards();
        items.forEach(i => {
            i.chart.zIndex = Math.max(1, i.chart.zIndex - 1);
            i.card.style.zIndex = i.chart.zIndex;
            this._persist(i.chart);
        });
    }

    // ── Auto-arrange (grid layout) ──────────────────────────────

    autoArrange() {
        const charts = window.canvasManager?.charts || [];
        if (charts.length === 0) return;

        const cols = Math.max(1, Math.ceil(Math.sqrt(charts.length)));
        const padding = 24;
        const cardWidth = 380;
        const cardHeight = 340;

        charts.forEach((chart, i) => {
            const col = i % cols;
            const row = Math.floor(i / cols);
            chart.posX = padding + col * (cardWidth + padding);
            chart.posY = padding + row * (cardHeight + padding);

            if (this._snapToGrid) {
                chart.posX = this.snapValue(chart.posX);
                chart.posY = this.snapValue(chart.posY);
            }

            const card = document.querySelector(`.chart-card[data-chart-id="${chart.id}"]`);
            if (card) {
                card.style.left = chart.posX + 'px';
                card.style.top = chart.posY + 'px';
            }
            this._persist(chart);
        });
        if (window.groupManager) window.groupManager._renderGroupOutlines();
    }

    // ── Helpers ──────────────────────────────────────────────────

    _setPos(item, x, y) {
        if (this._snapToGrid) {
            x = this.snapValue(x);
            y = this.snapValue(y);
        }
        item.chart.posX = Math.max(0, Math.round(x));
        item.chart.posY = Math.max(0, Math.round(y));
        item.card.style.left = item.chart.posX + 'px';
        item.card.style.top = item.chart.posY + 'px';
        this._persist(item.chart);
    }

    _persist(chart) {
        fetch(`/api/chart/${chart.id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(chart)
        }).catch(err => console.warn('Could not persist layout:', err));
    }

    // ── Button bindings ─────────────────────────────────────────

    _bindButtons() {
        const bind = (id, fn) => {
            const btn = document.getElementById(id);
            if (btn) btn.addEventListener('click', () => fn.call(this));
        };

        // Align
        bind('btn-align-left', this.alignLeft);
        bind('btn-align-center', this.alignCenter);
        bind('btn-align-right', this.alignRight);
        bind('btn-align-top', this.alignTop);
        bind('btn-align-middle', this.alignMiddle);
        bind('btn-align-bottom', this.alignBottom);

        // Distribute
        bind('btn-dist-h', this.distributeHorizontal);
        bind('btn-dist-v', this.distributeVertical);

        // Same size
        bind('btn-same-width', this.sameWidth);
        bind('btn-same-height', this.sameHeight);

        // Z-order
        bind('btn-bring-forward', this.bringForward);
        bind('btn-send-backward', this.sendBackward);

        // Auto arrange
        bind('btn-auto-arrange', this.autoArrange);

        // Snap to grid
        bind('btn-snap-grid', this.toggleSnap);

        // Group / Ungroup
        bind('btn-group-items', () => window.groupManager?.group());
        bind('btn-ungroup-items', () => window.groupManager?.ungroup());

        // Update group outlines after layout changes
        const layoutBtns = document.querySelectorAll('.layout-btn');
        layoutBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                setTimeout(() => {
                    if (window.groupManager) window.groupManager._renderGroupOutlines();
                }, 50);
            });
        });
    }
}

window.layoutManager = new LayoutManager();
