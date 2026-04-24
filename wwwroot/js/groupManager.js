// Group Manager — multi-select, group/ungroup, group operations
class GroupManager {
    constructor() {
        this._selectedIds = new Set();
        this._groups = {};          // groupId -> Set of chartIds
        this._nextGroupId = 1;
        this._marquee = null;
        this._marqueeStart = null;
        this._onMarqueeMove = null;
        this._onMarqueeUp = null;
    }

    init() {
        this._initMarqueeSelect();
        this._initKeyboard();
        this._updateToolbarState();
    }

    // ── Multi-selection ─────────────────────────────────────────

    get selectedIds() { return [...this._selectedIds]; }

    get selectedCount() { return this._selectedIds.size; }

    isSelected(chartId) { return this._selectedIds.has(chartId); }

    /** Single select (replaces selection). Called from canvasManager.selectChart. */
    selectOne(chartId) {
        this._clearHighlights();
        this._selectedIds.clear();
        this._selectedIds.add(chartId);
        this._applyHighlights();
        this._updateToolbarState();
    }

    /** Toggle selection (Ctrl+click). */
    toggleSelect(chartId) {
        if (this._selectedIds.has(chartId)) {
            this._selectedIds.delete(chartId);
        } else {
            this._selectedIds.add(chartId);
        }
        this._applyHighlights();
        this._updateToolbarState();
        // Also tell properties panel about primary selection
        if (this._selectedIds.size === 1) {
            const id = [...this._selectedIds][0];
            const chart = window.canvasManager?.charts.find(c => c.id === id);
            if (chart && window.propertiesPanel) window.propertiesPanel.load(chart);
        }
    }

    /** Clear all selection. */
    clearSelection() {
        this._clearHighlights();
        this._selectedIds.clear();
        this._updateToolbarState();
    }

    /** Select multiple by IDs. */
    selectMany(chartIds) {
        this._clearHighlights();
        this._selectedIds.clear();
        chartIds.forEach(id => this._selectedIds.add(id));
        this._applyHighlights();
        this._updateToolbarState();
    }

    /** Select all items in the current group of a chart. */
    selectGroup(chartId) {
        const groupId = this._findGroupOf(chartId);
        if (!groupId || !this._groups[groupId]) return;
        this.selectMany([...this._groups[groupId]]);
    }

    _clearHighlights() {
        document.querySelectorAll('.chart-card.multi-selected').forEach(c => c.classList.remove('multi-selected'));
    }

    _applyHighlights() {
        document.querySelectorAll('.chart-card').forEach(card => {
            const id = card.dataset.chartId;
            if (this._selectedIds.has(id)) {
                card.classList.add('multi-selected');
            } else {
                card.classList.remove('multi-selected');
            }
        });
    }

    // ── Marquee (rubber-band) selection ─────────────────────────

    /** Safely clean up any stale marquee state. */
    _cleanupMarquee() {
        if (this._onMarqueeMove) {
            document.removeEventListener('mousemove', this._onMarqueeMove);
            this._onMarqueeMove = null;
        }
        if (this._onMarqueeUp) {
            document.removeEventListener('mouseup', this._onMarqueeUp);
            this._onMarqueeUp = null;
        }
        if (this._marquee) {
            this._marquee.remove();
            this._marquee = null;
        }
        this._marqueeStart = null;
    }

    _initMarqueeSelect() {
        const canvas = document.getElementById('chart-canvas-drop');
        if (!canvas) return;

        // Safety: cancel marquee when browser loses focus
        window.addEventListener('blur', () => this._cleanupMarquee());
        window.addEventListener('pointerup', () => this._cleanupMarquee());

        canvas.addEventListener('mousedown', (e) => {
            // Only start marquee if clicking on the empty canvas (not on a card)
            if (e.target !== canvas && !e.target.classList.contains('chart-grid')) return;
            if (e.button !== 0) return;

            // Clean up any stale marquee from a previous interrupted drag
            this._cleanupMarquee();

            const scrollEl = canvas.parentElement;
            const rect = canvas.getBoundingClientRect();
            const sl = scrollEl ? scrollEl.scrollLeft : 0;
            const st = scrollEl ? scrollEl.scrollTop : 0;
            const startX = e.clientX - rect.left + sl;
            const startY = e.clientY - rect.top + st;

            this._marqueeStart = { x: startX, y: startY, rect, scrollEl, sl, st };

            // Create marquee element
            const marquee = document.createElement('div');
            marquee.className = 'selection-marquee';
            canvas.appendChild(marquee);
            this._marquee = marquee;

            if (!e.ctrlKey && !e.metaKey) {
                this.clearSelection();
            }

            this._onMarqueeMove = (ev) => {
                if (!this._marquee) return;
                const csl = scrollEl ? scrollEl.scrollLeft : 0;
                const cst = scrollEl ? scrollEl.scrollTop : 0;
                const curX = ev.clientX - rect.left + csl;
                const curY = ev.clientY - rect.top + cst;

                const left = Math.min(startX, curX);
                const top = Math.min(startY, curY);
                const width = Math.abs(curX - startX);
                const height = Math.abs(curY - startY);

                marquee.style.left = left + 'px';
                marquee.style.top = top + 'px';
                marquee.style.width = width + 'px';
                marquee.style.height = height + 'px';

                // Highlight cards within marquee
                this._selectWithinRect(left, top, width, height, e.ctrlKey || e.metaKey);
            };

            this._onMarqueeUp = () => {
                this._cleanupMarquee();
                this._updateToolbarState();
            };

            document.addEventListener('mousemove', this._onMarqueeMove);
            document.addEventListener('mouseup', this._onMarqueeUp);
        });
    }

    _selectWithinRect(rx, ry, rw, rh, additive) {
        if (!additive) this._selectedIds.clear();
        const cards = document.querySelectorAll('.chart-card');
        cards.forEach(card => {
            const id = card.dataset.chartId;
            const cl = parseInt(card.style.left) || 0;
            const ct = parseInt(card.style.top) || 0;
            const cw = card.offsetWidth;
            const ch = card.offsetHeight;

            // Check overlap
            const overlaps = !(cl + cw < rx || cl > rx + rw || ct + ch < ry || ct > ry + rh);
            if (overlaps) {
                this._selectedIds.add(id);
            }
        });
        this._applyHighlights();
    }

    // ── Grouping ────────────────────────────────────────────────

    /** Group currently selected items. */
    group() {
        if (this._selectedIds.size < 2) return;
        const groupId = 'g' + (this._nextGroupId++);
        // Remove any existing group membership for these items
        this._selectedIds.forEach(id => {
            const existing = this._findGroupOf(id);
            if (existing) this._groups[existing].delete(id);
        });
        // Clean empty groups
        this._cleanEmptyGroups();
        // Create new group
        this._groups[groupId] = new Set(this._selectedIds);
        // Persist groupId on chart definitions
        this._selectedIds.forEach(id => {
            const chart = window.canvasManager?.charts.find(c => c.id === id);
            if (chart) chart.groupId = groupId;
        });
        this._renderGroupOutlines();
        this._updateToolbarState();
        this._persistGroups();
    }

    /** Ungroup the group of the primary selected item. */
    ungroup() {
        const groupIds = new Set();
        this._selectedIds.forEach(id => {
            const gid = this._findGroupOf(id);
            if (gid) groupIds.add(gid);
        });
        groupIds.forEach(gid => {
            if (this._groups[gid]) {
                this._groups[gid].forEach(id => {
                    const chart = window.canvasManager?.charts.find(c => c.id === id);
                    if (chart) chart.groupId = null;
                });
                delete this._groups[gid];
            }
        });
        this._renderGroupOutlines();
        this._updateToolbarState();
        this._persistGroups();
    }

    /** Get the group ID for a chart. */
    getGroupOf(chartId) { return this._findGroupOf(chartId); }

    /** Get all chart IDs in a group. */
    getGroupMembers(groupId) { return this._groups[groupId] ? [...this._groups[groupId]] : []; }

    /** Check if any selected item is in a group. */
    hasGroupInSelection() {
        for (const id of this._selectedIds) {
            if (this._findGroupOf(id)) return true;
        }
        return false;
    }

    _findGroupOf(chartId) {
        for (const [gid, members] of Object.entries(this._groups)) {
            if (members.has(chartId)) return gid;
        }
        return null;
    }

    _cleanEmptyGroups() {
        for (const gid of Object.keys(this._groups)) {
            if (this._groups[gid].size === 0) delete this._groups[gid];
        }
    }

    // ── Group dragging ──────────────────────────────────────────

    /** Called when a card starts dragging — if it's in a group, return group sibling IDs. */
    getGroupSiblings(chartId) {
        const groupId = this._findGroupOf(chartId);
        if (!groupId || !this._groups[groupId]) return [];
        return [...this._groups[groupId]].filter(id => id !== chartId);
    }

    /** Move group siblings by delta. */
    moveGroupSiblings(chartId, deltaX, deltaY) {
        const siblings = this.getGroupSiblings(chartId);
        siblings.forEach(id => {
            const card = document.querySelector(`.chart-card[data-chart-id="${id}"]`);
            const chart = window.canvasManager?.charts.find(c => c.id === id);
            if (card && chart) {
                const newX = Math.max(0, (chart._dragStartX ?? chart.posX) + deltaX);
                const newY = Math.max(0, (chart._dragStartY ?? chart.posY) + deltaY);
                card.style.left = newX + 'px';
                card.style.top = newY + 'px';
            }
        });
    }

    /** Finalize sibling positions after drag. */
    finalizeGroupDrag(chartId, deltaX, deltaY) {
        const siblings = this.getGroupSiblings(chartId);
        siblings.forEach(id => {
            const chart = window.canvasManager?.charts.find(c => c.id === id);
            if (chart) {
                chart.posX = Math.max(0, Math.round((chart._dragStartX ?? chart.posX) + deltaX));
                chart.posY = Math.max(0, Math.round((chart._dragStartY ?? chart.posY) + deltaY));
                delete chart._dragStartX;
                delete chart._dragStartY;
                // Persist
                fetch(`/api/chart/${id}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(chart)
                }).catch(err => console.warn('Could not persist group drag:', err));
            }
        });
        this._renderGroupOutlines();
    }

    /** Snapshot drag start positions for group members. */
    snapshotGroupPositions(chartId) {
        const siblings = this.getGroupSiblings(chartId);
        siblings.forEach(id => {
            const chart = window.canvasManager?.charts.find(c => c.id === id);
            if (chart) {
                chart._dragStartX = chart.posX;
                chart._dragStartY = chart.posY;
            }
        });
    }

    // ── Multi-select dragging (all selected visuals) ────────────

    /** Get all IDs that should move with a dragged card (multi-select + group siblings, merged). */
    getDragSiblings(chartId) {
        const siblings = new Set();
        // Add multi-selected items
        if (this._selectedIds.size > 1 && this._selectedIds.has(chartId)) {
            this._selectedIds.forEach(id => { if (id !== chartId) siblings.add(id); });
        }
        // Also include formal group siblings
        this.getGroupSiblings(chartId).forEach(id => siblings.add(id));
        return [...siblings];
    }

    /** Snapshot drag start positions for ALL items that will move with the dragged card. */
    snapshotDragPositions(chartId) {
        const siblings = this.getDragSiblings(chartId);
        siblings.forEach(id => {
            const chart = window.canvasManager?.charts.find(c => c.id === id);
            if (chart) {
                chart._dragStartX = chart.posX;
                chart._dragStartY = chart.posY;
            }
        });
    }

    /** Move ALL drag siblings by delta during drag. */
    moveDragSiblings(chartId, deltaX, deltaY) {
        const siblings = this.getDragSiblings(chartId);
        siblings.forEach(id => {
            const card = document.querySelector(`.chart-card[data-chart-id="${id}"]`);
            const chart = window.canvasManager?.charts.find(c => c.id === id);
            if (card && chart) {
                let newX = Math.max(0, (chart._dragStartX ?? chart.posX) + deltaX);
                let newY = Math.max(0, (chart._dragStartY ?? chart.posY) + deltaY);
                if (window.layoutManager && window.layoutManager.snapToGrid) {
                    newX = window.layoutManager.snapValue(newX);
                    newY = window.layoutManager.snapValue(newY);
                }
                card.style.left = newX + 'px';
                card.style.top = newY + 'px';
            }
        });
    }

    /** Finalize ALL drag sibling positions after drag ends. */
    finalizeDragSiblings(chartId, deltaX, deltaY) {
        const siblings = this.getDragSiblings(chartId);
        siblings.forEach(id => {
            const chart = window.canvasManager?.charts.find(c => c.id === id);
            if (chart) {
                let newX = Math.max(0, Math.round((chart._dragStartX ?? chart.posX) + deltaX));
                let newY = Math.max(0, Math.round((chart._dragStartY ?? chart.posY) + deltaY));
                if (window.layoutManager && window.layoutManager.snapToGrid) {
                    newX = window.layoutManager.snapValue(newX);
                    newY = window.layoutManager.snapValue(newY);
                }
                chart.posX = newX;
                chart.posY = newY;
                delete chart._dragStartX;
                delete chart._dragStartY;
                fetch(`/api/chart/${id}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(chart)
                }).catch(err => console.warn('Could not persist drag:', err));
            }
        });
        this._renderGroupOutlines();
    }

    // ── Group outlines (visual) ─────────────────────────────────

    _renderGroupOutlines() {
        // Remove existing outlines
        document.querySelectorAll('.group-outline').forEach(el => el.remove());

        const canvas = document.getElementById('chart-canvas-drop');
        if (!canvas) return;

        for (const [gid, members] of Object.entries(this._groups)) {
            if (members.size < 2) continue;
            let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
            let valid = false;

            members.forEach(id => {
                const card = document.querySelector(`.chart-card[data-chart-id="${id}"]`);
                if (card) {
                    valid = true;
                    const x = parseInt(card.style.left) || 0;
                    const y = parseInt(card.style.top) || 0;
                    const w = card.offsetWidth;
                    const h = card.offsetHeight;
                    minX = Math.min(minX, x);
                    minY = Math.min(minY, y);
                    maxX = Math.max(maxX, x + w);
                    maxY = Math.max(maxY, y + h);
                }
            });

            if (!valid) continue;

            const pad = 8;
            const outline = document.createElement('div');
            outline.className = 'group-outline';
            outline.dataset.groupId = gid;
            outline.style.left = (minX - pad) + 'px';
            outline.style.top = (minY - pad) + 'px';
            outline.style.width = (maxX - minX + pad * 2) + 'px';
            outline.style.height = (maxY - minY + pad * 2) + 'px';

            const label = document.createElement('span');
            label.className = 'group-label';
            label.innerHTML = `<i class="bi bi-collection-fill"></i>Group (${members.size})`;
            label.addEventListener('click', (e) => {
                e.stopPropagation();
                this.selectMany([...members]);
            });
            outline.appendChild(label);

            // Make group outline draggable — drag the whole group
            this._makeGroupDraggable(outline, gid, members, canvas);

            canvas.appendChild(outline);
        }
    }

    /** Attach mousedown drag to a group outline so the entire group can be dragged. */
    _makeGroupDraggable(outline, groupId, members, canvas) {
        outline.addEventListener('mousedown', (e) => {
            // Ignore clicks on the label text (handled separately)
            if (e.target.closest('.group-label')) return;
            e.preventDefault();
            e.stopPropagation();

            // Select all members
            this.selectMany([...members]);

            const scrollEl = canvas.parentElement;
            const containerRect = canvas.getBoundingClientRect();
            const scrollLeft = scrollEl ? scrollEl.scrollLeft : 0;
            const scrollTop = scrollEl ? scrollEl.scrollTop : 0;

            const startMouseX = e.clientX;
            const startMouseY = e.clientY;

            // Snapshot every member's starting position
            const startPositions = {};
            members.forEach(id => {
                const chart = window.canvasManager?.charts.find(c => c.id === id);
                if (chart) startPositions[id] = { x: chart.posX, y: chart.posY };
            });

            const outlineStartLeft = parseInt(outline.style.left) || 0;
            const outlineStartTop = parseInt(outline.style.top) || 0;

            outline.classList.add('group-dragging');

            const onMouseMove = (ev) => {
                const dx = ev.clientX - startMouseX;
                const dy = ev.clientY - startMouseY;

                // Move the outline itself
                outline.style.left = Math.max(0, outlineStartLeft + dx) + 'px';
                outline.style.top = Math.max(0, outlineStartTop + dy) + 'px';

                // Move all member cards
                members.forEach(id => {
                    const start = startPositions[id];
                    if (!start) return;
                    const card = document.querySelector(`.chart-card[data-chart-id="${id}"]`);
                    if (card) {
                        let newX = Math.max(0, start.x + dx);
                        let newY = Math.max(0, start.y + dy);
                        if (window.layoutManager && window.layoutManager.snapToGrid) {
                            newX = window.layoutManager.snapValue(newX);
                            newY = window.layoutManager.snapValue(newY);
                        }
                        card.style.left = newX + 'px';
                        card.style.top = newY + 'px';
                    }
                });
            };

            const onMouseUp = (ev) => {
                outline.classList.remove('group-dragging');
                document.removeEventListener('mousemove', onMouseMove);
                document.removeEventListener('mouseup', onMouseUp);

                const dx = ev.clientX - startMouseX;
                const dy = ev.clientY - startMouseY;

                // Finalize positions and persist
                members.forEach(id => {
                    const chart = window.canvasManager?.charts.find(c => c.id === id);
                    const start = startPositions[id];
                    if (chart && start) {
                        let newX = Math.max(0, Math.round(start.x + dx));
                        let newY = Math.max(0, Math.round(start.y + dy));
                        if (window.layoutManager && window.layoutManager.snapToGrid) {
                            newX = window.layoutManager.snapValue(newX);
                            newY = window.layoutManager.snapValue(newY);
                        }
                        chart.posX = newX;
                        chart.posY = newY;
                        fetch(`/api/chart/${id}`, {
                            method: 'PUT',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify(chart)
                        }).catch(err => console.warn('Could not persist group drag:', err));
                    }
                });

                // Re-render outlines to update bounding box
                this._renderGroupOutlines();
            };

            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
        });
    }

    // ── Restore groups from chart definitions ───────────────────

    restoreGroups(charts) {
        this._groups = {};
        this._nextGroupId = 1;
        charts.forEach(c => {
            if (c.groupId) {
                if (!this._groups[c.groupId]) this._groups[c.groupId] = new Set();
                this._groups[c.groupId].add(c.id);
                // Track next id
                const num = parseInt(c.groupId.replace('g', ''));
                if (!isNaN(num) && num >= this._nextGroupId) this._nextGroupId = num + 1;
            }
        });
        this._cleanEmptyGroups();
        setTimeout(() => this._renderGroupOutlines(), 100);
    }

    // ── Persist group IDs ───────────────────────────────────────

    _persistGroups() {
        // Persist each chart's groupId via the existing update API
        for (const [gid, members] of Object.entries(this._groups)) {
            members.forEach(id => {
                const chart = window.canvasManager?.charts.find(c => c.id === id);
                if (chart) {
                    chart.groupId = gid;
                    fetch(`/api/chart/${id}`, {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(chart)
                    }).catch(err => console.warn('Could not persist group:', err));
                }
            });
        }
    }

    // ── Keyboard shortcuts ──────────────────────────────────────

    _initKeyboard() {
        document.addEventListener('keydown', (e) => {
            // Ctrl+A: select all
            if ((e.ctrlKey || e.metaKey) && e.key === 'a' && this._isCanvasFocused()) {
                e.preventDefault();
                const allIds = (window.canvasManager?.charts || []).map(c => c.id);
                this.selectMany(allIds);
            }
            // Ctrl+G: group
            if ((e.ctrlKey || e.metaKey) && e.key === 'g' && !e.shiftKey && this._isCanvasFocused()) {
                e.preventDefault();
                this.group();
            }
            // Ctrl+Shift+G: ungroup
            if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.key === 'G' && this._isCanvasFocused()) {
                e.preventDefault();
                this.ungroup();
            }
            // Escape: clear selection
            if (e.key === 'Escape') {
                this.clearSelection();
            }
            // Delete: delete selected
            if (e.key === 'Delete' && this._selectedIds.size > 0 && this._isCanvasFocused()) {
                e.preventDefault();
                this._deleteSelected();
            }
        });
    }

    _isCanvasFocused() {
        const active = document.activeElement;
        return !active || active === document.body ||
               active.closest('.canvas-panel') !== null;
    }

    async _deleteSelected() {
        if (this._selectedIds.size === 0) return;
        const count = this._selectedIds.size;
        const confirmed = await this._showDeleteSelectedConfirm(count);
        if (!confirmed) return;
        const ids = [...this._selectedIds];
        for (const id of ids) {
            await window.canvasManager?.deleteChart(id);
            // Remove from groups
            const gid = this._findGroupOf(id);
            if (gid && this._groups[gid]) this._groups[gid].delete(id);
        }
        this._cleanEmptyGroups();
        this._selectedIds.clear();
        this._renderGroupOutlines();
        this._updateToolbarState();
    }

    _showDeleteSelectedConfirm(count) {
        return new Promise(function (resolve) {
            const plural = count > 1;
            const overlay = document.createElement('div');
            overlay.className = 'ar-overlay';
            overlay.innerHTML =
                '<div class="ar-modal" style="width:420px">' +
                    '<div class="ar-modal-header" style="background:linear-gradient(135deg,#dc3545 0%,#c82333 100%)">' +
                        '<i class="bi bi-trash3-fill"></i>Delete ' + (plural ? 'Items' : 'Item') +
                    '</div>' +
                    '<div class="ar-modal-body" style="text-align:center;padding:24px 20px">' +
                        '<i class="bi bi-exclamation-triangle-fill" style="font-size:2.2rem;color:#dc3545;display:block;margin-bottom:12px"></i>' +
                        '<div style="font-size:0.92rem;font-weight:600;color:var(--cp-text,#1e2d3d);margin-bottom:6px">Delete ' + count + ' selected item' + (plural ? 's' : '') + '?</div>' +
                        '<div style="font-size:0.78rem;color:var(--cp-text-secondary,#6c757d)">' +
                            (plural ? 'These charts' : 'This chart') + ' will be permanently removed from the page.<br>This action cannot be undone.' +
                        '</div>' +
                    '</div>' +
                    '<div class="ar-modal-footer" style="justify-content:center;gap:12px">' +
                        '<button class="ar-cancel-btn" id="arDelSelNo" style="min-width:90px">Cancel</button>' +
                        '<button class="ar-generate-btn" id="arDelSelYes" style="min-width:90px;background:#dc3545"><i class="bi bi-trash3 me-1"></i>Delete</button>' +
                    '</div>' +
                '</div>';
            document.body.appendChild(overlay);
            requestAnimationFrame(function () { overlay.classList.add('ar-open'); });

            function close(result) {
                overlay.classList.remove('ar-open');
                document.removeEventListener('keydown', onKey, true);
                setTimeout(function () { overlay.remove(); }, 260);
                resolve(result);
            }
            function onKey(e) {
                if (e.key === 'Escape') { e.preventDefault(); close(false); }
                else if (e.key === 'Enter') { e.preventDefault(); close(true); }
            }
            document.addEventListener('keydown', onKey, true);

            document.getElementById('arDelSelYes').addEventListener('click', function () { close(true); });
            document.getElementById('arDelSelNo').addEventListener('click', function () { close(false); });
            overlay.addEventListener('click', function (e) { if (e.target === overlay) close(false); });
            setTimeout(function () {
                const yes = document.getElementById('arDelSelYes');
                if (yes) yes.focus();
            }, 120);
        });
    }

    // ── Toolbar state ───────────────────────────────────────────

    _updateToolbarState() {
        const count = this._selectedIds.size;

        // Group actions visibility
        const groupActions = document.getElementById('group-actions');
        if (groupActions) {
            groupActions.classList.toggle('hidden', count < 2);
        }

        // Layout toolbar visibility
        const layoutToolbar = document.getElementById('layout-toolbar');
        if (layoutToolbar) {
            layoutToolbar.classList.toggle('hidden', count < 2);
        }

        // Group button state
        const groupBtn = document.getElementById('btn-group-items');
        const ungroupBtn = document.getElementById('btn-ungroup-items');
        if (groupBtn) groupBtn.disabled = count < 2;
        if (ungroupBtn) ungroupBtn.disabled = !this.hasGroupInSelection();

        // Dispatch event for other modules
        document.dispatchEvent(new CustomEvent('selection:changed', {
            detail: { selectedIds: this.selectedIds, count }
        }));
    }
}

window.groupManager = new GroupManager();
