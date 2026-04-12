// ChatAgentContext — Dynamic suggestions, auto-schema panel, datasource-aware chat
(function () {
    'use strict';

    function _esc(str) {
        var d = document.createElement('div');
        d.appendChild(document.createTextNode(String(str || '')));
        return d.innerHTML;
    }

    // ── Ensure the welcome block exists inside #chatMessages ─────────
    // clearChatUI() in chat.js wipes innerHTML, so we must rebuild it.
    function ensureWelcome(detail) {
        var messages = document.getElementById('chatMessages');
        if (!messages) return null;

        var welcome = document.getElementById('chatWelcome');
        if (welcome) return welcome;

        var agentName = detail.agentName || 'AI data assistant';
        var subtitleText = detail.datasourceName
            ? 'Connected to ' + _esc(detail.datasourceName) + '. Ask me to query, analyze, or visualize your data.'
            : 'Ask me anything about your data. I can query your datasources, analyze trends, and create visualizations.';

        var html =
            '<div class="chat-welcome" id="chatWelcome">' +
                '<div class="welcome-icon"><i class="bi bi-robot"></i></div>' +
                '<h3>Hello! I\'m ' + _esc(agentName) + '</h3>' +
                '<p class="welcome-subtitle" id="welcomeSubtitle">' + subtitleText + '</p>' +
                '<div class="welcome-ds-badge" id="welcomeDsBadge" style="' +
                    (detail.datasourceName ? '' : 'display:none') + '">' +
                    '<i class="bi bi-database-fill me-1"></i>' +
                    '<span id="welcomeDsName">' + _esc(detail.datasourceName || '') + '</span>' +
                    ' <span class="welcome-ds-type" id="welcomeDsType">' + _esc(detail.datasourceType || '') + '</span>' +
                '</div>' +
                '<div class="welcome-suggestions" id="welcomeSuggestions"></div>' +
            '</div>';

        messages.insertAdjacentHTML('afterbegin', html);
        return document.getElementById('chatWelcome');
    }

    // ── Update welcome message with datasource context ───────────────
    function updateWelcome(detail) {
        ensureWelcome(detail);

        var badge = document.getElementById('welcomeDsBadge');
        var dsNameEl = document.getElementById('welcomeDsName');
        var dsTypeEl = document.getElementById('welcomeDsType');
        var subtitle = document.getElementById('welcomeSubtitle');

        if (detail.datasourceName && badge && dsNameEl) {
            dsNameEl.textContent = detail.datasourceName;
            if (dsTypeEl) dsTypeEl.textContent = detail.datasourceType || '';
            badge.style.display = '';
        }

        if (subtitle && detail.datasourceName) {
            subtitle.textContent = 'Connected to ' + detail.datasourceName +
                '. Ask me to query, analyze, or visualize your data.';
        }
    }

    // ── Load dynamic suggestion prompts from API ─────────────────────
    async function loadSuggestions(agentGuid) {
        var container = document.getElementById('welcomeSuggestions');
        if (!container) return;

        // Show default fallback chips immediately while fetching
        var fallbackChips = [
            { icon: 'bi-bar-chart', text: 'What are the top revenue regions?' },
            { icon: 'bi-graph-up', text: 'Show me sales trends this quarter' },
            { icon: 'bi-diagram-3', text: 'Compare performance by product' }
        ];
        renderChips(container, fallbackChips);

        try {
            var r = await fetch('/api/chat/suggestions?agentId=' + encodeURIComponent(agentGuid));
            if (!r.ok) return;
            var suggestions = await r.json();
            if (!suggestions || !suggestions.length) return;

            renderChips(container, suggestions);
        } catch (e) {
            // Keep fallback suggestions on error
        }
    }

    function renderChips(container, items) {
        container.innerHTML = '';
        container.classList.remove('cac-suggestions-loaded');
        container.classList.add('cac-suggestions-loading');

        items.forEach(function (s) {
            var btn = document.createElement('button');
            btn.className = 'suggestion-chip cac-suggestion-chip';
            btn.onclick = function () { if (window.sendSuggestion) window.sendSuggestion(btn); };
            var icon = s.icon || 'bi-chat-dots';
            btn.innerHTML = '<i class="bi ' + _esc(icon) + ' me-1"></i>' + _esc(s.text);
            container.appendChild(btn);
        });

        requestAnimationFrame(function () {
            container.classList.remove('cac-suggestions-loading');
            container.classList.add('cac-suggestions-loaded');
        });
    }

    // ── Auto-open schema panel on the right side ─────────────────────
    function autoOpenSchemaPanel() {
        setTimeout(function () {
            if (window.ChatThinkingPanel) {
                window.ChatThinkingPanel.open();
                window.ChatThinkingPanel.switchTab('schema');
            }
        }, 300);
    }

    // ── Stored context so we can rebuild after clearChatUI ───────────
    var _lastDetail = null;

    function rebuildWelcome() {
        var detail = _lastDetail || _detailFromGlobals();
        if (!detail) return;
        updateWelcome(detail);
        if (detail.agentGuid) loadSuggestions(detail.agentGuid);
    }

    function _detailFromGlobals() {
        var ag = window.currentAgentGuid;
        if (!ag) return null;
        return {
            wsGuid: window.currentWorkspaceGuid || '',
            agentGuid: ag,
            agentName: '',
            datasourceId: window.currentDatasourceId || null,
            datasourceName: window.currentDatasourceName || null,
            datasourceType: window.currentDatasourceType || null
        };
    }

    // ── Listen for chatAgentReady event from workspaceFlow.js ────────
    document.addEventListener('chatAgentReady', function (e) {
        var detail = e.detail || {};
        _lastDetail = detail;

        // 1. Rebuild & update welcome message with datasource info
        updateWelcome(detail);

        // 2. Load dynamic suggestions based on agent's datasource
        if (detail.agentGuid) {
            loadSuggestions(detail.agentGuid);
        }

        // 3. Auto-open schema panel if datasource is connected
        if (detail.datasourceId) {
            autoOpenSchemaPanel();
        }
    });

    window.ChatAgentContext = {
        updateWelcome: updateWelcome,
        loadSuggestions: loadSuggestions,
        autoOpenSchemaPanel: autoOpenSchemaPanel,
        rebuildWelcome: rebuildWelcome
    };
})();
