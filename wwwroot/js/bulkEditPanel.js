// Phase 32-B14 — Bulk-edit floating toolbar.
// When groupManager has 2+ charts selected, a compact toolbar appears at the
// bottom of the canvas with common style/layout properties. Changes are
// applied to every selected chart via canvasManager.updateChart.
(function (global) {
    'use strict';

    var PALETTES = ['default', 'vibrant', 'pastel', 'ocean', 'forest', 'sunset', 'mono'];
    var FONTS = [
        'Inter, sans-serif',
        'Roboto, sans-serif',
        'Segoe UI, sans-serif',
        'Georgia, serif',
        'Menlo, monospace'
    ];

    var _el = null;

    function _ensure() {
        if (_el) return _el;
        _el = document.createElement('div');
        _el.id = 'bulk-edit-toolbar';
        _el.className = 'bulk-edit-toolbar hidden';
        _el.innerHTML =
            '<div class="bet-group">' +
                '<i class="bi bi-pencil-square"></i>' +
                '<span class="bet-count" id="bet-count">0 selected</span>' +
            '</div>' +
            '<div class="bet-divider"></div>' +
            '<div class="bet-group">' +
                '<label>Palette</label>' +
                '<select id="bet-palette" class="form-select form-select-sm">' +
                    '<option value="">—</option>' +
                    PALETTES.map(function (p) { return '<option value="' + p + '">' + p + '</option>'; }).join('') +
                '</select>' +
            '</div>' +
            '<div class="bet-group">' +
                '<label>Font</label>' +
                '<select id="bet-font" class="form-select form-select-sm">' +
                    '<option value="">—</option>' +
                    FONTS.map(function (f) { return '<option value="' + f + '">' + f.split(',')[0] + '</option>'; }).join('') +
                '</select>' +
            '</div>' +
            '<div class="bet-group">' +
                '<label>Title size</label>' +
                '<input type="number" id="bet-title-size" class="form-control form-control-sm" min="10" max="28" step="1" placeholder="14" />' +
            '</div>' +
            '<div class="bet-group">' +
                '<label>Radius</label>' +
                '<input type="number" id="bet-radius" class="form-control form-control-sm" min="0" max="24" step="1" placeholder="4" />' +
            '</div>' +
            '<div class="bet-group bet-toggle">' +
                '<label><input type="checkbox" id="bet-animated" /> Animated</label>' +
                '<label><input type="checkbox" id="bet-legend" /> Legend</label>' +
            '</div>' +
            '<div class="bet-divider"></div>' +
            '<button type="button" class="btn btn-sm btn-primary" id="bet-apply"><i class="bi bi-check2"></i> Apply</button>' +
            '<button type="button" class="btn btn-sm btn-outline-secondary" id="bet-reset"><i class="bi bi-arrow-counterclockwise"></i></button>';
        document.body.appendChild(_el);

        document.getElementById('bet-apply').addEventListener('click', _apply);
        document.getElementById('bet-reset').addEventListener('click', _reset);
        return _el;
    }

    function _reset() {
        if (!_el) return;
        _el.querySelectorAll('select,input').forEach(function (i) {
            if (i.type === 'checkbox') i.indeterminate = true;
            else i.value = '';
        });
    }

    function _readForm() {
        var patch = {};
        var palette = document.getElementById('bet-palette').value;
        var font = document.getElementById('bet-font').value;
        var titleSize = document.getElementById('bet-title-size').value;
        var radius = document.getElementById('bet-radius').value;
        var animated = document.getElementById('bet-animated');
        var legend = document.getElementById('bet-legend');

        if (palette)   patch.colorPalette   = palette;
        if (font)      patch.fontFamily     = font;
        if (titleSize) patch.titleFontSize  = parseInt(titleSize, 10);
        if (radius !== '') patch.borderRadius = String(parseInt(radius, 10));
        if (!animated.indeterminate) patch.animated = animated.checked;
        if (!legend.indeterminate)   patch.showLegend = legend.checked;
        return patch;
    }

    async function _apply() {
        var gm = global.groupManager;
        var cm = global.canvasManager;
        if (!gm || !cm) return;
        var ids = gm.selectedIds || [];
        if (ids.length < 2) return;
        var patch = _readForm();
        if (Object.keys(patch).length === 0) {
            if (global.dashboardChartTransfer) {
                global.dashboardChartTransfer.showToast('Set at least one property first', 'warn');
            }
            return;
        }
        var applyBtn = document.getElementById('bet-apply');
        if (applyBtn) { applyBtn.disabled = true; applyBtn.innerHTML = '<i class="bi bi-hourglass-split"></i> Applying…'; }

        try {
            for (var i = 0; i < ids.length; i++) {
                var chart = cm.charts.find(function (c) { return c.id === ids[i]; });
                if (!chart) continue;
                chart.style = chart.style || {};
                if (patch.colorPalette   !== undefined) chart.style.colorPalette  = patch.colorPalette;
                if (patch.fontFamily     !== undefined) chart.style.fontFamily    = patch.fontFamily;
                if (patch.titleFontSize  !== undefined) chart.style.titleFontSize = patch.titleFontSize;
                if (patch.borderRadius   !== undefined) chart.style.borderRadius  = patch.borderRadius;
                if (patch.animated       !== undefined) chart.style.animated      = patch.animated;
                if (patch.showLegend     !== undefined) chart.style.showLegend    = patch.showLegend;
                await cm.updateChart(chart);
            }
            if (global.dashboardChartTransfer) {
                global.dashboardChartTransfer.showToast('Applied to ' + ids.length + ' charts', 'success');
            }
        } catch (err) {
            console.warn('[bulk-edit] failed:', err);
            if (global.dashboardChartTransfer) {
                global.dashboardChartTransfer.showToast('Bulk edit failed', 'error');
            }
        } finally {
            if (applyBtn) { applyBtn.disabled = false; applyBtn.innerHTML = '<i class="bi bi-check2"></i> Apply'; }
        }
    }

    function _onSelectionChanged(e) {
        var detail = e && e.detail ? e.detail : { count: 0 };
        var count = detail.count || 0;
        _ensure();
        if (count < 2) {
            _el.classList.add('hidden');
            return;
        }
        var cnt = document.getElementById('bet-count');
        if (cnt) cnt.textContent = count + ' selected';
        _el.classList.remove('hidden');
        // Default checkboxes to indeterminate so we don't force a state.
        var animated = document.getElementById('bet-animated');
        var legend = document.getElementById('bet-legend');
        if (animated) animated.indeterminate = true;
        if (legend) legend.indeterminate = true;
    }

    document.addEventListener('selection:changed', _onSelectionChanged);
    document.addEventListener('DOMContentLoaded', function () { _ensure(); });

    global.bulkEditPanel = { show: _onSelectionChanged, apply: _apply };
}(window));
