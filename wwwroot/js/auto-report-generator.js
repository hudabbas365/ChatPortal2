// auto-report-generator.js — Auto Generate Report via AI
// Adds a button to the dashboard chat panel that generates a full multi-page report.
(function (global) {
    'use strict';

    // ── State ────────────────────────────────────────────────────────
    let _overlay = null;
    let _abortController = null;
    let _isRunning = false;

    // ── Helpers ──────────────────────────────────────────────────────
    function _esc(str) {
        if (typeof escapeHtml === 'function') return escapeHtml(str);
        const d = document.createElement('div');
        d.appendChild(document.createTextNode(String(str ?? '')));
        return d.innerHTML;
    }

    function _toast(msg, type) {
        if (global.dashboardChartTransfer) {
            global.dashboardChartTransfer.showToast(msg, type || 'success');
        }
    }

    function _getWorkspaceId() {
        const p = new URLSearchParams(location.search);
        return p.get('workspace') || p.get('ws') ||
            global._dashboardWsData?.guid || global.currentWorkspaceGuid || null;
    }

    // Validate that every chart on the current page sits fully inside the
    // chart-canvas-drop element horizontally. Vertical bounds are NOT clamped —
    // the drop zone grows/scrolls vertically, so forcing posY to fit the current
    // min-height would pull charts up and cause overlap.
    // If `onlyIds` is provided (Set of chart ids), only those charts are inspected/clamped;
    // any other pre-existing user-arranged charts are left untouched.
    function _validateAndFixPagePositions(cm, margin, onlyIds) {
        if (!cm || !cm.charts) return;
        var dropZone = document.getElementById('chart-canvas-drop');
        if (!dropZone) return;
        var zoneW = dropZone.clientWidth;
        var m = margin || 20;
        var changed = false;
        cm.charts.forEach(function (c) {
            if (onlyIds && !onlyIds.has(c.id)) return;
            var cardW = cm.colsToPixels(c.width || 6);
            var fixed = false;

            if (c.posX == null || c.posX < m) { c.posX = m; fixed = true; }
            if (c.posY == null || c.posY < m) { c.posY = m; fixed = true; }
            if (c.posX + cardW > zoneW - m) {
                c.posX = Math.max(m, zoneW - m - cardW);
                fixed = true;
            }
            if (fixed) {
                changed = true;
                console.warn('[auto-report] Clamped out-of-range chart:', c.title, 'to', c.posX, c.posY);
                // Update DOM card element if already rendered
                var el = document.querySelector('[data-chart-id="' + c.id + '"]');
                if (el) {
                    el.style.left = c.posX + 'px';
                    el.style.top = c.posY + 'px';
                }
            }
        });
        // Grow the drop zone to contain the tallest chart so nothing ever appears
        // outside the visible canvas area even after scrolling.
        var maxBottom = 0;
        cm.charts.forEach(function (c) {
            var cardH = (c.height || 300) + 44;
            var bottom = (c.posY || 0) + cardH;
            if (bottom > maxBottom) maxBottom = bottom;
        });
        if (maxBottom + m > (dropZone.clientHeight || 0)) {
            dropZone.style.minHeight = (maxBottom + m) + 'px';
        }
        if (changed && typeof cm.saveState === 'function') {
            try { cm.saveState(); } catch (e) {}
        }
    }

    // Second verification pass — after charts render, measure each card's ACTUAL
    // width from the DOM (which may differ from colsToPixels at plan-time due to
    // window resize, scrollbars, or CSS min/max constraints) and re-pack the
    // page so rows are tight, left-aligned, and nothing drifts off-canvas.
    // If `onlyIds` is provided, ONLY charts with those ids are re-packed. Pre-existing
    // user-arranged charts keep their posX/posY so the auto-report flow never
    // "resets" the canvas layout the user already built.
    function _repackPageFromDom(cm, margin, rowGap, colGap, onlyIds) {
        if (!cm || !cm.charts || cm.charts.length === 0) return;
        var dropZone = document.getElementById('chart-canvas-drop');
        if (!dropZone) return;

        var scrollEl = dropZone.parentElement;
        var viewportW = scrollEl ? scrollEl.clientWidth : dropZone.clientWidth;
        var zoneW = dropZone.clientWidth;
        var canvasBase = Math.max(
            cm.colsToPixels(12),
            Math.min(viewportW, zoneW) - margin * 2
        );

        // Only iterate the newly-inserted charts when a filter set is provided.
        var source = onlyIds
            ? cm.charts.filter(function (c) { return onlyIds.has(c.id); })
            : cm.charts.slice();

        // Preserve the original order the charts were added in.
        var items = source.sort(function (a, b) {
            var ay = a.posY || 0, by = b.posY || 0;
            if (ay !== by) return ay - by;
            return (a.posX || 0) - (b.posX || 0);
        });

        // When re-packing only newly-added charts, start the cursor BELOW any
        // pre-existing user charts so we don't stack the AI output on top of them.
        var startY = margin;
        if (onlyIds) {
            var existingBottom = margin;
            cm.charts.forEach(function (c) {
                if (onlyIds.has(c.id)) return;
                var h = (c.height || 300) + 44;
                var b = (c.posY || 0) + h;
                if (b > existingBottom) existingBottom = b;
            });
            if (existingBottom > margin) startY = existingBottom + rowGap;
        }

        var cursorX = margin;
        var rowY = startY;
        var rowMaxH = 0;
        var touched = false;

        items.forEach(function (c) {
            var el = document.querySelector('[data-chart-id="' + c.id + '"]');
            var measuredW = el ? el.offsetWidth : cm.colsToPixels(c.width || 6);
            var measuredH = el ? el.offsetHeight : ((c.height || 300) + 44);
            if (measuredW > canvasBase) measuredW = canvasBase;

            // Full-width cards always start a new row.
            var isFullWidth = (c.width || 6) >= 10 || measuredW > canvasBase * 0.75;
            if (isFullWidth && cursorX > margin) {
                rowY += rowMaxH + rowGap;
                cursorX = margin;
                rowMaxH = 0;
            }

            // Wrap if this card won't fit in the remaining space.
            if (cursorX > margin && cursorX + measuredW > margin + canvasBase) {
                rowY += rowMaxH + rowGap;
                cursorX = margin;
                rowMaxH = 0;
            }

            var newX = cursorX;
            var newY = rowY;
            if (c.posX !== newX || c.posY !== newY) {
                c.posX = newX;
                c.posY = newY;
                touched = true;
                if (el) {
                    el.style.left = newX + 'px';
                    el.style.top = newY + 'px';
                }
            }

            cursorX += measuredW + colGap;
            if (measuredH > rowMaxH) rowMaxH = measuredH;
            if (isFullWidth) {
                // Full-width → close the row immediately.
                rowY += rowMaxH + rowGap;
                cursorX = margin;
                rowMaxH = 0;
            }
        });

        // Grow drop zone to contain the last row.
        var lastBottom = rowY + rowMaxH + margin;
        if (lastBottom > (dropZone.clientHeight || 0)) {
            dropZone.style.minHeight = lastBottom + 'px';
        }

        if (touched && typeof cm.saveState === 'function') {
            try { cm.saveState(); } catch (e) {}
        }
    }

    // ── Inject button into chat panel ────────────────────────────────
    function _injectButton() {
        var messagesEl = document.getElementById('dcpMessages');
        if (!messagesEl) return;
        var welcome = messagesEl.querySelector('.dcp-welcome');
        if (!welcome) return;

        // Add button before the chips
        var existing = welcome.querySelector('.dcp-auto-report-btn');
        if (existing) return;

        var btn = document.createElement('button');
        btn.className = 'dcp-auto-report-btn';
        btn.innerHTML = '<i class="bi bi-magic"></i> Auto Generate Report';
        btn.title = 'Let AI design a complete multi-page report from your data';
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            _showPromptModal(false);
        });

        // Insert before the dcp-chips div
        var chips = welcome.querySelector('.dcp-chips');
        if (chips) {
            welcome.insertBefore(btn, chips);
        } else {
            welcome.appendChild(btn);
        }
    }

    // ── Wire toolbar button ──────────────────────────────────────────
    function _wireToolbarButton() {
        var btn = document.getElementById('auto-report-toolbar-btn');
        if (!btn) return;
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            var hasCharts = global.canvasManager && global.canvasManager.charts && global.canvasManager.charts.length > 0;
            if (hasCharts) {
                _showModePickerModal();
            } else {
                _showPromptModal(false);
            }
        });
    }

    // ── Mode Picker (New vs Redesign) ────────────────────────────────
    function _showModePickerModal() {
        _removeOverlay();
        _overlay = document.createElement('div');
        _overlay.className = 'ar-overlay';
        _overlay.innerHTML =
            '<div class="ar-modal">' +
                '<div class="ar-modal-header"><i class="bi bi-magic"></i>Auto Report</div>' +
                '<div class="ar-modal-body">' +
                    '<p style="font-size:0.82rem;color:var(--cp-text-secondary,#6c757d);margin-bottom:12px">Your canvas already has charts. What would you like to do?</p>' +
                    '<div class="d-flex gap-2">' +
                        '<button class="ar-generate-btn flex-fill" id="arModeNew" style="background:var(--cp-primary,#4A90D9)"><i class="bi bi-file-earmark-plus me-1"></i>Generate New</button>' +
                        '<button class="ar-generate-btn flex-fill" id="arModeRedesign" style="background:var(--cp-purple,#9B59B6)"><i class="bi bi-arrow-repeat me-1"></i>Redesign Existing</button>' +
                    '</div>' +
                '</div>' +
                '<div class="ar-modal-footer">' +
                    '<button class="ar-cancel-btn" id="arModeCancel">Cancel</button>' +
                '</div>' +
            '</div>';
        document.body.appendChild(_overlay);
        requestAnimationFrame(function () { _overlay.classList.add('ar-open'); });
        document.getElementById('arModeCancel').addEventListener('click', _removeOverlay);
        document.getElementById('arModeNew').addEventListener('click', function () {
            _removeOverlay();
            setTimeout(function () { _showPromptModal(false); }, 280);
        });
        document.getElementById('arModeRedesign').addEventListener('click', function () {
            _removeOverlay();
            setTimeout(function () { _showPromptModal(true); }, 280);
        });
    }

    // ── Collect existing chart descriptions for redesign ─────────────
    function _collectExistingCharts() {
        if (!global.canvasManager) return '';
        var cm = global.canvasManager;
        var lines = [];
        var allPages = cm.pages || [];
        for (var pi = 0; pi < allPages.length; pi++) {
            var page = allPages[pi];
            var charts = (pi === cm.activePageIndex) ? (cm.charts || []) : (page.charts || []);
            if (charts.length === 0) continue;
            lines.push('Page ' + (pi + 1) + ': ' + (page.name || 'Untitled'));
            for (var ci = 0; ci < charts.length; ci++) {
                var c = charts[ci];
                var desc = '  - ' + (c.chartType || 'unknown') + ': "' + (c.title || 'Untitled') + '"';
                if (c.dataQuery) desc += ' (query: ' + c.dataQuery.substring(0, 120) + ')';
                if (c.mapping && c.mapping.labelField) desc += ' label=' + c.mapping.labelField;
                if (c.mapping && c.mapping.valueField) desc += ' value=' + c.mapping.valueField;
                lines.push(desc);
            }
        }
        return lines.join('\n');
    }

    // ── Prompt Modal ─────────────────────────────────────────────────
    function _showPromptModal(isRedesign) {
        _removeOverlay();

        const tables = (global._realTableNames || []).filter(Boolean);

        var tableCheckboxes = '';
        if (tables.length > 0) {
            tableCheckboxes =
                '<div class="ar-table-select">' +
                    '<div class="ar-table-select-header">' +
                        '<label style="font-size:0.76rem;font-weight:600;color:var(--cp-text,#1e2d3d)"><i class="bi bi-table me-1"></i>Select Tables</label>' +
                        '<button type="button" class="ar-table-toggle-all" id="arToggleAll">Deselect All</button>' +
                    '</div>' +
                    '<div class="ar-table-list">' +
                    tables.map(function (t) {
                        return '<label class="ar-table-item"><input type="checkbox" class="ar-table-cb" value="' + _esc(t) + '" checked /><span>' + _esc(t) + '</span></label>';
                    }).join('') +
                    '</div>' +
                '</div>';
        } else {
            tableCheckboxes = '<div style="margin-bottom:10px;font-size:0.75rem;color:var(--cp-text-muted,#adb5bd)">No tables detected</div>';
        }

        var headerText = isRedesign ? 'Redesign Report' : 'Auto Generate Report';
        var headerIcon = isRedesign ? 'bi-arrow-repeat' : 'bi-magic';
        var placeholder = isRedesign
            ? 'Describe how you want to improve the report (e.g. "add more KPIs", "focus on trends", "make it more visual")...'
            : 'Describe what you want in the report, or leave blank for a comprehensive overview...';
        var btnText = isRedesign ? '<i class="bi bi-arrow-repeat me-1"></i>Redesign Report' : '<i class="bi bi-stars me-1"></i>Generate Report';

        _overlay = document.createElement('div');
        _overlay.className = 'ar-overlay';
        _overlay.innerHTML =
            '<div class="ar-modal" style="width:480px">' +
                '<div class="ar-modal-header"><i class="bi ' + headerIcon + '"></i>' + headerText + '</div>' +
                '<div class="ar-modal-body">' +
                    tableCheckboxes +
                    (isRedesign ? '<div style="margin-bottom:8px;font-size:0.72rem;padding:6px 10px;background:var(--cp-bg-alt,#f8f9fa);border-radius:6px;border-left:3px solid var(--cp-purple,#9B59B6);color:var(--cp-text-secondary,#6c757d)"><i class="bi bi-info-circle me-1"></i>AI will analyze your existing charts and redesign the report. Current charts will be replaced.</div>' : '') +
                    '<textarea class="ar-prompt-input" id="arPromptInput" rows="3" placeholder="' + placeholder + '"></textarea>' +
                '</div>' +
                '<div class="ar-modal-footer">' +
                    '<button class="ar-cancel-btn" id="arCancelBtn">Cancel</button>' +
                    '<button class="ar-generate-btn" id="arGoBtn">' + btnText + '</button>' +
                '</div>' +
            '</div>';

        document.body.appendChild(_overlay);
        requestAnimationFrame(function () { _overlay.classList.add('ar-open'); });

        // Wire toggle all button
        var toggleBtn = document.getElementById('arToggleAll');
        if (toggleBtn) {
            toggleBtn.addEventListener('click', function () {
                var cbs = _overlay.querySelectorAll('.ar-table-cb');
                var allChecked = Array.from(cbs).every(function (cb) { return cb.checked; });
                cbs.forEach(function (cb) { cb.checked = !allChecked; });
                toggleBtn.textContent = allChecked ? 'Select All' : 'Deselect All';
            });
        }

        document.getElementById('arCancelBtn').addEventListener('click', _removeOverlay);
        document.getElementById('arGoBtn').addEventListener('click', function () {
            var prompt = (document.getElementById('arPromptInput')?.value || '').trim();
            var selectedTables = Array.from(_overlay.querySelectorAll('.ar-table-cb:checked')).map(function (cb) { return cb.value; });
            if (tables.length > 0 && selectedTables.length === 0) {
                _toast('Please select at least one table', 'warn');
                return;
            }
            var existingCharts = isRedesign ? _collectExistingCharts() : '';
            _startGeneration(prompt, selectedTables.length > 0 ? selectedTables : tables, existingCharts);
        });

        // Focus textarea
        setTimeout(function () { document.getElementById('arPromptInput')?.focus(); }, 200);
    }

    function _removeOverlay() {
        if (_overlay) {
            var el = _overlay;
            _overlay = null;
            el.classList.remove('ar-open');
            setTimeout(function () { el.remove(); }, 260);
        }
    }

    // ── Progress Modal ───────────────────────────────────────────────
    var _timerInterval = null;

    function _showProgressModal() {
        _removeOverlay();
        if (_timerInterval) { clearInterval(_timerInterval); _timerInterval = null; }

        var startTime = Date.now();

        _overlay = document.createElement('div');
        _overlay.className = 'ar-overlay';
        _overlay.innerHTML =
            '<div class="ar-modal">' +
                '<div class="ar-modal-header"><i class="bi bi-magic"></i>Generating Report…</div>' +
                '<div class="ar-modal-body">' +
                    '<div class="ar-progress-bar"><div class="ar-progress-fill" id="arProgressFill"></div></div>' +
                    '<div class="d-flex justify-content-between align-items-center" style="margin-bottom:12px">' +
                        '<div class="ar-status" id="arStatus" style="margin-bottom:0">Connecting to AI…</div>' +
                        '<span class="ar-elapsed" id="arElapsed">0s</span>' +
                    '</div>' +
                    '<ul class="ar-steps" id="arSteps">' +
                        '<li class="ar-step-active" data-step="plan"><span class="ar-step-icon"><i class="bi bi-hourglass-split"></i></span>Planning report structure</li>' +
                        '<li data-step="pages"><span class="ar-step-icon"><i class="bi bi-circle"></i></span>Creating pages</li>' +
                        '<li data-step="charts"><span class="ar-step-icon"><i class="bi bi-circle"></i></span>Adding charts &amp; text</li>' +
                        '<li data-step="done"><span class="ar-step-icon"><i class="bi bi-circle"></i></span>Finalising</li>' +
                    '</ul>' +
                '</div>' +
                '<div class="ar-modal-footer">' +
                    '<button class="ar-cancel-btn" id="arAbortBtn">Cancel</button>' +
                '</div>' +
            '</div>';

        document.body.appendChild(_overlay);
        requestAnimationFrame(function () { _overlay.classList.add('ar-open'); });

        _timerInterval = setInterval(function () {
            var el = document.getElementById('arElapsed');
            if (el) {
                var sec = Math.floor((Date.now() - startTime) / 1000);
                el.textContent = sec < 60 ? sec + 's' : Math.floor(sec / 60) + 'm ' + (sec % 60) + 's';
            }
        }, 1000);

        document.getElementById('arAbortBtn').addEventListener('click', function () {
            if (_abortController) _abortController.abort();
            _removeOverlay();
            _isRunning = false;
            if (_timerInterval) { clearInterval(_timerInterval); _timerInterval = null; }
        });
    }

    function _setStep(stepName) {
        var steps = document.querySelectorAll('#arSteps li');
        var reached = false;
        steps.forEach(function (li) {
            if (li.dataset.step === stepName) {
                li.className = 'ar-step-active';
                li.querySelector('.ar-step-icon').innerHTML = '<i class="bi bi-hourglass-split"></i>';
                reached = true;
            } else if (!reached) {
                li.className = 'ar-step-done';
                li.querySelector('.ar-step-icon').innerHTML = '<i class="bi bi-check-circle-fill"></i>';
            } else {
                li.className = '';
                li.querySelector('.ar-step-icon').innerHTML = '<i class="bi bi-circle"></i>';
            }
        });
    }

    function _setProgress(pct) {
        var fill = document.getElementById('arProgressFill');
        if (fill) fill.style.width = Math.min(100, Math.max(0, pct)) + '%';
    }

    function _setStatus(text) {
        var el = document.getElementById('arStatus');
        if (el) el.textContent = text;
    }

    // ── Phase 31: Interactive plan preview ───────────────────────────
    // Swap the progress modal's content with an editable preview of the AI plan.
    // Resolves with { action: 'build' | 'regenerate' | 'cancel', plan }.
    function _showPlanPreview(plan) {
        return new Promise(function (resolve) {
            if (!_overlay) { resolve({ action: 'cancel' }); return; }
            var modal = _overlay.querySelector('.ar-modal');
            if (!modal) { resolve({ action: 'cancel' }); return; }

            // Deep clone so the user's edits don't mutate the original plan
            // until they explicitly click Build.
            var editable;
            try { editable = JSON.parse(JSON.stringify(plan)); }
            catch (e) { editable = plan; }
            if (!Array.isArray(editable.pages)) editable.pages = [];

            var totalCharts = 0;
            editable.pages.forEach(function (p) {
                if (!Array.isArray(p.charts)) p.charts = [];
                totalCharts += p.charts.length;
            });

            function _esc2(s) {
                return String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
            }

            function _fieldsSummary(c) {
                var m = c.mapping || {};
                var parts = [];
                if (m.labelField)   parts.push('label: ' + m.labelField);
                if (m.valueField)   parts.push('value: ' + m.valueField);
                if (m.xField)       parts.push('x: ' + m.xField);
                if (m.yField)       parts.push('y: ' + m.yField);
                if (m.groupByField) parts.push('group: ' + m.groupByField);
                if (Array.isArray(m.multiValueFields) && m.multiValueFields.length) {
                    parts.push('values: ' + m.multiValueFields.map(function (f) {
                        return (typeof f === 'object' ? f.field : f);
                    }).join(', '));
                }
                return parts.join(' · ');
            }

            function render() {
                totalCharts = 0;
                editable.pages.forEach(function (p) { totalCharts += (p.charts || []).length; });

                var pagesHtml = editable.pages.map(function (page, pi) {
                    var chartsHtml = (page.charts || []).map(function (c, ci) {
                        var type = _esc2(c.chartType || 'chart');
                        var ds   = c.datasetName ? ' · ' + _esc2(c.datasetName) : '';
                        var fields = _fieldsSummary(c);
                        return '' +
                            '<div class="ar-pp-chart" data-pi="' + pi + '" data-ci="' + ci + '">' +
                                '<span class="ar-pp-chart-type" title="' + type + '">' + type + '</span>' +
                                '<input type="text" class="ar-pp-chart-title" value="' + _esc2(c.title || '') + '" placeholder="Chart title" />' +
                                '<span class="ar-pp-chart-meta">' + (fields ? _esc2(fields) : '') + ds + '</span>' +
                                '<button class="ar-pp-x" data-action="del-chart" title="Remove chart"><i class="bi bi-x"></i></button>' +
                            '</div>';
                    }).join('');

                    return '' +
                        '<div class="ar-pp-page" data-pi="' + pi + '">' +
                            '<div class="ar-pp-page-head">' +
                                '<i class="bi bi-file-earmark-text"></i>' +
                                '<input type="text" class="ar-pp-page-name" value="' + _esc2(page.name || ('Page ' + (pi + 1))) + '" placeholder="Page name" />' +
                                '<span class="ar-pp-count">' + (page.charts || []).length + ' charts</span>' +
                                '<button class="ar-pp-x" data-action="del-page" title="Remove page"><i class="bi bi-trash3"></i></button>' +
                            '</div>' +
                            '<div class="ar-pp-charts">' + (chartsHtml || '<div class="ar-pp-empty">No charts on this page</div>') + '</div>' +
                        '</div>';
                }).join('');

                modal.innerHTML =
                    '<div class="ar-modal-header" style="background:linear-gradient(135deg,#4A90D9 0%,#2C6FAC 100%)"><i class="bi bi-clipboard-check"></i>Review AI Plan</div>' +
                    '<div class="ar-modal-body" style="padding:16px 20px">' +
                        '<div style="font-size:0.82rem;color:var(--cp-text-secondary,#6c757d);margin-bottom:12px">' +
                            'The AI has drafted a report with <strong>' + editable.pages.length + '</strong> page' + (editable.pages.length !== 1 ? 's' : '') +
                            ' and <strong>' + totalCharts + '</strong> chart' + (totalCharts !== 1 ? 's' : '') + '. Edit titles, remove items, or regenerate before building.' +
                        '</div>' +
                        '<div class="ar-pp-list">' + (pagesHtml || '<div class="ar-pp-empty">No pages in plan</div>') + '</div>' +
                    '</div>' +
                    '<div class="ar-modal-footer" style="justify-content:space-between">' +
                        '<button class="ar-cancel-btn" id="arPpCancel">Cancel</button>' +
                        '<div style="display:flex;gap:8px">' +
                            '<button class="ar-cancel-btn" id="arPpRegen" title="Ask AI for a different plan"><i class="bi bi-arrow-repeat me-1"></i>Regenerate</button>' +
                            '<button class="ar-generate-btn" id="arPpBuild"' + (totalCharts === 0 ? ' disabled' : '') + '><i class="bi bi-play-fill me-1"></i>Build Report</button>' +
                        '</div>' +
                    '</div>';

                _injectPreviewStyles();
                _wireEvents();
            }

            function _wireEvents() {
                modal.querySelectorAll('.ar-pp-page-name').forEach(function (inp) {
                    inp.addEventListener('input', function () {
                        var pi = parseInt(inp.closest('.ar-pp-page').dataset.pi, 10);
                        if (editable.pages[pi]) editable.pages[pi].name = inp.value;
                    });
                });
                modal.querySelectorAll('.ar-pp-chart-title').forEach(function (inp) {
                    inp.addEventListener('input', function () {
                        var card = inp.closest('.ar-pp-chart');
                        var pi = parseInt(card.dataset.pi, 10);
                        var ci = parseInt(card.dataset.ci, 10);
                        if (editable.pages[pi] && editable.pages[pi].charts[ci]) {
                            editable.pages[pi].charts[ci].title = inp.value;
                        }
                    });
                });
                modal.querySelectorAll('[data-action="del-chart"]').forEach(function (btn) {
                    btn.addEventListener('click', function () {
                        var card = btn.closest('.ar-pp-chart');
                        var pi = parseInt(card.dataset.pi, 10);
                        var ci = parseInt(card.dataset.ci, 10);
                        if (editable.pages[pi]) editable.pages[pi].charts.splice(ci, 1);
                        render();
                    });
                });
                modal.querySelectorAll('[data-action="del-page"]').forEach(function (btn) {
                    btn.addEventListener('click', function () {
                        var pg = btn.closest('.ar-pp-page');
                        var pi = parseInt(pg.dataset.pi, 10);
                        editable.pages.splice(pi, 1);
                        render();
                    });
                });
                document.getElementById('arPpCancel').addEventListener('click', function () {
                    _removeOverlay();
                    _isRunning = false;
                    if (_timerInterval) { clearInterval(_timerInterval); _timerInterval = null; }
                    resolve({ action: 'cancel' });
                });
                document.getElementById('arPpRegen').addEventListener('click', function () {
                    resolve({ action: 'regenerate' });
                });
                var buildBtn = document.getElementById('arPpBuild');
                if (buildBtn) buildBtn.addEventListener('click', function () {
                    // Strip empty pages
                    editable.pages = editable.pages.filter(function (p) {
                        return (p.charts || []).length > 0;
                    });
                    resolve({ action: 'build', plan: editable });
                });
            }

            render();
        });
    }

    // Inject minimal CSS for the plan preview (once).
    function _injectPreviewStyles() {
        if (document.getElementById('ar-pp-styles')) return;
        var s = document.createElement('style');
        s.id = 'ar-pp-styles';
        s.textContent = [
            '.ar-pp-list{max-height:62vh;overflow-y:auto;padding-right:4px;display:flex;flex-direction:column;gap:10px}',
            '.ar-pp-page{border:1px solid var(--cp-border,#e2e8f0);border-radius:8px;background:var(--cp-surface,#fff);max-height:320px;overflow-y:auto;display:flex;flex-direction:column}',
            '.ar-pp-page-head{display:flex;align-items:center;gap:8px;padding:8px 10px;background:var(--cp-bg-alt,#f8fafc);border-bottom:1px solid var(--cp-border,#e2e8f0);position:sticky;top:0;z-index:1}',
            '.ar-pp-page-head i.bi{color:var(--cp-primary,#4A90D9);font-size:1rem}',
            '.ar-pp-page-name{flex:1;border:1px solid transparent;background:transparent;font-weight:600;font-size:0.85rem;color:var(--cp-text,#1e2d3d);padding:3px 6px;border-radius:4px}',
            '.ar-pp-page-name:hover,.ar-pp-page-name:focus{border-color:var(--cp-border,#e2e8f0);background:#fff;outline:none}',
            '.ar-pp-count{font-size:0.72rem;color:var(--cp-text-secondary,#6c757d);padding:2px 8px;background:var(--cp-surface,#fff);border:1px solid var(--cp-border,#e2e8f0);border-radius:10px;flex-shrink:0}',
            '.ar-pp-charts{padding:6px 10px 10px;display:flex;flex-direction:column;gap:4px}',
            '.ar-pp-chart{display:flex;align-items:center;gap:8px;padding:5px 6px;border-radius:6px}',
            '.ar-pp-chart:hover{background:var(--cp-bg-alt,#f8fafc)}',
            '.ar-pp-chart-type{font-size:0.68rem;font-weight:600;text-transform:uppercase;letter-spacing:0.02em;color:#fff;background:var(--cp-purple,#9B59B6);padding:2px 8px;border-radius:10px;flex-shrink:0;max-width:120px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}',
            '.ar-pp-chart-title{flex:1;min-width:120px;border:1px solid transparent;background:transparent;font-size:0.82rem;color:var(--cp-text,#1e2d3d);padding:3px 6px;border-radius:4px}',
            '.ar-pp-chart-title:hover,.ar-pp-chart-title:focus{border-color:var(--cp-border,#e2e8f0);background:#fff;outline:none}',
            '.ar-pp-chart-meta{font-size:0.7rem;color:var(--cp-text-secondary,#6c757d);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:38%}',
            '.ar-pp-x{flex-shrink:0;border:1px solid var(--cp-border,#e2e8f0);background:var(--cp-surface,#fff);color:#dc3545;border-radius:4px;padding:2px 6px;font-size:0.75rem;cursor:pointer}',
            '.ar-pp-x:hover{background:#dc3545;color:#fff;border-color:#dc3545}',
            '.ar-pp-empty{font-size:0.76rem;color:var(--cp-text-secondary,#6c757d);padding:8px;text-align:center;font-style:italic}'
        ].join('\n');
        document.head.appendChild(s);
    }

    // Phase 28-A5: named phases give users a narrative instead of a raw percent.
    var _PHASES = {
        schema: { icon: '\uD83D\uDD0D', label: 'Reading your schema\u2026',         pct: 8  },
        design: { icon: '\uD83E\uDDE0', label: 'Designing pages with AI\u2026',       pct: 30 },
        place:  { icon: '\uD83D\uDCCA', label: 'Placing visuals on the canvas\u2026', pct: 55 },
        polish: { icon: '\u2728',       label: 'Applying theme & alignment\u2026',     pct: 95 }
    };
    function _setPhase(key) {
        var p = _PHASES[key]; if (!p) return;
        _setStatus(p.icon + '  ' + p.label);
        _setProgress(p.pct);
    }

    function _showDone(pageCount, chartCount) {
        _setStep('done');
        _setProgress(100);
        var body = _overlay?.querySelector('.ar-modal-body');
        if (body) {
            body.innerHTML =
                '<div class="ar-done-msg">' +
                    '<i class="bi bi-check-circle-fill"></i>' +
                    '<div class="ar-done-text">Report Generated!</div>' +
                    '<div class="ar-done-sub">' + pageCount + ' page' + (pageCount !== 1 ? 's' : '') +
                    ' · ' + chartCount + ' chart' + (chartCount !== 1 ? 's' : '') + ' created</div>' +
                '</div>';
        }
        var footer = _overlay?.querySelector('.ar-modal-footer');
        if (footer) {
            footer.innerHTML = '<button class="ar-generate-btn" id="arDoneClose">Done</button>';
            document.getElementById('arDoneClose').addEventListener('click', _removeOverlay);
        }
    }

    // ── Main generation logic ────────────────────────────────────────
    async function _startGeneration(prompt, tables, existingCharts) {
        if (!global.canvasManager) return;
        // Reset stuck state if a previous run didn't clean up
        if (_isRunning && _abortController) { _abortController.abort(); }
        _isRunning = true;

        _showProgressModal();
        _setProgress(5);

        _abortController = new AbortController();
        // Auto-timeout after 2 minutes to prevent stuck state
        var _fetchTimeout = setTimeout(function () { if (_abortController) _abortController.abort(); }, 120000);
        var user = null;
        try { user = JSON.parse(localStorage.getItem('cp_user') || 'null'); } catch (e) {}

        // Always delete all pages and start fresh
        var isRedesign = !!(existingCharts);
        _setPhase('schema');
        _setStatus(isRedesign ? '\uD83E\uDDF9  Clearing canvas for redesign\u2026' : '\uD83D\uDD0D  Reading your schema\u2026');
        await global.canvasManager.deleteAllPages();
        await new Promise(function (r) { setTimeout(r, 300); });

        try {
            // 1. Call AI to get report plan
            _setPhase('design');
            _setStep('plan');

            var response = await fetch('/api/auto-report/generate', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer ' + (localStorage.getItem('cp_token') || '')
                },
                body: JSON.stringify({
                    workspaceId: _getWorkspaceId(),
                    userId: user?.id || '',
                    datasourceId: global.currentDatasourceId || null,
                    prompt: prompt,
                    tableNames: tables,
                    existingCharts: existingCharts || ''
                }),
                signal: _abortController.signal
            });

            if (!response.ok) {
                throw new Error('AI request failed (HTTP ' + response.status + ')');
            }

            // 2. Stream and accumulate the response
            var fullText = '';
            if (global.aiStream && global.aiStream.readSseText) {
                var result = await global.aiStream.readSseText(response, function (chunk) {
                    fullText += chunk;
                    _setStatus('Receiving report plan… (' + fullText.length + ' chars)');
                    _setProgress(10 + Math.min(30, fullText.length / 50));
                });
                fullText = result.fullText || fullText;
            } else {
                var body = await response.text();
                // Parse SSE manually
                body.split('\n').forEach(function (line) {
                    if (line.startsWith('data: ') && line !== 'data: [DONE]') {
                        try {
                            var obj = JSON.parse(line.substring(6));
                            if (obj.text) fullText += obj.text;
                            if (obj.error) throw new Error(obj.error);
                        } catch (e) {}
                    }
                });
            }

            _setProgress(40);
            _setStatus('Parsing report plan…');

            // 3. Parse the JSON plan from AI response
            var plan = _extractJson(fullText);
            if (!plan || !plan.pages || !plan.pages.length) {
                throw new Error('AI did not return a valid report plan. Please try again.');
            }

            // 3.5 Phase 31: Interactive plan preview — let the user review
            // and edit the plan before we build pages & charts.
            _setProgress(42);
            _setStatus('Waiting for your review…');
            var preview = await _showPlanPreview(plan);
            if (preview.action === 'cancel') {
                _isRunning = false;
                if (_timerInterval) { clearInterval(_timerInterval); _timerInterval = null; }
                clearTimeout(_fetchTimeout);
                return;
            }
            if (preview.action === 'regenerate') {
                _isRunning = false;
                if (_timerInterval) { clearInterval(_timerInterval); _timerInterval = null; }
                clearTimeout(_fetchTimeout);
                _removeOverlay();
                // Small delay so the overlay has a chance to unmount before we
                // rebuild a fresh progress modal.
                setTimeout(function () { _startGeneration(prompt, tables, existingCharts); }, 120);
                return;
            }
            plan = preview.plan || plan;
            if (!plan.pages || !plan.pages.length) {
                throw new Error('No pages left to build. Please regenerate.');
            }
            // Rebuild the progress modal so the subsequent steps have their UI.
            _showProgressModal();
            _setPhase('design');
            _setStep('plan');
            _setProgress(44);

            // 4. Build pages and charts
            _setStep('pages');
            _setProgress(45);

            var totalCharts = 0;
            plan.pages.forEach(function (p) { totalCharts += (p.charts || []).length; });
            var chartsDone = 0;

            // Use existing pages or create new ones
            var cm = global.canvasManager;
            var startPageIdx = cm.pages.length;

            for (var pi = 0; pi < plan.pages.length; pi++) {
                var pageDef = plan.pages[pi];
                _setStatus('Creating page: ' + (pageDef.name || ('Page ' + (pi + 1))));
                _setStep('pages');
                _setProgress(45 + (pi / plan.pages.length) * 10);

                // Add a new page (except for first page if canvas is empty)
                if (pi === 0 && cm.charts.length === 0 && cm.pages.length === 1) {
                    // Reuse the existing empty first page
                    if (pageDef.name) {
                        cm.pages[0].name = pageDef.name;
                        cm.renderPageTabs();
                        try {
                            fetch(_chartApiUrl('/api/page/rename'), {
                                method: 'POST',
                                headers: { 'Content-Type': 'application/json' },
                                body: JSON.stringify({ index: 0, name: pageDef.name })
                            });
                        } catch (e) {}
                    }
                } else {
                    await cm.addPage();
                    var newIdx = cm.pages.length - 1;
                    if (pageDef.name) {
                        cm.pages[newIdx].name = pageDef.name;
                        cm.renderPageTabs();
                        try {
                            fetch(_chartApiUrl('/api/page/rename'), {
                                method: 'POST',
                                headers: { 'Content-Type': 'application/json' },
                                body: JSON.stringify({ index: newIdx, name: pageDef.name })
                            });
                        } catch (e) {}
                    }
                }

                // Add charts to this page using pixel-packing layout
                _setStep('charts');
                _setPhase('place');
                var charts = pageDef.charts || [];

                // Pixel-packing layout: position cards using their ACTUAL rendered width
                // (cm.colsToPixels) so the card and its x-coordinate never disagree. This
                // avoids the old bug where colsToPixels hit its 200px floor while grid math
                // still used full canvas-width/12 slots, producing tiny cards with huge gaps.
                var margin = 20;        // gutter on all sides of the canvas
                var rowGap = 20;        // vertical gap between rows
                var colGap = 16;        // horizontal gap between cards in a row
                var MIN_CHART_PX = 189; // 5 cm @ 96dpi — minimum rendered chart width

                // Determine the real usable layout width by measuring the DOM. We lay
                // out inside the VIEWPORT (so charts are visible without scrolling) but
                // never wider than the drop-zone, and never narrower than a single
                // full-width card (so a 12-col card always fits).
                var canvasEl = document.getElementById('chart-canvas-drop');
                var scrollEl = canvasEl ? canvasEl.parentElement : null;
                var dropZoneTotal = canvasEl ? canvasEl.clientWidth : 0;
                var viewportW = scrollEl ? scrollEl.clientWidth : (canvasEl ? canvasEl.clientWidth : 0);
                var cardMaxW = cm.colsToPixels(12);

                var canvasTotal = Math.max(cardMaxW + margin * 2, viewportW || cardMaxW + margin * 2);
                if (dropZoneTotal > 0) canvasTotal = Math.min(canvasTotal, dropZoneTotal);
                if (canvasTotal < MIN_CHART_PX + margin * 2) {
                    canvasTotal = MIN_CHART_PX + margin * 2;
                }
                var canvasBase = canvasTotal - (margin * 2); // usable width between side margins

                // Minimum col count needed for a card to reach 5 cm after colsToPixels
                // (accounting for its 200 px floor).
                var MIN_COLS = 3;
                for (var _w = 3; _w <= 12; _w++) {
                    if (cm.colsToPixels(_w) >= MIN_CHART_PX) { MIN_COLS = _w; break; }
                }

                // Row bookkeeping for pixel packing.
                var rowY = margin;
                var rowItems = [];
                var rowWidth = 0;
                var rowMaxH = 0;

                // Track ids of charts INSERTED BY THIS RUN so the post-layout
                // helpers only touch newly-added cards. Pre-existing user-arranged
                // charts keep their posX/posY exactly as the user left them.
                var newChartIds = new Set();

                // Close the current row and start a new one below.
                function _commitRow() {
                    if (rowItems.length === 0) return;
                    rowY += rowMaxH + rowGap;
                    rowItems = [];
                    rowWidth = 0;
                    rowMaxH = 0;
                }

                for (var ci = 0; ci < charts.length; ci++) {
                    var chartDef = charts[ci];
                    chartsDone++;
                    _setStatus('\uD83D\uDCCA  Placing \u201C' + (chartDef.title || 'Chart') + '\u201D  (' + chartsDone + ' of ' + totalCharts + ')');
                    _setProgress(55 + (chartsDone / totalCharts) * 38);

                    // Clamp AI-provided width into [MIN_COLS..12] so every chart is ≥ 5cm wide
                    var requestedW = parseInt(chartDef.width, 10);
                    if (!requestedW || isNaN(requestedW)) requestedW = 6;
                    var chartWidth = Math.max(MIN_COLS, Math.min(12, requestedW));

                    // Dashboard rhythm: choose col counts that produce a proper
                    // dashboard look & feel instead of a pile of narrow 3-col cards.
                    var ct = (chartDef.chartType || '').toLowerCase();
                    var isKpi = (ct === 'kpi' || ct === 'kpicard' || ct === 'card' || ct === 'metrictile');
                    var isCircular = (ct === 'pie' || ct === 'doughnut' || ct === 'donut' ||
                        ct === 'piechart' || ct === 'doughnutchart' || ct === 'gauge' ||
                        ct === 'radar' || ct === 'polararea' || ct === 'nightingalerose' ||
                        ct === 'radialprogress' || ct === 'sunburst');
                    var isSeries = (ct === 'bar' || ct === 'column' || ct === 'horizontalbar' ||
                        ct === 'stackedbar' || ct === 'groupedbar' || ct === 'line' ||
                        ct === 'area' || ct === 'stepline' || ct === 'mixedbarline' ||
                        ct === 'histogram' || ct === 'pareto' || ct === 'waterfall' ||
                        ct === 'funnel' || ct === 'scatter' || ct === 'bubble' ||
                        ct === 'timeline' || ct === 'controlchart');
                    if (isKpi) {
                        // Count total single-metric cards on this page so the top row
                        // forms a balanced 2/3/4-up grid. Also verify the row actually
                        // fits the canvas — colsToPixels has a 200px floor that can push
                        // 4-up over the edge on narrow canvases, leaving an ugly wrap.
                        var kpiCount = charts.filter(function (x) {
                            var xt = (x.chartType || '').toLowerCase();
                            return xt === 'kpi' || xt === 'kpicard' || xt === 'card' || xt === 'metrictile';
                        }).length;
                        var desiredN = Math.max(1, Math.min(4, kpiCount || 1));
                        chartWidth = 6; // 2-up fallback
                        for (var _n = desiredN; _n >= 2; _n--) {
                            var _w = Math.max(3, Math.floor(12 / _n));
                            var _total = _n * cm.colsToPixels(_w) + (_n - 1) * colGap;
                            if (_total <= canvasBase) { chartWidth = _w; break; }
                        }
                    } else if (ct === 'table') {
                        chartWidth = 12; // tables always span full canvas width
                    } else if (isSeries) {
                        chartWidth = Math.max(chartWidth, 8); // dense axes need width
                    } else if (isCircular) {
                        chartWidth = Math.max(chartWidth, 6); // circular charts 2-up
                    } else {
                        // Any other chart type: never render as a cramped 3-col tile.
                        chartWidth = Math.max(chartWidth, 6);
                    }

                    // Shrink cols if colsToPixels at render time would overflow the real canvas
                    // (canvasManager sizes the card from its own colsToPixels, so we must reduce cols,
                    // not just the local posX math, to prevent the visual from escaping).
                    while (chartWidth > MIN_COLS && cm.colsToPixels(chartWidth) > canvasBase) {
                        chartWidth--;
                    }
                    // Use better default heights per chart type for presentable layout.
                    // Kept compact so rows don't leave huge vertical whitespace.
                    var defaultH = 260;
                    if (chartDef.chartType === 'kpi' || chartDef.chartType === 'kpiCard' ||
                        chartDef.chartType === 'card' || chartDef.chartType === 'metricTile') defaultH = 150;
                    else if (chartDef.chartType === 'shape-textbox') defaultH = 70;
                    else if (chartDef.chartType === 'table') defaultH = 340;
                    else if (isCircular) defaultH = 280;
                    var chartHeight = Math.max(80, Math.min(500, chartDef.height || defaultH));

                    // Full-width charts always start a fresh row
                    if (chartWidth >= 10 && rowItems.length > 0) {
                        _commitRow();
                    }

                    // Pixel-packing: use the ACTUAL rendered width so posX and card size
                    // are always in sync. Wrap to a new row if this card won't fit.
                    var renderedW = cm.colsToPixels(chartWidth);
                    if (renderedW > canvasBase) renderedW = canvasBase;

                    var projected = (rowItems.length === 0 ? 0 : rowWidth + colGap) + renderedW;
                    if (rowItems.length > 0 && projected > canvasBase) {
                        _commitRow();
                    }

                    // Track full rendered card height (canvas-wrap + header + resize + borders)
                    var isShape = chartDef.chartType === 'shape-textbox' || chartDef.chartType === 'navigation';
                    var cardOverhead = isShape ? 4 : 44;
                    var fullCardH = chartHeight + cardOverhead;

                    // Provisional posX before row is committed; may be adjusted by _commitRow
                    var posX = margin + (rowItems.length === 0 ? 0 : rowWidth + colGap);
                    var posY = rowY;

                    rowItems.push({ posX: posX, w: renderedW, h: fullCardH });
                    rowWidth = (rowItems.length === 1) ? renderedW : (rowWidth + colGap + renderedW);
                    rowMaxH = Math.max(rowMaxH, fullCardH);

                    // Resolve tableName from AI response to a matching real table name
                    var aiTable = chartDef.tableName || '';
                    var resolvedTable = '';
                    if (aiTable && global._realTableNames) {
                        var lowerAi = aiTable.toLowerCase();
                        resolvedTable = global._realTableNames.find(function (t) {
                            var lt = t.toLowerCase();
                            return lt === lowerAi || lt.endsWith('.' + lowerAi) || lowerAi.endsWith('.' + lt);
                        }) || aiTable;
                    }

                    // Strip SQL brackets from labelField/valueField (e.g. [Region] → Region)
                    var stripBrackets = function (s) { return (s || '').replace(/^\[|\]$/g, ''); };
                    var aiLabel = stripBrackets(chartDef.labelField || '');
                    var aiValue = stripBrackets(chartDef.valueField || '');
                    var aiGroupBy = stripBrackets(chartDef.groupByField || '');

                    // Determine aggregation based on chart type
                    var aggFunc = 'SUM';
                    var aggEnabled = true;
                    if (chartDef.chartType === 'table' || chartDef.chartType === 'shape-textbox') {
                        aggFunc = 'None';
                        aggEnabled = false;
                    } else if (chartDef.chartType === 'kpi' || chartDef.chartType === 'card') {
                        aggFunc = 'SUM';
                    }

                    var partial = {
                        chartType: chartDef.chartType || 'bar',
                        title: chartDef.title || 'Chart',
                        datasourceId: global.currentDatasourceId || null,
                        datasetName: resolvedTable,
                        dataQuery: chartDef.dataQuery || '',
                        width: chartWidth,
                        height: chartHeight,
                        posX: posX,
                        posY: posY,
                        mapping: {
                            labelField: aiLabel,
                            valueField: aiValue,
                            valueFieldAgg: aggFunc,
                            groupByField: aiGroupBy,
                            xField: '',
                            yField: '',
                            rField: '',
                            multiValueFields: [],
                            tableFields: []
                        },
                        aggregation: {
                            function: aggFunc,
                            enabled: aggEnabled
                        },
                        style: {
                            backgroundColor: '#4A90D9',
                            borderColor: '#2C6FAC',
                            showLegend: chartDef.chartType !== 'kpi' && chartDef.chartType !== 'card' && chartDef.chartType !== 'shape-textbox',
                            legendPosition: 'top',
                            showTooltips: true,
                            fillArea: chartDef.chartType === 'area',
                            colorPalette: 'default',
                            showDataLabels: chartDef.chartType === 'kpi' || chartDef.chartType === 'card' || chartDef.chartType === 'pie' || chartDef.chartType === 'doughnut',
                            fontFamily: 'Inter, sans-serif',
                            titleFontSize: 14,
                            animated: true,
                            responsive: true,
                            borderRadius: '4'
                        },
                        rowLimit: 100,
                        filterWhere: ''
                    };

                    // Handle shape-textbox
                    if (chartDef.chartType === 'shape-textbox') {
                        partial.dataQuery = '';
                        partial.shapeProps = {
                            fillColor: '#FFFFFF',
                            strokeColor: '#E2E8F0',
                            strokeWidth: 1,
                            opacity: 1,
                            text: chartDef.text || chartDef.title || '',
                            fontSize: chartDef.width >= 10 ? 20 : 14,
                            fontColor: '#1E2D3D',
                            textAlign: 'left',
                            cornerRadius: 8
                        };
                    }

                    try {
                        var added = await cm.addChart(partial);
                        if (added && added.id) newChartIds.add(added.id);
                    } catch (e) {
                        console.warn('[auto-report] Failed to add chart:', chartDef.title, e);
                    }

                    // Small delay so UI can breathe
                    await new Promise(function (r) { setTimeout(r, 100); });
                }

                // Post-layout validation: walk every chart just added to this page
                // and verify its rendered DOM element is inside chart-canvas-drop.
                // If anything ended up out of range (e.g. AI-provided posX/posY
                // survived somewhere, or colsToPixels grew on resize), snap it back.
                _validateAndFixPagePositions(cm, margin, newChartIds);

                // Second verification pass — re-pack using ACTUAL rendered widths
                // from the DOM so cards tile tightly with uniform gaps regardless
                // of any drift between plan-time col math and render-time sizing.
                await new Promise(function (r) { setTimeout(r, 60); });
                _repackPageFromDom(cm, margin, rowGap, colGap, newChartIds);
            }

            // 5. Switch back to first generated page
            _setStep('done');
            _setPhase('polish');

            var firstPage = (startPageIdx === 0 && cm.charts.length > 0) ? 0 : startPageIdx;
            if (firstPage < cm.pages.length) {
                cm.switchPage(firstPage);
            }

            await new Promise(function (r) { setTimeout(r, 400); });

            clearTimeout(_fetchTimeout);
            if (_timerInterval) { clearInterval(_timerInterval); _timerInterval = null; }
            _showDone(plan.pages.length, chartsDone);
            _toast('Report generated: ' + plan.pages.length + ' pages, ' + chartsDone + ' charts', 'success');

        } catch (err) {
            if (err.name === 'AbortError') {
                _toast('Report generation cancelled', 'warn');
            } else {
                console.error('[auto-report] Error:', err);
                _setStatus('Error: ' + (err.message || 'Generation failed'));
                _setProgress(0);
                _toast('Report generation failed: ' + (err.message || 'Unknown error'), 'error');
            }
        } finally {
            _isRunning = false;
            _abortController = null;
            clearTimeout(_fetchTimeout);
            if (_timerInterval) { clearInterval(_timerInterval); _timerInterval = null; }
        }
    }

    // ── Extract JSON from AI text (handles markdown fences) ──────────
    function _extractJson(text) {
        if (!text) return null;
        // Try extracting from code fence
        var fenceMatch = text.match(/```(?:json)?\s*([\s\S]*?)```/i);
        var jsonStr = fenceMatch ? fenceMatch[1].trim() : text.trim();

        // Find the outermost { ... }
        var start = jsonStr.indexOf('{');
        if (start === -1) return null;
        var depth = 0, inStr = false, esc = false;
        for (var i = start; i < jsonStr.length; i++) {
            var ch = jsonStr[i];
            if (esc) { esc = false; continue; }
            if (ch === '\\' && inStr) { esc = true; continue; }
            if (ch === '"') { inStr = !inStr; continue; }
            if (inStr) continue;
            if (ch === '{') depth++;
            else if (ch === '}') {
                depth--;
                if (depth === 0) {
                    try { return JSON.parse(jsonStr.slice(start, i + 1)); }
                    catch (e) { return null; }
                }
            }
        }
        return null;
    }

    // ── Init ─────────────────────────────────────────────────────────
    function _init() {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', function () { _injectButton(); _wireToolbarButton(); });
        } else {
            _injectButton();
            _wireToolbarButton();
        }
        // Also re-inject if the welcome is re-rendered (e.g. after clearing chat)
        var observer = new MutationObserver(function () {
            var welcome = document.querySelector('#dcpMessages .dcp-welcome');
            if (welcome && !welcome.querySelector('.dcp-auto-report-btn')) {
                _injectButton();
            }
        });
        var target = document.getElementById('dcpMessages');
        if (target) observer.observe(target, { childList: true, subtree: true });
    }

    _init();

    // Expose for external use
    global.autoReportGenerator = {
        generate: function (prompt) {
            var tables = (global._realTableNames || []).filter(Boolean);
            _startGeneration(prompt || '', tables, '');
        },
        redesign: function (prompt) {
            var tables = (global._realTableNames || []).filter(Boolean);
            var existing = _collectExistingCharts();
            _startGeneration(prompt || 'Improve and redesign this report', tables, existing);
        }
    };

}(window));
