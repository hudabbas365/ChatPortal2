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
        // After snapping, two cards can land on the same grid line — resolve any
        // residual overlaps by pushing the lower-priority card down just enough
        // to clear the collision, so "Snap to Grid" never leaves charts stacked.
        this._resolveOverlaps();
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

    // Phase 33-B10 — Tidy pack: lay selected charts out in a single row/column
    // with a uniform gap, starting at the topmost/leftmost selected position.
    // Works with 2+ selected items (unlike distribute which requires 3+).
    tidyRow(gap) {
        const items = this._getSelectedCards();
        if (items.length < 2) return;
        const g = (gap === undefined) ? 16 : gap;
        items.sort((a, b) => a.chart.posX - b.chart.posX);
        const startX = items[0].chart.posX;
        const y = items[0].chart.posY;
        let cx = startX;
        items.forEach(i => {
            this._setPos(i, Math.round(cx), y);
            cx += i.card.offsetWidth + g;
        });
    }

    tidyColumn(gap) {
        const items = this._getSelectedCards();
        if (items.length < 2) return;
        const g = (gap === undefined) ? 16 : gap;
        items.sort((a, b) => a.chart.posY - b.chart.posY);
        const startY = items[0].chart.posY;
        const x = items[0].chart.posX;
        let cy = startY;
        items.forEach(i => {
            this._setPos(i, x, Math.round(cy));
            cy += i.card.offsetHeight + g;
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

    // Pack charts left-to-right into rows using EACH card's real rendered
    // width/height so cards with different widths (KPIs, charts, full-width
    // tables) tile without overlap and without leaving huge gaps. Wrap to
    // the next row when the current card won't fit. Full-width cards (≥ 10
    // cols or > 75% of the canvas) always start — and close — their own row.
    autoArrange() {
        const cm = window.canvasManager;
        const charts = cm?.charts || [];
        if (charts.length === 0) return;

        const dropZone = document.getElementById('chart-canvas-drop');
        if (!dropZone) return;

        const margin = 20;
        const rowGap = 16;
        const colGap = 16;

        const scrollEl = dropZone.parentElement;
        const viewportW = scrollEl ? scrollEl.clientWidth : dropZone.clientWidth;
        const zoneW = dropZone.clientWidth;
        const canvasBase = Math.max(
            cm.colsToPixels ? cm.colsToPixels(12) : 900,
            Math.min(viewportW, zoneW) - margin * 2
        );

        // Preserve existing reading order (top-to-bottom, left-to-right)
        // so auto-arrange feels like "tidy up" rather than "shuffle".
        const ordered = charts.slice().sort((a, b) => {
            const ay = a.posY || 0, by = b.posY || 0;
            if (ay !== by) return ay - by;
            return (a.posX || 0) - (b.posX || 0);
        });

        let cursorX = margin;
        let rowY = margin;
        let rowMaxH = 0;

        ordered.forEach(chart => {
            const card = document.querySelector(`.chart-card[data-chart-id="${chart.id}"]`);
            let w = card ? card.offsetWidth
                : (cm.colsToPixels ? cm.colsToPixels(chart.width || 6) : 380);
            let h = card ? card.offsetHeight : ((chart.height || 300) + 44);
            if (w > canvasBase) w = canvasBase;

            const isFullWidth = (chart.width || 6) >= 10 || w > canvasBase * 0.75;
            if (isFullWidth && cursorX > margin) {
                rowY += rowMaxH + rowGap;
                cursorX = margin;
                rowMaxH = 0;
            }

            if (cursorX > margin && cursorX + w > margin + canvasBase) {
                rowY += rowMaxH + rowGap;
                cursorX = margin;
                rowMaxH = 0;
            }

            let newX = cursorX;
            let newY = rowY;
            if (this._snapToGrid) {
                newX = this.snapValue(newX);
                newY = this.snapValue(newY);
            }

            chart.posX = newX;
            chart.posY = newY;
            if (card) {
                card.style.left = newX + 'px';
                card.style.top = newY + 'px';
            }
            this._persist(chart);

            cursorX += w + colGap;
            if (h > rowMaxH) rowMaxH = h;

            if (isFullWidth) {
                rowY += rowMaxH + rowGap;
                cursorX = margin;
                rowMaxH = 0;
            }
        });

        // Grow the drop zone so the last row is scroll-reachable.
        const lastBottom = rowY + rowMaxH + margin;
        if (lastBottom > (dropZone.clientHeight || 0)) {
            dropZone.style.minHeight = lastBottom + 'px';
        }

        if (window.groupManager) window.groupManager._renderGroupOutlines();
    }

    // Scan all charts for axis-aligned bounding-box overlaps and push the
    // later-ordered chart downward until it no longer intersects. Used after
    // Snap-to-Grid so quantising positions never leaves two cards stacked.
    _resolveOverlaps() {
        const cm = window.canvasManager;
        const charts = cm?.charts || [];
        if (charts.length < 2) return;

        // Build measured rects from the DOM.
        const rects = charts.map(chart => {
            const card = document.querySelector(`.chart-card[data-chart-id="${chart.id}"]`);
            const w = card ? card.offsetWidth
                : (cm.colsToPixels ? cm.colsToPixels(chart.width || 6) : 380);
            const h = card ? card.offsetHeight : ((chart.height || 300) + 44);
            return { chart, card, w, h };
        });

        // Sort by posY then posX so we always push the LATER card down.
        rects.sort((a, b) => {
            const ay = a.chart.posY || 0, by = b.chart.posY || 0;
            if (ay !== by) return ay - by;
            return (a.chart.posX || 0) - (b.chart.posX || 0);
        });

        const pad = 8;
        let changed = false;
        for (let i = 0; i < rects.length; i++) {
            for (let j = 0; j < i; j++) {
                const A = rects[j], B = rects[i];
                const ax = A.chart.posX || 0, ay = A.chart.posY || 0;
                const bx = B.chart.posX || 0, by = B.chart.posY || 0;
                const overlapX = bx < ax + A.w && bx + B.w > ax;
                const overlapY = by < ay + A.h && by + B.h > ay;
                if (overlapX && overlapY) {
                    let newY = ay + A.h + pad;
                    if (this._snapToGrid) newY = this.snapValue(newY);
                    B.chart.posY = newY;
                    if (B.card) B.card.style.top = newY + 'px';
                    this._persist(B.chart);
                    changed = true;
                }
            }
        }
        if (changed && window.groupManager) window.groupManager._renderGroupOutlines();
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

        // Phase 33-B10: Tidy pack (works with 2+ charts)
        bind('btn-tidy-row', this.tidyRow);
        bind('btn-tidy-col', this.tidyColumn);

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
