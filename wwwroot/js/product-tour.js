// product-tour.js — Lightweight first-login product tour
// Renders an overlay + spotlight + tooltip card walking the user through
// key navigation features. Persistence: localStorage flag `cp_tour_completed_v1`.
// Replay: call `window.productTour.start()` from anywhere (user menu link).
(function (global) {
    'use strict';

    var STORAGE_KEY = 'cp_tour_completed_v1';

    var _steps = [];
    var _idx = 0;
    var _overlay = null;
    var _spotlight = null;
    var _card = null;
    var _resizeBound = null;
    var _running = false;

    // ── DOM helpers ──────────────────────────────────────────────────
    function _make(tag, cls, html) {
        var el = document.createElement(tag);
        if (cls) el.className = cls;
        if (html != null) el.innerHTML = html;
        return el;
    }

    function _esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // Resolve the current step's target element. Returns null if none/missing.
    function _resolveTarget(step) {
        if (!step || !step.target) return null;
        try {
            var el = document.querySelector(step.target);
            return el && el.offsetParent !== null ? el : null;
        } catch (e) { return null; }
    }

    // Position the spotlight ring + tooltip card relative to the target.
    function _positionStep(step) {
        var target = _resolveTarget(step);
        var card = _card;
        if (!card) return;

        if (!target) {
            // Centered card (welcome/finish/preflight)
            card.classList.add('pt-centered');
            if (_spotlight) _spotlight.style.display = 'none';
            return;
        }

        card.classList.remove('pt-centered');
        if (_spotlight) _spotlight.style.display = 'block';

        // Bring target into view first
        try { target.scrollIntoView({ behavior: 'smooth', block: 'center', inline: 'center' }); } catch (e) {}

        var rect = target.getBoundingClientRect();
        var pad = 8;

        // Spotlight ring
        _spotlight.style.left = (rect.left - pad) + 'px';
        _spotlight.style.top = (rect.top - pad) + 'px';
        _spotlight.style.width = (rect.width + pad * 2) + 'px';
        _spotlight.style.height = (rect.height + pad * 2) + 'px';

        // Card placement: prefer below; flip above if not enough room.
        var placement = step.placement || 'bottom';
        var cardRect = card.getBoundingClientRect();
        var cardW = cardRect.width || 360;
        var cardH = cardRect.height || 200;
        var gap = 16;
        var vw = window.innerWidth;
        var vh = window.innerHeight;

        var top, left;
        if (placement === 'top' || (placement === 'bottom' && rect.bottom + gap + cardH > vh - 8)) {
            top = rect.top - gap - cardH;
            placement = 'top';
        } else {
            top = rect.bottom + gap;
            placement = 'bottom';
        }
        left = rect.left + rect.width / 2 - cardW / 2;
        if (left < 12) left = 12;
        if (left + cardW > vw - 12) left = vw - 12 - cardW;
        if (top < 12) top = rect.bottom + gap;

        card.style.left = left + 'px';
        card.style.top = top + 'px';

        // Reposition arrow
        var arrow = card.querySelector('.pt-arrow');
        if (arrow) {
            arrow.className = 'pt-arrow pt-arrow-' + (placement === 'top' ? 'bottom' : 'top');
            var targetCenter = rect.left + rect.width / 2;
            var arrowLeft = targetCenter - left;
            arrowLeft = Math.max(16, Math.min(cardW - 16, arrowLeft));
            arrow.style.left = arrowLeft + 'px';
            arrow.style.marginLeft = '-7px';
        }
    }

    // Render the body of a single step into the card.
    function _renderCard() {
        var step = _steps[_idx];
        if (!step || !_card) return;

        var dots = _steps.map(function (_, i) {
            return '<span class="pt-progress-dot' + (i <= _idx ? ' pt-done' : '') + '"></span>';
        }).join('');

        var bodyHtml;
        if (step.type === 'datasource-preflight') {
            bodyHtml = _renderDatasourcePreflight();
        } else {
            bodyHtml = step.body || '';
        }

        var prevDisabled = _idx === 0 ? 'disabled' : '';
        var isLast = _idx === _steps.length - 1;
        var nextLabel = isLast
            ? '<i class="bi bi-check-lg"></i> Finish'
            : 'Next <i class="bi bi-arrow-right"></i>';

        _card.innerHTML =
            '<div class="pt-arrow"></div>' +
            '<div class="pt-card-header">' +
                '<i class="bi ' + (step.icon || 'bi-stars') + '"></i>' +
                '<span>' + _esc(step.title || '') + '</span>' +
            '</div>' +
            '<div class="pt-card-body">' + bodyHtml + '</div>' +
            '<div class="pt-card-footer">' +
                '<div class="pt-progress">' +
                    'Step ' + (_idx + 1) + ' of ' + _steps.length +
                    '<span class="pt-progress-dots">' + dots + '</span>' +
                '</div>' +
                '<div class="pt-actions">' +
                    '<button type="button" class="pt-btn-link" data-pt="skip">Skip</button>' +
                    '<button type="button" class="pt-btn" data-pt="prev" ' + prevDisabled + '><i class="bi bi-arrow-left"></i> Back</button>' +
                    '<button type="button" class="pt-btn pt-btn-primary" data-pt="next">' + nextLabel + '</button>' +
                '</div>' +
            '</div>';

        // Wire buttons
        _card.querySelector('[data-pt="skip"]').addEventListener('click', _finish);
        _card.querySelector('[data-pt="prev"]').addEventListener('click', function () {
            if (_idx > 0) { _idx--; _go(); }
        });
        _card.querySelector('[data-pt="next"]').addEventListener('click', function () {
            if (_idx >= _steps.length - 1) { _finish(true); }
            else { _idx++; _go(); }
        });

        // Wire datasource-preflight tabs if present
        var tabs = _card.querySelectorAll('.pt-ds-tab');
        if (tabs.length) {
            tabs.forEach(function (tab) {
                tab.addEventListener('click', function () {
                    tabs.forEach(function (t) { t.classList.remove('pt-active'); });
                    tab.classList.add('pt-active');
                    var key = tab.dataset.dsType;
                    var lists = _card.querySelectorAll('.pt-ds-checklist');
                    lists.forEach(function (l) {
                        l.style.display = (l.dataset.dsType === key) ? '' : 'none';
                    });
                });
            });
        }

        // Show with animation, then position (after layout).
        requestAnimationFrame(function () {
            _card.classList.add('pt-show');
            _positionStep(step);
        });
    }

    function _renderDatasourcePreflight() {
        var groups = [
            { key: 'sql', label: 'SQL', icon: 'bi-database', items: [
                'A reachable host & port (or public/whitelisted IP)',
                'A database user with SELECT permission',
                'The database name and a SELECT-able schema/table',
                'Network firewall allowing outbound from our servers'
            ]},
            { key: 'rest', label: 'REST API', icon: 'bi-cloud', items: [
                'The base URL of the API',
                'Authentication: API key, Bearer token, or Basic auth',
                'A sample endpoint that returns JSON',
                'Rate limits / quotas you should respect'
            ]},
            { key: 'powerbi', label: 'Power BI', icon: 'bi-bar-chart', items: [
                'An Azure AD app registration (client ID + secret)',
                'Your Power BI tenant ID',
                'Workspace + Dataset IDs you want to query',
                'Service principal added as a Power BI workspace member'
            ]},
            { key: 'file', label: 'File / URL', icon: 'bi-file-earmark', items: [
                'A publicly reachable URL (CSV, JSON, Excel)',
                'Or upload the file directly in the wizard',
                'Stable column names — they become your fields',
                'Reasonable file size (under ~50 MB recommended)'
            ]}
        ];

        var tabs = groups.map(function (g, i) {
            return '<button type="button" class="pt-ds-tab' + (i === 0 ? ' pt-active' : '') + '" data-ds-type="' + g.key + '"><i class="bi ' + g.icon + ' me-1"></i>' + g.label + '</button>';
        }).join('');

        var lists = groups.map(function (g, i) {
            var items = g.items.map(function (it) {
                return '<li><i class="bi bi-check-circle-fill"></i><span>' + _esc(it) + '</span></li>';
            }).join('');
            return '<ul class="pt-ds-checklist" data-ds-type="' + g.key + '" style="' + (i === 0 ? '' : 'display:none') + '">' + items + '</ul>';
        }).join('');

        return '<p>Before connecting a datasource, make sure you have the right credentials and access. Pick the type you plan to add:</p>' +
               '<div class="pt-ds-tabs">' + tabs + '</div>' +
               lists +
               '<p style="margin-top:10px;color:var(--pt-text-muted);font-size:.78rem">' +
               '<i class="bi bi-shield-lock me-1"></i>Credentials are encrypted at rest. Your business data never leaves your own database.</p>';
    }

    // Move to the current step (re-render + reposition).
    function _go() {
        if (!_card) return;
        _card.classList.remove('pt-show');
        setTimeout(_renderCard, 120);
    }

    // ── Lifecycle ────────────────────────────────────────────────────
    function _start(steps) {
        if (_running) return;
        if (!Array.isArray(steps) || steps.length === 0) return;
        _steps = steps;
        _idx = 0;
        _running = true;

        _overlay = _make('div', 'pt-overlay');
        document.body.appendChild(_overlay);
        requestAnimationFrame(function () { _overlay.classList.add('pt-active'); });

        _spotlight = _make('div', 'pt-spotlight');
        _spotlight.style.display = 'none';
        document.body.appendChild(_spotlight);

        _card = _make('div', 'pt-card');
        document.body.appendChild(_card);

        // Click overlay = no-op to avoid accidental dismiss; users use Skip/Finish.
        _overlay.addEventListener('click', function (e) { e.stopPropagation(); });

        // Keyboard: Esc=skip, Enter=next, ArrowLeft=back
        document.addEventListener('keydown', _onKey);

        _resizeBound = function () { _positionStep(_steps[_idx]); };
        window.addEventListener('resize', _resizeBound);
        window.addEventListener('scroll', _resizeBound, true);

        _renderCard();
    }

    function _onKey(e) {
        if (!_running) return;
        if (e.key === 'Escape') { e.preventDefault(); _finish(); }
        else if (e.key === 'Enter') {
            e.preventDefault();
            if (_idx >= _steps.length - 1) _finish(true);
            else { _idx++; _go(); }
        } else if (e.key === 'ArrowLeft' && _idx > 0) {
            e.preventDefault(); _idx--; _go();
        } else if (e.key === 'ArrowRight') {
            e.preventDefault();
            if (_idx >= _steps.length - 1) _finish(true);
            else { _idx++; _go(); }
        }
    }

    function _finish(completed) {
        if (!_running) return;
        _running = false;
        try { localStorage.setItem(STORAGE_KEY, completed ? 'completed' : 'skipped'); } catch (e) {}
        document.removeEventListener('keydown', _onKey);
        if (_resizeBound) {
            window.removeEventListener('resize', _resizeBound);
            window.removeEventListener('scroll', _resizeBound, true);
            _resizeBound = null;
        }
        if (_overlay) _overlay.classList.remove('pt-active');
        if (_card) _card.classList.remove('pt-show');
        setTimeout(function () {
            if (_overlay) _overlay.remove();
            if (_spotlight) _spotlight.remove();
            if (_card) _card.remove();
            _overlay = _spotlight = _card = null;
            _steps = []; _idx = 0;
        }, 280);

        if (completed && global.cpToast) {
            global.cpToast({ title: 'Tour complete', message: 'You can replay it anytime from your profile menu.', variant: 'success', duration: 3500 });
        }
    }

    function _hasCompleted() {
        try { return !!localStorage.getItem(STORAGE_KEY); }
        catch (e) { return false; }
    }

    function _reset() {
        try { localStorage.removeItem(STORAGE_KEY); } catch (e) {}
    }

    global.productTour = {
        start: _start,
        finish: _finish,
        hasCompleted: _hasCompleted,
        reset: _reset,
        isRunning: function () { return _running; }
    };
}(window));
