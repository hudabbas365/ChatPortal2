// Dashboard Chart Transfer
// Handles: receiving charts pushed from Chat, Ctrl+C / Ctrl+V copy-paste, copy button per card
(function () {
    'use strict';

    const PENDING_KEY   = 'cp_pending_chart';
    const CLIPBOARD_KEY = 'cp_chart_clipboard';

    let _clipboard = null;

    // ── Safe HTML escape ─────────────────────────────────────────────
    function _esc(str) {
        if (typeof escapeHtml === 'function') return escapeHtml(str);
        const d = document.createElement('div');
        d.appendChild(document.createTextNode(String(str ?? '')));
        return d.innerHTML;
    }

    // ── Toast notification ───────────────────────────────────────────
    function _ensureToastContainer() {
        let c = document.getElementById('dct-toast-container');
        if (!c) {
            c = document.createElement('div');
            c.id = 'dct-toast-container';
            c.className = 'dct-toast-container';
            document.body.appendChild(c);
        }
        return c;
    }

    function showToast(msg, type) {
        type = type || 'success';
        const container = _ensureToastContainer();
        const toast = document.createElement('div');
        toast.className = 'dct-toast dct-toast-' + type;
        const icons = { success: 'bi-check-circle-fill', info: 'bi-clipboard-check', warn: 'bi-exclamation-circle', error: 'bi-x-circle-fill' };
        const icon = icons[type] || 'bi-info-circle';
        toast.innerHTML = '<i class="bi ' + icon + ' me-2"></i><span>' + _esc(msg) + '</span>';
        container.appendChild(toast);
        requestAnimationFrame(function () { toast.classList.add('dct-toast-show'); });
        setTimeout(function () {
            toast.classList.remove('dct-toast-show');
            toast.addEventListener('transitionend', function () { toast.remove(); }, { once: true });
        }, 3200);
    }

    // ── Receive chart pushed from Chat page ──────────────────────────
    function receiveFromChat() {
        var raw = localStorage.getItem(PENDING_KEY);
        if (!raw) return;
        localStorage.removeItem(PENDING_KEY);
        var pending;
        try { pending = JSON.parse(raw); } catch (e) { return; }
        if (!pending || !Array.isArray(pending.labels) || !Array.isArray(pending.values)) return;

        var lf = pending.labelField || 'label';
        var vf = pending.valueField || 'value';
        // fetchData returns customJsonData directly; buildConfig reads data.labels / data.values
        var customData = { labels: pending.labels, values: pending.values };

        canvasManager.addChart({
            chartType     : pending.chartType  || 'bar',
            title         : pending.title      || 'Chat Chart',
            customJsonData: JSON.stringify(customData),
            mapping: {
                labelField  : lf,
                valueField  : vf,
                groupByField: '',
                xField      : '',
                yField      : '',
                rField      : '',
                multiValueFields: []
            }
        }).then(function (chart) {
            showToast('"' + (chart.title || 'Chart') + '" added to dashboard', 'success');
        }).catch(function () {
            showToast('Chart added to dashboard', 'success');
        });
    }

    // ── Copy a chart to the internal clipboard ───────────────────────
    function copyChart(chartId) {
        if (!window.canvasManager) return;
        var chart = canvasManager.charts.find(function (c) { return c.id === chartId; });
        if (!chart) return;
        _clipboard = JSON.parse(JSON.stringify(chart));
        try { localStorage.setItem(CLIPBOARD_KEY, JSON.stringify(_clipboard)); } catch (_) {}
        showToast('"' + chart.title + '" copied', 'info');
    }

    // ── Paste the clipboard chart onto the canvas ────────────────────
    function pasteChart() {
        if (!window.canvasManager) return;
        if (!_clipboard) {
            try { _clipboard = JSON.parse(localStorage.getItem(CLIPBOARD_KEY) || 'null'); } catch (_) {}
        }
        if (!_clipboard) {
            showToast('Nothing to paste — copy a chart first (Ctrl+C)', 'warn');
            return;
        }
        var copy = JSON.parse(JSON.stringify(_clipboard));
        copy.title = _clipboard.title.replace(/ \(Copy\)+$/, '') + ' (Copy)';
        copy.posX  = (_clipboard.posX || 0) + 30;
        copy.posY  = (_clipboard.posY || 0) + 30;
        canvasManager.addChart(copy).then(function (chart) {
            showToast('"' + chart.title + '" pasted', 'success');
        }).catch(function () {
            showToast('Chart pasted', 'success');
        });
    }

    // ── Keyboard shortcuts Ctrl+C / Ctrl+V ──────────────────────────
    function _wireKeyboard() {
        document.addEventListener('keydown', function (e) {
            if (e.target.matches('input, textarea, select')) return;
            var mod = e.ctrlKey || e.metaKey;
            if (mod && !e.shiftKey && e.key === 'c') {
                var selId = canvasManager && canvasManager.selectedChartId;
                if (selId) { e.preventDefault(); copyChart(selId); }
            }
            if (mod && !e.shiftKey && e.key === 'v') {
                e.preventDefault();
                pasteChart();
            }
        });
    }

    // ── Inject per-card copy button ──────────────────────────────────
    function _injectCopyBtn(card) {
        if (card.querySelector('[data-action="copy-chart"]')) return;
        var dupBtn = card.querySelector('[data-action="duplicate"]');
        if (!dupBtn) return;

        var btn = document.createElement('button');
        btn.className = 'btn btn-xs btn-icon dct-copy-btn';
        btn.dataset.action = 'copy-chart';
        btn.title = 'Copy chart (Ctrl+C)';
        btn.innerHTML = '<i class="bi bi-clipboard"></i>';

        btn.addEventListener('click', function (e) {
            e.stopPropagation();
            copyChart(card.dataset.chartId);
            btn.innerHTML = '<i class="bi bi-clipboard-check text-success"></i>';
            setTimeout(function () { btn.innerHTML = '<i class="bi bi-clipboard"></i>'; }, 1600);
        });

        dupBtn.insertAdjacentElement('beforebegin', btn);
    }

    function _wireChartCards() {
        var container = document.getElementById('chart-canvas-drop');
        if (!container) return;

        container.querySelectorAll('.chart-card').forEach(_injectCopyBtn);

        new MutationObserver(function (mutations) {
            mutations.forEach(function (m) {
                m.addedNodes.forEach(function (node) {
                    if (node.nodeType === 1 && node.classList.contains('chart-card')) {
                        _injectCopyBtn(node);
                    }
                });
            });
        }).observe(container, { childList: true });
    }

    // ── Paste button in the top navbar ───────────────────────────────
    function _wirePasteBtn() {
        var actions = document.querySelector('.navbar-actions');
        if (!actions || document.getElementById('dct-paste-btn')) return;
        var btn = document.createElement('button');
        btn.id = 'dct-paste-btn';
        btn.className = 'btn btn-sm btn-outline-secondary me-1 dct-navbar-btn';
        btn.title = 'Paste chart (Ctrl+V)';
        btn.innerHTML = '<i class="bi bi-clipboard-plus me-1"></i>Paste';
        btn.addEventListener('click', pasteChart);
        var resetBtn = actions.querySelector('.btn-danger');
        if (resetBtn) resetBtn.insertAdjacentElement('beforebegin', btn);
        else actions.appendChild(btn);
    }

    // ── Public API ───────────────────────────────────────────────────
    window.dashboardChartTransfer = {
        copyChart      : copyChart,
        pasteChart     : pasteChart,
        receiveFromChat: receiveFromChat,
        showToast      : showToast
    };

    document.addEventListener('DOMContentLoaded', function () {
        _wireKeyboard();
        _wireChartCards();
        _wirePasteBtn();
        // receiveFromChat() is called explicitly from the inline script in
        // Dashboard/Index.cshtml, AFTER canvasManager.init(), to guarantee ordering.
    });
})();
