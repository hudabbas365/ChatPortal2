// chatUxEnhancements.js — First-wave Chat UX polish.
// Additive module: never touches chat.js. Uses MutationObserver to enrich
// DOM owned by chat.js (message bubbles, cohere status).
// Items: 5, 6, 7, 11, 12, 15, 16, 17, 18, 19, 25, 28.
(function () {
    'use strict';
    if (window.__chatUxEnhancementsLoaded) return;
    window.__chatUxEnhancementsLoaded = true;

    var SLASH_COMMANDS = [
        { key: '/summarize', label: 'Summarize this data', icon: 'bi-file-text' },
        { key: '/trends',    label: 'Show trends over time', icon: 'bi-graph-up' },
        { key: '/top',       label: 'Top N by metric',       icon: 'bi-trophy' },
        { key: '/compare',   label: 'Compare groups',        icon: 'bi-bar-chart' },
        { key: '/anomalies', label: 'Find anomalies',        icon: 'bi-exclamation-triangle' },
        { key: '/forecast',  label: 'Forecast next period',  icon: 'bi-arrow-up-right' },
        { key: '/clear',     label: 'Clear conversation',    icon: 'bi-trash' }
    ];

    function _toast(msg, type) {
        if (window.dashboardChartTransfer) {
            window.dashboardChartTransfer.showToast(msg, type || 'info');
        }
    }

    // ── Init ────────────────────────────────────────────────────────
    function init() {
        var messages = document.getElementById('chatMessages');
        var input    = document.getElementById('chatInput');
        var sendBtn  = document.getElementById('chatSendBtn');
        var composer = document.querySelector('.chat-input-container');
        if (!messages || !input) return;

        // Item 18 — ARIA live region for screen readers.
        messages.setAttribute('role', 'log');
        messages.setAttribute('aria-live', 'polite');
        messages.setAttribute('aria-relevant', 'additions');
        input.setAttribute('aria-label', 'Chat message');

        // Item 19 — honor reduced-motion preference system-wide.
        try {
            var mq = window.matchMedia('(prefers-reduced-motion: reduce)');
            var applyMotion = function () { document.documentElement.classList.toggle('reduce-motion', mq.matches); };
            applyMotion();
            mq.addEventListener && mq.addEventListener('change', applyMotion);
        } catch (e) {}

        _installAutoGrowAndCounter(input);
        _installSlashMenu(input);
        _installKeyboardShortcuts(input);
        _installJumpToLatest(messages);
        _installMessageObserver(messages);
        _installCohereErrorRetry(sendBtn);
        _installDatasourcePill();           // Item 3
        _installFloatingStop();             // Item 4
        _installCodeBlockActions(messages); // Item 13
        _installChipsScroll();              // Item 26
        _installCoherePanelPersistence();   // Item 27
        _installClearUndoToast();           // Item 24
        _installCostMeter();                // Item 23
        _installDynamicChips();             // Item 2
        _installSourcesFooter(messages);    // Item 22

        // Emit a ready signal for any late observers.
        document.dispatchEvent(new CustomEvent('chat-ux-ready'));
    }

    // ── Item 22 — Sources footer on assistant bubbles ───────────────
    // Inspects each assistant bubble for a SQL query / dataset name (produced
    // by chat.js's renderDataResponse) and appends a compact "Sources" chip row.
    function _installSourcesFooter(messages) {
        function scan(node) {
            if (!(node instanceof HTMLElement)) return;
            var bubbles = node.matches && node.matches('.chat-message.assistant-message .msg-bubble')
                ? [node]
                : node.querySelectorAll ? node.querySelectorAll('.chat-message.assistant-message .msg-bubble') : [];
            bubbles.forEach(enhance);
        }
        function enhance(bubble) {
            if (!bubble || bubble.dataset.srcEnh === '1') return;
            var queryEl = bubble.querySelector('.dr-code-editor, .dr-query-body code, pre code.sql, pre code.language-sql');
            var query = queryEl ? (queryEl.value || queryEl.textContent || '').trim() : '';
            // Also look for a dataset/table hint rendered by chat.js.
            var dsHint = bubble.querySelector('[data-dataset], [data-table]');
            var dsName = dsHint ? (dsHint.getAttribute('data-dataset') || dsHint.getAttribute('data-table')) : '';
            if (!query && !dsName) return;
            bubble.dataset.srcEnh = '1';

            // Guess tables from the query (best-effort).
            var tables = [];
            if (query) {
                var m, re = /\b(?:from|join)\s+([\[\]"`\w.]+)/gi;
                while ((m = re.exec(query)) !== null) {
                    var t = m[1].replace(/[\[\]"`]/g, '');
                    if (t && tables.indexOf(t) === -1) tables.push(t);
                }
            }
            if (dsName && tables.indexOf(dsName) === -1) tables.unshift(dsName);
            if (tables.length === 0) return;

            var wrap = document.createElement('div');
            wrap.className = 'chat-sources';
            wrap.innerHTML =
                '<span class="chat-sources-label"><i class="bi bi-link-45deg"></i> Sources</span>' +
                tables.slice(0, 4).map(function (t) {
                    return '<span class="chat-sources-chip" title="' +
                        t.replace(/"/g, '&quot;') + '"><i class="bi bi-table"></i>' +
                        t.replace(/</g, '&lt;') + '</span>';
                }).join('');
            if (query) {
                var view = document.createElement('button');
                view.type = 'button';
                view.className = 'chat-sources-view';
                view.innerHTML = '<i class="bi bi-code-slash"></i> View query';
                view.addEventListener('click', function () {
                    _showSourceModal(query, tables);
                });
                wrap.appendChild(view);
            }
            bubble.appendChild(wrap);
        }

        new MutationObserver(function (muts) {
            muts.forEach(function (m) {
                m.addedNodes && m.addedNodes.forEach(scan);
                // Also re-scan on subtree mutations because chat.js re-renders
                // the bubble asynchronously after SSE completes.
                if (m.target && m.target.classList && m.target.classList.contains('msg-bubble')) {
                    enhance(m.target);
                }
            });
        }).observe(messages, { childList: true, subtree: true });
        scan(messages);
    }

    function _showSourceModal(query, tables) {
        var existing = document.getElementById('chatSourceModal');
        if (existing) existing.remove();
        var ov = document.createElement('div');
        ov.id = 'chatSourceModal';
        ov.className = 'chat-shortcut-overlay';
        var esc = function (s) { return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); };
        ov.innerHTML =
            '<div class="chat-shortcut-modal" style="width:640px">' +
                '<div class="chat-shortcut-head"><i class="bi bi-code-slash me-2"></i>Query Source</div>' +
                '<div class="chat-shortcut-body">' +
                    '<div style="font-size:0.75rem;margin-bottom:8px;color:var(--cp-muted,#8290A3)">Tables: ' +
                        tables.map(esc).join(', ') + '</div>' +
                    '<pre style="background:var(--cp-bg-alt,#f8fafc);border:1px solid var(--cp-border,#e2e8f0);border-radius:6px;padding:10px;font-size:0.78rem;max-height:320px;overflow:auto;white-space:pre-wrap">' +
                        esc(query) + '</pre>' +
                '</div>' +
                '<div class="chat-shortcut-foot" style="display:flex;gap:8px;justify-content:flex-end">' +
                    '<button class="btn btn-sm btn-outline-secondary" id="chatSourceCopy"><i class="bi bi-clipboard me-1"></i>Copy</button>' +
                    '<button class="btn btn-sm btn-primary" id="chatSourceClose">Close</button>' +
                '</div>' +
            '</div>';
        document.body.appendChild(ov);
        ov.addEventListener('click', function (e) { if (e.target === ov) ov.remove(); });
        document.getElementById('chatSourceClose').addEventListener('click', function () { ov.remove(); });
        document.getElementById('chatSourceCopy').addEventListener('click', function () {
            if (navigator.clipboard) navigator.clipboard.writeText(query).then(function () { _toast('Query copied', 'success'); });
        });
    }

    // ── Item 2 — Dynamic suggestion chips from schema ───────────────
    // Watches #schemaExplorer for column names and replaces the canned
    // welcome chips with data-aware suggestions.
    function _installDynamicChips() {
        var schemaEl = document.getElementById('schemaExplorer');
        var chipsBox = document.getElementById('welcomeSuggestions');
        if (!schemaEl || !chipsBox) return;

        var applied = false;
        function pickColumns() {
            // Heuristic: grab column labels rendered by chat.js — look for
            // typical selectors the schema tree uses (column rows, leaf items).
            var nodes = schemaEl.querySelectorAll('[data-col], [data-column], .schema-col, .schema-column, .tree-col, .tree-column, li.col');
            var names = [];
            nodes.forEach(function (n) {
                var name = n.getAttribute('data-col') || n.getAttribute('data-column') ||
                    (n.textContent || '').trim();
                if (name && name.length < 40 && names.indexOf(name) === -1) names.push(name);
            });
            return names.slice(0, 8);
        }

        function generateSuggestions(cols) {
            if (!cols.length) return null;
            // Classify columns naively by name hints.
            var metrics = cols.filter(function (c) { return /amount|total|revenue|sales|cost|price|count|qty|quantity|value/i.test(c); });
            var dates   = cols.filter(function (c) { return /date|time|year|month|day|created|updated/i.test(c); });
            var dims    = cols.filter(function (c) { return /region|country|city|category|product|customer|department|segment|channel|name|type|status/i.test(c); });

            var out = [];
            if (metrics[0] && dims[0]) out.push('Top 10 ' + dims[0] + ' by ' + metrics[0]);
            if (metrics[0] && dates[0]) out.push(metrics[0] + ' trend over ' + dates[0]);
            if (metrics.length >= 2) out.push('Compare ' + metrics[0] + ' vs ' + metrics[1]);
            if (dims[0] && metrics[0]) out.push('Share of ' + metrics[0] + ' by ' + dims[0]);
            if (metrics[0]) out.push('Find anomalies in ' + metrics[0]);
            return out.slice(0, 4);
        }

        function apply() {
            if (applied) return;
            var cols = pickColumns();
            var suggestions = generateSuggestions(cols);
            if (!suggestions || suggestions.length === 0) return;
            applied = true;
            chipsBox.innerHTML = suggestions.map(function (s) {
                return '<button class="suggestion-chip" onclick="sendSuggestion(this)">' +
                    s.replace(/</g, '&lt;').replace(/>/g, '&gt;') + '</button>';
            }).join('');
        }

        new MutationObserver(apply).observe(schemaEl, { childList: true, subtree: true });
        apply();
    }

    // ── Item 3 — Datasource pill in composer ────────────────────────
    function _installDatasourcePill() {
        var footer = document.querySelector('.chat-input-footer');
        if (!footer || footer.querySelector('.chat-ds-pill')) return;
        var pill = document.createElement('span');
        pill.className = 'chat-ds-pill';
        pill.id = 'chatDsPill';
        pill.hidden = true;
        pill.innerHTML = '<i class="bi bi-database-fill me-1"></i><span class="chat-ds-pill-name">—</span>';
        footer.insertBefore(pill, footer.firstChild);

        function sync() {
            var nameEl = document.getElementById('welcomeDsName');
            var typeEl = document.getElementById('welcomeDsType');
            var badge  = document.getElementById('welcomeDsBadge');
            var visible = badge && badge.style.display !== 'none';
            var name = (nameEl && nameEl.textContent || '').trim();
            if (visible && name) {
                pill.querySelector('.chat-ds-pill-name').textContent = name;
                pill.title = 'Connected datasource: ' + name + (typeEl ? ' ' + typeEl.textContent : '');
                pill.hidden = false;
            } else {
                pill.hidden = true;
            }
        }
        var target = document.getElementById('welcomeDsBadge');
        if (target) new MutationObserver(sync).observe(target, { attributes: true, childList: true, subtree: true, attributeFilter: ['style'] });
        sync();
    }

    // ── Item 4 — Floating stop button over last message while streaming
    function _installFloatingStop() {
        var stop = document.getElementById('chatStopBtn');
        var messages = document.getElementById('chatMessages');
        if (!stop || !messages) return;
        function sync() {
            var streaming = stop.offsetParent !== null && stop.style.display !== 'none';
            document.body.classList.toggle('chat-streaming', streaming);
        }
        new MutationObserver(sync).observe(stop, { attributes: true, attributeFilter: ['style', 'class'] });
        sync();
    }

    // ── Item 13 — Code block copy/run buttons ───────────────────────
    function _installCodeBlockActions(messages) {
        function enhance(pre) {
            if (pre.dataset.cbEnh === '1') return;
            pre.dataset.cbEnh = '1';
            var code = pre.querySelector('code');
            var text = (code ? code.textContent : pre.textContent) || '';
            if (!text.trim()) return;
            var bar = document.createElement('div');
            bar.className = 'chat-code-actions';
            var copy = document.createElement('button');
            copy.type = 'button';
            copy.title = 'Copy code';
            copy.innerHTML = '<i class="bi bi-clipboard"></i>';
            copy.addEventListener('click', function () {
                if (navigator.clipboard) navigator.clipboard.writeText(text).then(function () {
                    copy.innerHTML = '<i class="bi bi-check2"></i>';
                    setTimeout(function () { copy.innerHTML = '<i class="bi bi-clipboard"></i>'; }, 1200);
                    _toast('Code copied', 'success');
                });
            });
            bar.appendChild(copy);
            // Run button only for SQL-ish blocks.
            var lang = (code && code.className || '').toLowerCase();
            var looksSql = /sql|language-sql/.test(lang) || /\bselect\b/i.test(text);
            if (looksSql) {
                var run = document.createElement('button');
                run.type = 'button';
                run.title = 'Send this query to the assistant';
                run.innerHTML = '<i class="bi bi-play-fill"></i>';
                run.addEventListener('click', function () {
                    var input = document.getElementById('chatInput');
                    var send  = document.getElementById('chatSendBtn');
                    if (input && send) {
                        input.value = 'Run this query:\n```sql\n' + text.trim() + '\n```';
                        input.dispatchEvent(new Event('input', { bubbles: true }));
                        send.click();
                    }
                });
                bar.appendChild(run);
            }
            pre.style.position = pre.style.position || 'relative';
            pre.appendChild(bar);
        }
        function scan(root) {
            if (!(root instanceof HTMLElement)) return;
            (root.matches && root.matches('pre') ? [root] : []).forEach(enhance);
            root.querySelectorAll && root.querySelectorAll('pre').forEach(enhance);
        }
        new MutationObserver(function (muts) {
            muts.forEach(function (m) { m.addedNodes && m.addedNodes.forEach(scan); });
        }).observe(messages, { childList: true, subtree: true });
        scan(messages);
    }

    // ── Item 26 — Horizontal chip scroll on narrow screens ──────────
    function _installChipsScroll() {
        var box = document.getElementById('welcomeSuggestions');
        if (!box) return;
        box.classList.add('chips-scrollable');
    }

    // ── Item 27 — Cohere panel open/closed persisted ────────────────
    function _installCoherePanelPersistence() {
        var toggle = document.getElementById('coherePanelToggle');
        var detail = document.getElementById('coherePanelDetail');
        var icon   = document.getElementById('cohereToggleIcon');
        if (!toggle || !detail) return;
        var KEY = 'cp_cohere_panel_open';
        var saved = localStorage.getItem(KEY);
        if (saved === '1') {
            detail.classList.add('open');
            if (icon) { icon.classList.remove('bi-chevron-up'); icon.classList.add('bi-chevron-down'); }
        }
        // Observe class changes on detail to persist new state.
        new MutationObserver(function () {
            try { localStorage.setItem(KEY, detail.classList.contains('open') ? '1' : '0'); } catch (e) {}
        }).observe(detail, { attributes: true, attributeFilter: ['class'] });
    }

    // ── Item 24 — Undo toast after /clear ───────────────────────────
    var _undoSnapshot = null;
    function _installClearUndoToast() {
        var messages = document.getElementById('chatMessages');
        if (!messages) return;
        document.addEventListener('keydown', function (e) {
            if ((e.ctrlKey || e.metaKey) && (e.key === 'z' || e.key === 'Z') && _undoSnapshot) {
                e.preventDefault();
                messages.innerHTML = _undoSnapshot;
                _undoSnapshot = null;
                _toast('Conversation restored', 'success');
            }
        });
    }
    // Public hook (used by /clear slash command if invoked).
    window.chatUx = window.chatUx || {};
    window.chatUx.snapshotBeforeClear = function () {
        var messages = document.getElementById('chatMessages');
        if (messages) _undoSnapshot = messages.innerHTML;
        _showUndoToast();
    };
    function _showUndoToast() {
        var existing = document.getElementById('chatUndoToast');
        if (existing) existing.remove();
        var t = document.createElement('div');
        t.id = 'chatUndoToast';
        t.className = 'chat-undo-toast';
        t.innerHTML = '<span><i class="bi bi-check2-circle me-1"></i>Conversation cleared</span><button type="button" class="chat-undo-btn">Undo</button>';
        document.body.appendChild(t);
        requestAnimationFrame(function () { t.classList.add('show'); });
        var remove = function () { t.classList.remove('show'); setTimeout(function () { t.remove(); }, 200); };
        t.querySelector('.chat-undo-btn').addEventListener('click', function () {
            var messages = document.getElementById('chatMessages');
            if (messages && _undoSnapshot) {
                messages.innerHTML = _undoSnapshot;
                _undoSnapshot = null;
                _toast('Conversation restored', 'success');
            }
            remove();
        });
        setTimeout(remove, 6000);
    }

    // ── Item 23 — Session cost meter ────────────────────────────────
    function _installCostMeter() {
        var footer = document.querySelector('.chat-input-footer');
        var tokenNum = document.getElementById('cohereTokenNum');
        if (!footer || !tokenNum || footer.querySelector('.chat-cost-meter')) return;
        var meter = document.createElement('span');
        meter.className = 'chat-cost-meter';
        meter.title = 'Session tokens (estimated)';
        meter.innerHTML = '<i class="bi bi-coin me-1"></i><span class="chat-cost-val">0</span> tok';
        // Insert before model badge if present.
        var modelBadge = footer.querySelector('.model-badge');
        if (modelBadge) footer.insertBefore(meter, modelBadge);
        else footer.appendChild(meter);

        var cumulative = 0;
        var lastSeen = 0;
        new MutationObserver(function () {
            var v = parseInt(tokenNum.textContent || '0', 10) || 0;
            if (v < lastSeen) cumulative += lastSeen; // new request started; lock in previous
            lastSeen = v;
            var total = cumulative + v;
            meter.querySelector('.chat-cost-val').textContent = total > 999 ? (total / 1000).toFixed(1) + 'k' : total;
        }).observe(tokenNum, { childList: true, characterData: true, subtree: true });
    }

    // ── Item 6 — Auto-grow textarea + char counter ──────────────────
    function _installAutoGrowAndCounter(input) {
        var counter = document.getElementById('chatCharCounter');
        var MAX = 2000;
        function autoGrow() {
            input.style.height = 'auto';
            var h = Math.min(160, Math.max(38, input.scrollHeight));
            input.style.height = h + 'px';
        }
        function updateCounter() {
            if (!counter) return;
            var n = input.value.length;
            counter.textContent = n > 0 ? (n + ' / ' + MAX) : '';
            counter.classList.toggle('is-near', n > MAX * 0.85);
            counter.classList.toggle('is-over', n > MAX);
        }
        input.addEventListener('input', function () { autoGrow(); updateCounter(); });
        input.addEventListener('focus', autoGrow);
        // Initial
        setTimeout(autoGrow, 50);
        updateCounter();
    }

    // ── Item 7 — Slash command menu ─────────────────────────────────
    function _installSlashMenu(input) {
        var menu = document.getElementById('chatSlashMenu');
        if (!menu) return;
        var selected = 0;
        var items = [];

        function isOpen() { return !menu.hasAttribute('hidden'); }
        function close() { menu.setAttribute('hidden', ''); items = []; }
        function open(filter) {
            var f = (filter || '').toLowerCase();
            var matches = SLASH_COMMANDS.filter(function (c) {
                return c.key.toLowerCase().indexOf(f) === 0 || c.label.toLowerCase().indexOf(f) >= 0;
            });
            if (matches.length === 0) { close(); return; }
            selected = 0;
            menu.innerHTML = matches.map(function (c, i) {
                return '<div class="chat-slash-item" role="option" data-key="' + c.key +
                    '" aria-selected="' + (i === 0 ? 'true' : 'false') + '">' +
                    '<i class="bi ' + c.icon + '"></i>' +
                    '<span class="chat-slash-key">' + c.key + '</span>' +
                    '<span class="chat-slash-label">' + c.label + '</span>' +
                    '</div>';
            }).join('');
            items = Array.prototype.slice.call(menu.querySelectorAll('.chat-slash-item'));
            items.forEach(function (el, i) {
                el.addEventListener('mouseenter', function () { highlight(i); });
                el.addEventListener('mousedown', function (e) { e.preventDefault(); pick(i); });
            });
            menu.removeAttribute('hidden');
        }
        function highlight(i) {
            selected = i;
            items.forEach(function (el, idx) { el.setAttribute('aria-selected', idx === i ? 'true' : 'false'); });
        }
        function pick(i) {
            var el = items[i]; if (!el) return;
            var key = el.dataset.key;
            // Special: /clear wipes the conversation with undo snapshot.
            if (key === '/clear') {
                var messages = document.getElementById('chatMessages');
                if (messages && window.chatUx && window.chatUx.snapshotBeforeClear) {
                    window.chatUx.snapshotBeforeClear();
                    // Preserve welcome if present.
                    var welcome = document.getElementById('chatWelcome');
                    messages.innerHTML = '';
                    if (welcome) messages.appendChild(welcome);
                }
                input.value = '';
                close();
                input.focus();
                input.dispatchEvent(new Event('input', { bubbles: true }));
                return;
            }
            // Replace slash-token at start of input with the picked key + space.
            var v = input.value;
            var m = v.match(/^\/\S*/);
            input.value = (m ? v.replace(m[0], key) : key) + ' ';
            close();
            input.focus();
            input.dispatchEvent(new Event('input', { bubbles: true }));
        }

        input.addEventListener('input', function () {
            var v = input.value;
            var m = v.match(/^\/(\S*)$/);
            if (m) open('/' + m[1]);
            else close();
        });
        input.addEventListener('keydown', function (e) {
            if (!isOpen()) return;
            if (e.key === 'ArrowDown') { e.preventDefault(); highlight((selected + 1) % items.length); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); highlight((selected - 1 + items.length) % items.length); }
            else if (e.key === 'Enter' || e.key === 'Tab') { e.preventDefault(); pick(selected); }
            else if (e.key === 'Escape') { e.preventDefault(); close(); }
        });
        input.addEventListener('blur', function () { setTimeout(close, 120); });
    }

    // ── Item 17 — Keyboard shortcuts ────────────────────────────────
    // Ctrl/Cmd+K → focus input; Ctrl+/ → show shortcut help; Esc → stop/blur; ↑ on empty → recall last user message.
    function _installKeyboardShortcuts(input) {
        var lastUserMsg = '';
        var messages = document.getElementById('chatMessages');

        document.addEventListener('keydown', function (e) {
            var mod = e.ctrlKey || e.metaKey;
            if (mod && (e.key === 'k' || e.key === 'K')) {
                e.preventDefault();
                input.focus();
                input.select && input.select();
            } else if (mod && e.key === '/') {
                e.preventDefault();
                _showShortcutHelp();
            } else if (e.key === 'Escape') {
                var stop = document.getElementById('chatStopBtn');
                if (stop && stop.offsetParent !== null) { stop.click(); return; }
                if (document.activeElement === input) input.blur();
            }
        });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowUp' && input.value === '' && lastUserMsg) {
                e.preventDefault();
                input.value = lastUserMsg;
                input.dispatchEvent(new Event('input', { bubbles: true }));
            }
        });
        // Observe messages to capture last user text (for ↑ recall).
        if (messages) {
            new MutationObserver(function () {
                var userBubbles = messages.querySelectorAll('.chat-message.user-message .msg-bubble');
                if (userBubbles.length > 0) lastUserMsg = userBubbles[userBubbles.length - 1].textContent.trim();
            }).observe(messages, { childList: true, subtree: true });
        }
    }

    function _showShortcutHelp() {
        if (document.getElementById('chatShortcutHelp')) { _closeShortcutHelp(); return; }
        var ov = document.createElement('div');
        ov.id = 'chatShortcutHelp';
        ov.className = 'chat-shortcut-overlay';
        ov.innerHTML =
            '<div class="chat-shortcut-modal" role="dialog" aria-label="Keyboard shortcuts">' +
                '<div class="chat-shortcut-head"><i class="bi bi-keyboard me-2"></i>Keyboard Shortcuts</div>' +
                '<div class="chat-shortcut-body">' +
                    _shortcutRow('Ctrl / ⌘ + K', 'Focus message input') +
                    _shortcutRow('Ctrl + /', 'Show this help') +
                    _shortcutRow('Enter', 'Send message') +
                    _shortcutRow('Shift + Enter', 'New line') +
                    _shortcutRow('↑', 'Recall last message (when input is empty)') +
                    _shortcutRow('Esc', 'Stop generating / close') +
                    _shortcutRow('/', 'Open slash command menu') +
                '</div>' +
                '<div class="chat-shortcut-foot"><button class="btn btn-sm btn-primary" id="chatShortcutClose">Close</button></div>' +
            '</div>';
        document.body.appendChild(ov);
        ov.addEventListener('click', function (e) { if (e.target === ov) _closeShortcutHelp(); });
        document.getElementById('chatShortcutClose').addEventListener('click', _closeShortcutHelp);
        document.addEventListener('keydown', _escCloseShortcutHelp);
    }
    function _shortcutRow(k, d) {
        return '<div class="chat-shortcut-row"><kbd>' + k + '</kbd><span>' + d + '</span></div>';
    }
    function _closeShortcutHelp() {
        var el = document.getElementById('chatShortcutHelp');
        if (el) el.remove();
        document.removeEventListener('keydown', _escCloseShortcutHelp);
    }
    function _escCloseShortcutHelp(e) { if (e.key === 'Escape') _closeShortcutHelp(); }

    // ── Item 16 — Jump-to-latest pill ───────────────────────────────
    function _installJumpToLatest(messages) {
        var pill = document.getElementById('chatJumpLatest');
        if (!pill) return;
        function isNearBottom() {
            return messages.scrollHeight - messages.scrollTop - messages.clientHeight < 120;
        }
        function update() {
            if (isNearBottom()) pill.setAttribute('hidden', '');
            else pill.removeAttribute('hidden');
        }
        messages.addEventListener('scroll', update, { passive: true });
        pill.addEventListener('click', function () {
            messages.scrollTo({ top: messages.scrollHeight, behavior: 'smooth' });
        });
        new MutationObserver(update).observe(messages, { childList: true, subtree: true });
        update();
    }

    // ── Items 5, 12, 15 — Message enrichment ────────────────────────
    function _installMessageObserver(messages) {
        var lastSender = null;
        var lastDateKey = null;

        function enrich(node) {
            if (!(node instanceof HTMLElement)) return;
            if (!node.classList || !node.classList.contains('chat-message')) return;
            if (node.dataset.uxEnriched === '1') return;
            node.dataset.uxEnriched = '1';

            // Item 15 — date separator (session-scoped; all "today" by default).
            var now = new Date();
            var dateKey = now.toDateString();
            if (dateKey !== lastDateKey) {
                lastDateKey = dateKey;
                var sep = document.createElement('div');
                sep.className = 'chat-date-sep';
                sep.textContent = _formatDateLabel(now);
                messages.insertBefore(sep, node);
            }

            // Item 12 — group consecutive same-sender bubbles.
            var sender = node.classList.contains('user-message') ? 'user'
                : node.classList.contains('assistant-message') ? 'assistant'
                : 'other';
            if (sender === lastSender) node.classList.add('same-sender');
            lastSender = sender;

            // Item 5 — hover actions (copy, regenerate hint for assistant).
            var bubble = node.querySelector('.msg-bubble');
            if (bubble && !bubble.querySelector('.chat-msg-actions')) {
                var actions = document.createElement('div');
                actions.className = 'chat-msg-actions';
                var copyBtn = document.createElement('button');
                copyBtn.type = 'button';
                copyBtn.title = 'Copy message';
                copyBtn.innerHTML = '<i class="bi bi-clipboard"></i>';
                copyBtn.addEventListener('click', function (e) {
                    e.stopPropagation();
                    var text = bubble.innerText || bubble.textContent || '';
                    var flash = function () {
                        copyBtn.innerHTML = '<i class="bi bi-check2"></i>';
                        setTimeout(function () { copyBtn.innerHTML = '<i class="bi bi-clipboard"></i>'; }, 1200);
                        _toast('Copied to clipboard', 'success');
                    };
                    if (window.copyRichContent) {
                        window.copyRichContent(bubble, text, flash);
                    } else if (navigator.clipboard) {
                        navigator.clipboard.writeText(text).then(flash);
                    }
                });
                actions.appendChild(copyBtn);

                if (sender === 'assistant') {
                    var upBtn = document.createElement('button');
                    upBtn.type = 'button';
                    upBtn.title = 'Helpful';
                    upBtn.innerHTML = '<i class="bi bi-hand-thumbs-up"></i>';
                    upBtn.addEventListener('click', function (e) {
                        e.stopPropagation();
                        upBtn.innerHTML = '<i class="bi bi-hand-thumbs-up-fill"></i>';
                        _toast('Thanks for the feedback', 'success');
                    });
                    var downBtn = document.createElement('button');
                    downBtn.type = 'button';
                    downBtn.title = 'Not helpful';
                    downBtn.innerHTML = '<i class="bi bi-hand-thumbs-down"></i>';
                    downBtn.addEventListener('click', function (e) {
                        e.stopPropagation();
                        downBtn.innerHTML = '<i class="bi bi-hand-thumbs-down-fill"></i>';
                        _toast('Feedback noted', 'info');
                    });
                    actions.appendChild(upBtn);
                    actions.appendChild(downBtn);
                }
                bubble.style.position = bubble.style.position || 'relative';
                bubble.appendChild(actions);
            }
        }

        new MutationObserver(function (muts) {
            muts.forEach(function (m) {
                m.addedNodes && m.addedNodes.forEach(enrich);
            });
        }).observe(messages, { childList: true });
        // Enrich pre-existing nodes (e.g. from history restore).
        Array.prototype.slice.call(messages.children).forEach(enrich);
    }

    function _formatDateLabel(d) {
        var today = new Date(); today.setHours(0,0,0,0);
        var y = new Date(today); y.setDate(today.getDate() - 1);
        var dd = new Date(d); dd.setHours(0,0,0,0);
        if (dd.getTime() === today.getTime()) return 'Today';
        if (dd.getTime() === y.getTime())     return 'Yesterday';
        return d.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
    }

    // ── Item 11 — Cohere error retry button ─────────────────────────
    function _installCohereErrorRetry(sendBtn) {
        var statusText = document.getElementById('cohereStatusText');
        var panel      = document.getElementById('coherePanel');
        if (!statusText || !panel) return;

        function check() {
            var t = (statusText.textContent || '').toLowerCase();
            var isError = t.indexOf('error') >= 0 || t.indexOf('failed') >= 0 || t.indexOf('timeout') >= 0;
            panel.classList.toggle('cohere-error', isError);
            var existing = panel.querySelector('.cohere-retry-btn');
            if (isError && !existing) {
                var btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'cohere-retry-btn';
                btn.innerHTML = '<i class="bi bi-arrow-clockwise me-1"></i>Retry';
                btn.title = 'Retry the last request';
                btn.addEventListener('click', function (e) {
                    e.stopPropagation();
                    var input = document.getElementById('chatInput');
                    var messages = document.getElementById('chatMessages');
                    if (input && messages && sendBtn) {
                        var userBubbles = messages.querySelectorAll('.chat-message.user-message .msg-bubble');
                        if (userBubbles.length > 0) {
                            input.value = userBubbles[userBubbles.length - 1].textContent.trim();
                            input.dispatchEvent(new Event('input', { bubbles: true }));
                            sendBtn.click();
                        }
                    }
                });
                var right = panel.querySelector('.cohere-panel-right');
                if (right) right.insertBefore(btn, right.firstChild);
            } else if (!isError && existing) {
                existing.remove();
            }
        }

        new MutationObserver(check).observe(statusText, { childList: true, subtree: true, characterData: true });
        check();
    }

    // ── Boot ────────────────────────────────────────────────────────
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
