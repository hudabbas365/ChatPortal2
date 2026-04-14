// ChatPortal2 - Chat Workspace JS
(function() {
    'use strict';

    const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
    let currentWorkspaceId = null;
    let currentChatId = null;
    let isStreaming = false;

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
    }

    // ── Browser Chat History (localStorage) ────────────────────────
    const ChatHistory = (function() {
        function _key(wsId) {
            const agentId = window.currentAgentGuid || '';
            return agentId ? `cp_chats_${wsId}_${agentId}` : `cp_chats_${wsId}`;
        }

        function getAll(wsId) {
            try { return JSON.parse(localStorage.getItem(_key(wsId)) || '[]'); }
            catch { return []; }
        }

        function _save(wsId, chats) {
            try { localStorage.setItem(_key(wsId), JSON.stringify(chats)); }
            catch {} // quota exceeded — silently skip
        }

        function createChat(wsId) {
            const id = 'chat-' + Date.now();
            const chats = getAll(wsId);
            chats.unshift({ id, title: 'New Chat', createdAt: new Date().toISOString(), messages: [] });
            _save(wsId, chats);
            return id;
        }

        function appendMessage(wsId, chatId, role, content, dataObj) {
            const chats = getAll(wsId);
            const chat = chats.find(c => c.id === chatId);
            if (!chat) return;
            chat.messages.push({ role, content, dataObj: dataObj || null });
            if (role === 'user' && chat.title === 'New Chat') {
                chat.title = content.substring(0, 45) + (content.length > 45 ? '…' : '');
            }
            _save(wsId, chats);
        }

        function deleteChat(wsId, chatId) {
            _save(wsId, getAll(wsId).filter(c => c.id !== chatId));
        }

        function getChat(wsId, chatId) {
            return getAll(wsId).find(c => c.id === chatId) || null;
        }

        return { getAll, createChat, appendMessage, deleteChat, getChat };
    })();

    // ── Thinking Panel ┬─────────────────────────────────────────────
    const ThinkingPanel = (function() {
        let _activeTab = 'context';

        function panel() { return document.getElementById('thinkingPanel'); }
        function body()  { return document.getElementById('thinkingPanelBody'); }

        function open() {
            const p = panel();
            if (p) p.classList.add('tp-open');
        }
        function close() {
            const p = panel();
            if (p) p.classList.remove('tp-open');
        }

        function switchTab(tab) {
            _activeTab = tab;
            document.querySelectorAll('.tp-tab').forEach(t => {
                t.classList.toggle('tp-tab-active', t.dataset.tpTab === tab);
            });
            const contextBody = document.getElementById('thinkingPanelBody');
            const historyBody = document.getElementById('thinkingHistoryBody');
            const schemaBody  = document.getElementById('thinkingSchemaBody');
            if (contextBody) contextBody.style.display = tab === 'context' ? '' : 'none';
            if (historyBody) historyBody.style.display = tab === 'history' ? '' : 'none';
            if (schemaBody)  schemaBody.style.display  = tab === 'schema'  ? '' : 'none';
            if (tab === 'history') renderChatList(currentWorkspaceId);
        }

        function showThinking() {
            switchTab('context');
            open();
            const b = body();
            if (!b) return;
            b.innerHTML = `
                <div class="tp-steps" id="tpSteps">
                    <div class="tp-step tp-step-active" id="tpStep1">
                        <div class="tp-step-icon"><i class="bi bi-lightbulb"></i></div>
                        <div class="tp-step-info">
                            <div class="tp-step-label">Analyzing Intent</div>
                            <div class="tp-step-status"><span class="tp-spinner"></span> Processing…</div>
                        </div>
                    </div>
                    <div class="tp-step" id="tpStep2">
                        <div class="tp-step-icon"><i class="bi bi-code-slash"></i></div>
                        <div class="tp-step-info">
                            <div class="tp-step-label">Generating Query</div>
                            <div class="tp-step-status tp-status-muted">Waiting…</div>
                        </div>
                    </div>
                    <div class="tp-step" id="tpStep3">
                        <div class="tp-step-icon"><i class="bi bi-bar-chart-line"></i></div>
                        <div class="tp-step-info">
                            <div class="tp-step-label">Formatting Response</div>
                            <div class="tp-step-status tp-status-muted">Waiting…</div>
                        </div>
                    </div>
                </div>`;
        }

        function advanceStep(n) {
            for (let i = 1; i <= 3; i++) {
                const el = document.getElementById(`tpStep${i}`);
                if (!el) continue;
                const status = el.querySelector('.tp-step-status');
                el.classList.remove('tp-step-active', 'tp-step-done');
                if (i < n) {
                    el.classList.add('tp-step-done');
                    if (status) status.innerHTML = '<i class="bi bi-check-circle-fill me-1"></i>Done';
                } else if (i === n) {
                    el.classList.add('tp-step-active');
                    if (status) status.innerHTML = '<span class="tp-spinner"></span> Processing…';
                } else {
                    if (status) status.innerHTML = '<span class="tp-status-muted">Waiting…</span>';
                }
            }
        }

        function allDone() {
            for (let i = 1; i <= 3; i++) {
                const el = document.getElementById(`tpStep${i}`);
                if (!el) continue;
                el.classList.remove('tp-step-active');
                el.classList.add('tp-step-done');
                const status = el.querySelector('.tp-step-status');
                if (status) status.innerHTML = '<i class="bi bi-check-circle-fill me-1"></i>Done';
            }
        }

        function showDetails(jsonObj) {
            open();
            const b = body();
            if (!b) return;
            const prompt = escapeHtml(jsonObj.prompt || '');
            const query  = escapeHtml(jsonObj.query  || '');
            const desc   = escapeHtml(jsonObj.description || '');
            b.innerHTML = `
                <div class="tp-details">
                    <div class="tp-detail-section">
                        <div class="tp-detail-label"><i class="bi bi-chat-left-quote me-1"></i>Intent</div>
                        <div class="tp-detail-value">${prompt}</div>
                    </div>
                    ${query ? `
                    <div class="tp-detail-section">
                        <div class="tp-detail-label-row">
                            <span class="tp-detail-label"><i class="bi bi-code-slash me-1"></i>Generated Query</span>
                            <span class="tp-readonly-badge"><i class="bi bi-shield-lock-fill me-1"></i>Read-only</span>
                        </div>
                        <pre class="tp-detail-code">${query}</pre>
                    </div>` : ''}
                    <div class="tp-detail-section">
                        <div class="tp-detail-label"><i class="bi bi-info-circle me-1"></i>Description</div>
                        <div class="tp-detail-value">${desc}</div>
                    </div>
                </div>`;
        }

        function showError(details) {
            switchTab('context');
            open();
            const b = body();
            if (!b) return;
            const status = details.httpStatus ? `HTTP ${details.httpStatus}` : 'Network Error';
            const reason = escapeHtml(details.errorText || 'Unknown error');
            const msg    = escapeHtml(details.message   || '—');
            const wsId   = details.workspaceId || '—';
            const uid    = details.userId
                ? details.userId.toString().substring(0, 8) + '…'
                : '—';
            const time   = escapeHtml(details.timestamp || new Date().toLocaleTimeString());
            b.innerHTML = `
                <div class="tp-error-card">
                    <div class="tp-error-header">
                        <span><i class="bi bi-exclamation-triangle-fill me-1"></i>Request Failed</span>
                        <span class="tp-error-time">${time}</span>
                    </div>
                    <div class="tp-error-body">
                        <div class="tp-error-row">
                            <span class="tp-error-key">Status</span>
                            <span class="tp-error-val tp-error-status">${escapeHtml(status)}</span>
                        </div>
                        <div class="tp-error-row">
                            <span class="tp-error-key">Reason</span>
                            <span class="tp-error-val">${reason}</span>
                        </div>
                        <div class="tp-error-row">
                            <span class="tp-error-key">Message</span>
                            <span class="tp-error-val tp-error-truncate" title="${escapeHtml(details.message || '')}">${msg}</span>
                        </div>
                        <div class="tp-error-row">
                            <span class="tp-error-key">Workspace</span>
                            <span class="tp-error-val">${wsId}</span>
                        </div>
                        <div class="tp-error-row">
                            <span class="tp-error-key">User</span>
                            <span class="tp-error-val">${uid}</span>
                        </div>
                    </div>
                </div>`;
        }

        return { open, close, showThinking, advanceStep, allDone, showDetails, showError, switchTab };
    })();

    // ── Cohere AI Progress Panel ────────────────────────────────────
    const CohereProgress = (function() {
        let _chunks = 0, _startTime = 0, _open = false;

        const el = id => document.getElementById(id);

        function setStatus(text, active) {
            const dot  = el('cohereStatusDot');
            const txt  = el('cohereStatusText');
            if (txt) txt.innerHTML = `<i class="bi bi-cpu me-1"></i>${text}`;
            if (dot) {
                dot.className = 'cohere-dot' + (active ? ' cohere-dot-active' : '');
            }
        }

        function start() {
            _chunks = 0;
            _startTime = Date.now();
            setStatus('Cohere AI &middot; Connecting…', true);
            const badge = el('cohereTokenBadge');
            if (badge) badge.style.display = 'none';
            _set('cohereDetailStatus', 'Connecting…');
            _set('cohereChunkCount', '0');
            _set('cohereTokenCount', '0');
            _set('cohereLatency', '—');
            const log = el('cohereStreamLog');
            if (log) log.innerHTML = '';
        }

        function onChunk(text) {
            _chunks++;
            const est = Math.round(_chunks * 1.4);
            const badge = el('cohereTokenBadge');
            if (badge) { badge.style.display = ''; el('cohereTokenNum').textContent = est; }
            _set('cohereDetailStatus', 'Streaming…');
            _set('cohereChunkCount', String(_chunks));
            _set('cohereTokenCount', String(est));
            _set('cohereLatency', ((Date.now() - _startTime) / 1000).toFixed(1) + 's');
            setStatus('Cohere AI &middot; Streaming…', true);
            // Show last ~80 chars of streamed text in the log
            const log = el('cohereStreamLog');
            if (log && text) {
                const preview = text.length > 80 ? '…' + text.slice(-80) : text;
                log.textContent = preview;
            }
        }

        function done(parsed) {
            const latency = ((Date.now() - _startTime) / 1000).toFixed(1) + 's';
            setStatus('Cohere AI &middot; Complete', false);
            _set('cohereDetailStatus', parsed ? 'Structured response parsed ✓' : 'Plain text response');
            _set('cohereLatency', latency);
            const log = el('cohereStreamLog');
            if (log) log.textContent = '';
        }

        function error() {
            setStatus('Cohere AI &middot; Error', false);
            _set('cohereDetailStatus', 'Request failed');
            const dot = el('cohereStatusDot');
            if (dot) dot.className = 'cohere-dot cohere-dot-error';
        }

        function _set(id, val) {
            const e = el(id);
            if (e) e.textContent = val;
        }

        // Toggle expand/collapse
        function wireToggle() {
            const btn = el('coherePanelToggle');
            const detail = el('coherePanelDetail');
            const icon = el('cohereToggleIcon');
            if (!btn || !detail) return;
            btn.addEventListener('click', function() {
                _open = !_open;
                detail.classList.toggle('cohere-detail-open', _open);
                if (icon) icon.className = _open ? 'bi bi-chevron-down' : 'bi bi-chevron-up';
                btn.title = _open ? 'Collapse Cohere details' : 'Expand Cohere details';
            });
        }

        return { start, onChunk, done, error, wireToggle };
    })();
    function validateQuery(sql) {
        const norm = sql.trim().replace(/\s+/g, ' ').toUpperCase();
        const WRITE_OPS = ['INSERT','UPDATE','DELETE','DROP','CREATE','ALTER',
                           'TRUNCATE','EXEC','EXECUTE','MERGE','CALL','GRANT',
                           'REVOKE','REPLACE','UPSERT','ATTACH','DETACH'];
        const firstWord = norm.split(/[\s(;]/)[0];

        // DAX EVALUATE and DMV queries are read-only — allow through without body scan
        var isDaxOrDmv = firstWord === 'EVALUATE'
            || (firstWord === 'SELECT' && norm.indexOf('$SYSTEM.') !== -1);
        if (isDaxOrDmv) return { valid: true };

        if (WRITE_OPS.includes(firstWord)) {
            return { valid: false, reason: `"${firstWord}" is a write operation — only read-only queries are permitted on this connection.` };
        }
        for (const kw of WRITE_OPS) {
            if (new RegExp(`\\b${kw}\\b`).test(norm)) {
                return { valid: false, reason: `Query contains "${kw}" which is not allowed. Only read-only queries are permitted.` };
            }
        }
        return { valid: true };
    }

    // Safely extract and parse a data_response JSON object from text
    function tryParseDataResponse(text) {
        let start = text.indexOf('{');
        while (start !== -1) {
            let depth = 0;
            let inString = false;
            let escape = false;
            for (let i = start; i < text.length; i++) {
                const ch = text[i];
                if (escape) { escape = false; continue; }
                if (ch === '\\' && inString) { escape = true; continue; }
                if (ch === '"') { inString = !inString; continue; }
                if (inString) continue;
                if (ch === '{') depth++;
                else if (ch === '}') {
                    depth--;
                    if (depth === 0) {
                        const candidate = text.slice(start, i + 1);
                        try {
                            const obj = JSON.parse(candidate);
                            if (obj && obj.type === 'data_response') return obj;
                        } catch { /* not valid JSON */ }
                        break;
                    }
                }
            }
            start = text.indexOf('{', start + 1);
        }
        return null;
    }

    // ── Analysis result formatter (lightweight markdown → HTML) ──────
    function formatAnalysisText(rawText) {
        let html = '';
        let inUl = false;
        let inOl = false;

        function esc(s) {
            return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }
        function inline(s) {
            return esc(s)
                .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
                .replace(/\*(.*?)\*/g, '<em>$1</em>')
                .replace(/`([^`]+)`/g, '<code class="analysis-inline-code">$1</code>');
        }
        function closeList() {
            if (inUl) { html += '</ul>'; inUl = false; }
            if (inOl) { html += '</ol>'; inOl = false; }
        }

        for (const rawLine of rawText.split('\n')) {
            const line = rawLine.trimEnd();

            // Headings: # ## ###
            const hm = line.match(/^(#{1,3})\s+(.*)/);
            if (hm) {
                closeList();
                const lvl = hm[1].length;
                html += `<div class="analysis-h analysis-h${lvl}">${inline(hm[2])}</div>`;
                continue;
            }

            // Unordered list
            const ul = line.match(/^[\-\*\+]\s+(.*)/);
            if (ul) {
                if (inOl) { html += '</ol>'; inOl = false; }
                if (!inUl) { html += '<ul class="analysis-list">'; inUl = true; }
                html += `<li>${inline(ul[1])}</li>`;
                continue;
            }

            // Ordered list
            const ol = line.match(/^(\d+)\.\s+(.*)/);
            if (ol) {
                if (inUl) { html += '</ul>'; inUl = false; }
                if (!inOl) { html += '<ol class="analysis-list">'; inOl = true; }
                html += `<li>${inline(ol[2])}</li>`;
                continue;
            }

            // Horizontal rule
            if (/^(-{3,}|\*{3,}|_{3,})$/.test(line.trim())) {
                closeList();
                html += '<hr class="analysis-hr">';
                continue;
            }

            // Blank line — close lists, add paragraph break
            if (!line.trim()) {
                closeList();
                continue;
            }

            // Regular paragraph
            closeList();
            html += `<p class="analysis-p">${inline(line)}</p>`;
        }

        closeList();
        return html;
    }

    function renderAnalysisResult(bubble, text) {
        bubble.innerHTML = `
            <div class="analysis-card">
                <div class="analysis-card-header">
                    <i class="bi bi-graph-up-arrow me-1"></i>Chart Analysis
                    <span class="analysis-model-badge">command-a-vision</span>
                </div>
                <div class="analysis-card-body">${formatAnalysisText(text)}</div>
            </div>`;
    }

    function renderErrorBubble(bubble, details) {
        if (!bubble) return;
        const status  = details.httpStatus ? `HTTP ${details.httpStatus}` : 'Network Error';
        const reason  = escapeHtml(details.errorText || 'Unknown error');
        const msg     = escapeHtml(details.message   || '—');
        const time    = escapeHtml(details.timestamp || new Date().toLocaleTimeString());
        bubble.innerHTML = `
            <div class="tp-error-card">
                <div class="tp-error-header">
                    <span><i class="bi bi-exclamation-triangle-fill me-1"></i>Request Failed</span>
                    <span class="tp-error-time">${time}</span>
                </div>
                <div class="tp-error-body">
                    <div class="tp-error-row">
                        <span class="tp-error-key">Status</span>
                        <span class="tp-error-val tp-error-status">${escapeHtml(status)}</span>
                    </div>
                    <div class="tp-error-row">
                        <span class="tp-error-key">Reason</span>
                        <span class="tp-error-val">${reason}</span>
                    </div>
                    <div class="tp-error-row">
                        <span class="tp-error-key">Message</span>
                        <span class="tp-error-val tp-error-truncate" title="${escapeHtml(details.message || '')}">${msg}</span>
                    </div>
                </div>
            </div>`;
    }

    function addMessage(role, content, streaming) {
        const container = document.getElementById('chatMessages');
        if (!container) return null;

        // Remove welcome message on first user message
        const welcome = container.querySelector('.chat-welcome');
        if (welcome && role === 'user') welcome.remove();

        const msgEl = document.createElement('div');
        msgEl.className = `chat-message ${role}-message`;
        const avatar = role === 'user'
            ? `<div class="msg-avatar user-avatar"><i class="bi bi-person"></i></div>`
            : `<div class="msg-avatar ai-avatar"><i class="bi bi-robot"></i></div>`;
        const bubbleId = 'msg-' + Date.now();
        msgEl.innerHTML = `
            ${role === 'assistant' ? avatar : ''}
            <div class="msg-content">
                <div class="msg-bubble" id="${bubbleId}">${streaming ? '<span class="typing-indicator"><span></span><span></span><span></span></span>' : escapeHtml(content)}</div>
                ${role === 'assistant' ? `<div class="msg-actions">
                    <button class="msg-action-btn" title="Copy" onclick="ChatApp.copyMessage(this)">
                        <i class="bi bi-clipboard"></i>
                    </button>
                </div>` : ''}
            </div>
            ${role === 'user' ? avatar : ''}
        `;
        container.appendChild(msgEl);
        container.scrollTop = container.scrollHeight;
        return msgEl.querySelector('.msg-bubble');
    }

    // Try to parse AI JSON data_response and render as structured card
    function renderDataResponse(bubble, jsonObj) {
        const q = escapeHtml(jsonObj.query || '');
        const desc = escapeHtml(jsonObj.description || '');
        const prompt = escapeHtml(jsonObj.prompt || '');
        const chart = jsonObj.suggestedChart || 'bar';

        bubble.innerHTML = `
            <div class="data-response-card">
                <div class="dr-section">
                    <div class="dr-label"><i class="bi bi-chat-left-quote me-1"></i>Intent</div>
                    <div class="dr-value">${prompt}</div>
                </div>
                <div class="dr-section dr-query-section">
                    <div class="dr-label-row">
                        <span class="dr-label"><i class="bi bi-code-slash me-1"></i>Generated Query</span>
                        <button class="dr-collapse-btn" title="Show query">
                            <i class="bi bi-eye me-1"></i>Show
                        </button>
                    </div>
                    <div class="dr-query-body" style="display:none">
                        <textarea class="dr-code-editor" spellcheck="false">${q}</textarea>
                    </div>
                </div>
                <div class="dr-section">
                    <div class="dr-label"><i class="bi bi-info-circle me-1"></i>Description</div>
                    <div class="dr-value">${desc}</div>
                </div>
                <div class="dr-actions">
                    <button class="btn btn-sm btn-outline-primary me-1 dr-execute-btn" title="Execute query">
                        <i class="bi bi-play-fill me-1"></i>Execute Query
                    </button>
                </div>
                <div class="dr-result-area" style="display:none"></div>
            </div>`;

        // Show / hide the query editor
        bubble.querySelector('.dr-collapse-btn')?.addEventListener('click', function() {
            const body = bubble.querySelector('.dr-query-body');
            const hidden = body.style.display === 'none';
            body.style.display = hidden ? '' : 'none';
            this.innerHTML = hidden
                ? '<i class="bi bi-eye-slash me-1"></i>Hide'
                : '<i class="bi bi-eye me-1"></i>Show';
            this.title = hidden ? 'Hide query' : 'Show query';
        });

        // Execute — validate for write ops, then use the (possibly edited) textarea value
        bubble.querySelector('.dr-execute-btn')?.addEventListener('click', async function() {
            const btn = this;
            const editedQuery = bubble.querySelector('.dr-code-editor')?.value || jsonObj.query;

            // Client-side read-only validation
            const check = validateQuery(editedQuery);
            const errEl = bubble.querySelector('.dr-validation-error') ||
                (() => {
                    const el = document.createElement('div');
                    el.className = 'dr-validation-error';
                    btn.closest('.dr-actions').insertAdjacentElement('afterend', el);
                    return el;
                })();

            if (!check.valid) {
                errEl.innerHTML = `<i class="bi bi-shield-exclamation me-1"></i>${escapeHtml(check.reason)}`;
                errEl.style.display = 'block';
                return;
            }
            errEl.style.display = 'none';

            btn.disabled = true;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Executing...';
            try {
                const r = await fetch('/api/data/execute', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ query: editedQuery, datasourceId: window.currentDatasourceId || null, userId: user?.id || '' })
                });
                const result = await r.json();
                if (result.success && result.data) {
                    renderResultTable(bubble, result.data, jsonObj, chart);
                } else {
                    btn.disabled = false;
                    const msg = result.error || 'Execution failed';
                    errEl.innerHTML = `<i class="bi bi-exclamation-triangle me-1"></i>${escapeHtml(msg)}`;
                    errEl.style.display = 'block';
                    btn.innerHTML = '<i class="bi bi-play-fill me-1"></i>Execute Query';
                }
            } catch {
                btn.disabled = false;
                btn.innerHTML = '<i class="bi bi-play-fill me-1"></i>Execute Query';
                errEl.innerHTML = '<i class="bi bi-exclamation-triangle me-1"></i>Network error. Please try again.';
                errEl.style.display = 'block';
            }
        });
    }

    function renderResultTable(bubble, data, jsonObj, suggestedChart) {
        const resultArea = bubble.querySelector('.dr-result-area');
        if (!resultArea || !data.length) return;

        const cols = Object.keys(data[0]);
        const thead = `<tr>${cols.map(c => `<th>${escapeHtml(c)}</th>`).join('')}</tr>`;
        const tbody = data.map(row =>
            `<tr>${cols.map(c => `<td>${escapeHtml(String(row[c] ?? ''))}</td>`).join('')}</tr>`
        ).join('');

        resultArea.style.display = 'block';
        resultArea.innerHTML = `
            <div class="dr-table-wrap mt-3">
                <div class="d-flex justify-content-between align-items-center mb-1">
                    <span class="small text-muted"><i class="bi bi-table me-1"></i>${data.length} rows</span>
                    <div>
                        <button class="btn btn-sm btn-success me-1 dr-pin-btn">
                            <i class="bi bi-pin me-1"></i>Pin to Dataset
                        </button>
                        <button class="btn btn-sm btn-outline-primary dr-viz-btn">
                            <i class="bi bi-bar-chart me-1"></i>Visualize
                        </button>
                    </div>
                </div>
                <div class="table-responsive dr-table-scroll">
                    <table class="table table-sm table-striped cp-table mb-0">
                        <thead>${thead}</thead>
                        <tbody>${tbody}</tbody>
                    </table>
                </div>
            </div>
            <div class="dr-chart-area mt-3" style="display:none">
                <div class="d-flex align-items-center gap-2 mb-2 flex-wrap">
                    <label class="form-label mb-0 small">Chart Type:</label>
                    <select class="form-select form-select-sm dr-chart-type-sel" style="width:auto">
                        <option value="bar" ${suggestedChart==='bar'?'selected':''}>Bar</option>
                        <option value="line" ${suggestedChart==='line'?'selected':''}>Line</option>
                        <option value="pie" ${suggestedChart==='pie'?'selected':''}>Pie</option>
                        <option value="doughnut" ${suggestedChart==='donut'||suggestedChart==='doughnut'?'selected':''}>Donut</option>
                    </select>
                    <label class="form-label mb-0 small">Label:</label>
                    <select class="form-select form-select-sm dr-label-sel" style="width:auto">
                        ${Object.keys(data[0]).map(c => `<option value="${escapeHtml(c)}" ${c===(jsonObj.suggestedFields?.label||'')?'selected':''}>${escapeHtml(c)}</option>`).join('')}
                    </select>
                    <label class="form-label mb-0 small">Value:</label>
                    <select class="form-select form-select-sm dr-value-sel" style="width:auto">
                        ${Object.keys(data[0]).map(c => `<option value="${escapeHtml(c)}" ${c===(jsonObj.suggestedFields?.value||'')?'selected':''}>${escapeHtml(c)}</option>`).join('')}
                    </select>
                    <button class="btn btn-sm btn-outline-secondary dr-render-chart-btn">Render</button>
                    <button class="btn btn-sm btn-outline-success dr-send-dashboard-btn">
                        <i class="bi bi-bar-chart-fill me-1"></i>Send to Dashboard
                    </button>
                </div>
                <div class="dr-chart-canvas-wrap" style="max-height:320px">
                    <canvas class="dr-chart-canvas"></canvas>
                </div>
                <div class="dr-analyze-bar mt-2">
                    <button class="btn btn-sm btn-outline-secondary dr-analyze-open-btn">
                        <i class="bi bi-cpu me-1"></i>Analyze with AI
                    </button>
                    <div class="dr-analyze-row" style="display:none">
                        <input type="text" class="form-control form-control-sm dr-analyze-prompt"
                               placeholder="e.g. Which region performs best, and why?">
                        <button class="btn btn-sm cp-btn-gradient dr-analyze-send-btn">
                            <i class="bi bi-send me-1"></i>Send to AI
                        </button>
                        <button class="btn btn-sm btn-link text-muted dr-analyze-cancel-btn">Cancel</button>
                    </div>
                    <div class="dr-suggest-chips" style="display:none">
                        <span class="dr-suggest-chip">Which item has the highest value?</span>
                        <span class="dr-suggest-chip">Summarize the key trends</span>
                        <span class="dr-suggest-chip">What are the outliers?</span>
                        <span class="dr-suggest-chip">Compare top vs bottom performers</span>
                    </div>
                </div>
            </div>`;

        let chartInstance = null;

        const pinBtn = resultArea.querySelector('.dr-pin-btn');
        pinBtn?.addEventListener('click', async function() {
            const name = 'pinned-' + Date.now();
            await fetch('/api/chat/pin', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    datasetName: name,
                    jsonData: JSON.stringify(data),
                    chatMessageId: 0,
                    workspaceId: currentWorkspaceId,
                    userId: user?.id || ''
                })
            });
            pinBtn.innerHTML = '<i class="bi bi-pin-fill me-1"></i>Pinned!';
            pinBtn.disabled = true;
            pinBtn.classList.remove('btn-success');
            pinBtn.classList.add('btn-primary');
        });

        const vizBtn = resultArea.querySelector('.dr-viz-btn');
        const chartArea = resultArea.querySelector('.dr-chart-area');
        vizBtn?.addEventListener('click', function() {
            chartArea.style.display = chartArea.style.display === 'none' ? 'block' : 'none';
            if (chartArea.style.display !== 'none') renderChart();
        });

        function renderChart() {
            const canvas = resultArea.querySelector('.dr-chart-canvas');
            const chartType = resultArea.querySelector('.dr-chart-type-sel')?.value || 'bar';
            const labelField = resultArea.querySelector('.dr-label-sel')?.value || Object.keys(data[0])[0];
            const valueField = resultArea.querySelector('.dr-value-sel')?.value || Object.keys(data[0])[1];
            const labels = data.map(r => String(r[labelField] ?? ''));
            const values = data.map(r => parseFloat(r[valueField]) || 0);
            if (chartInstance) { chartInstance.destroy(); chartInstance = null; }
            if (typeof Chart !== 'undefined') {
                chartInstance = new Chart(canvas, {
                    type: chartType === 'donut' ? 'doughnut' : chartType,
                    data: {
                        labels,
                        datasets: [{ label: valueField, data: values, backgroundColor: ['#4A90D9','#E87C3E','#4CAF50','#9C27B0','#FF5722','#00BCD4','#FFC107','#795548'] }]
                    },
                    options: { responsive: true, maintainAspectRatio: true, plugins: { legend: { display: chartType === 'pie' || chartType === 'doughnut' || chartType === 'donut' } } }
                });
            }
        }

        resultArea.querySelector('.dr-render-chart-btn')?.addEventListener('click', renderChart);
        resultArea.querySelector('.dr-send-dashboard-btn')?.addEventListener('click', function() {
            // Read current selector state (or fall back to first two columns)
            const chartType  = resultArea.querySelector('.dr-chart-type-sel')?.value || suggestedChart || 'bar';
            const labelField = resultArea.querySelector('.dr-label-sel')?.value  || Object.keys(data[0])[0];
            const valueField = resultArea.querySelector('.dr-value-sel')?.value  || Object.keys(data[0])[1];
            const labels     = data.map(r => String(r[labelField] ?? ''));
            const values     = data.map(r => parseFloat(r[valueField]) || 0);
            const title      = (jsonObj.prompt || 'Chat Chart').substring(0, 50);

            // Prefer live datasource + query over static snapshot
            const datasourceId = window.currentDatasourceId || null;
            const dataQuery    = jsonObj.sql || jsonObj.query || null;

            try {
                localStorage.setItem('cp_pending_chart', JSON.stringify({
                    chartType, labels, values, labelField, valueField, title,
                    datasourceId, dataQuery
                }));
            } catch (_) {}

            window.location.href = '/Dashboard?workspace=' + encodeURIComponent(currentWorkspaceId || '');
        });

        // ── Analyze chart with AI (vision) ───────────────────────────
        const analyzeOpenBtn   = resultArea.querySelector('.dr-analyze-open-btn');
        const analyzeRow       = resultArea.querySelector('.dr-analyze-row');
        const analyzeSendBtn   = resultArea.querySelector('.dr-analyze-send-btn');
        const analyzeCancelBtn = resultArea.querySelector('.dr-analyze-cancel-btn');
        const suggestChips     = resultArea.querySelector('.dr-suggest-chips');

        analyzeOpenBtn?.addEventListener('click', function() {
            analyzeRow.style.display = 'flex';
            if (suggestChips) suggestChips.style.display = 'flex';
            analyzeOpenBtn.style.display = 'none';
            resultArea.querySelector('.dr-analyze-prompt')?.focus();
        });

        analyzeCancelBtn?.addEventListener('click', function() {
            analyzeRow.style.display = 'none';
            if (suggestChips) suggestChips.style.display = 'none';
            analyzeOpenBtn.style.display = '';
        });

        // Allow Enter key inside the prompt input to submit
        resultArea.querySelector('.dr-analyze-prompt')?.addEventListener('keydown', function(e) {
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); analyzeSendBtn?.click(); }
        });

        // Suggestion chips: click to fill the prompt input and immediately send
        suggestChips?.querySelectorAll('.dr-suggest-chip').forEach(chip => {
            chip.addEventListener('click', function() {
                const promptEl = resultArea.querySelector('.dr-analyze-prompt');
                if (promptEl) promptEl.value = this.textContent.trim();
                analyzeSendBtn?.click();
            });
        });

        analyzeSendBtn?.addEventListener('click', async function() {
            const canvas = resultArea.querySelector('.dr-chart-canvas');
            if (!canvas || !chartInstance) {
                // Chart not rendered yet — render first
                renderChart();
                if (!chartInstance) return;
            }

            const prompt = resultArea.querySelector('.dr-analyze-prompt')?.value.trim()
                || 'Analyze this chart. Describe key trends, outliers, and actionable insights.';

            // Capture chart as PNG data URL
            let imageDataUrl;
            try { imageDataUrl = canvas.toDataURL('image/png'); }
            catch { return; }

            // Show user message in chat
            addMessage('user', `📊 ${prompt}`);

            // Open a streaming AI bubble
            const aiBubble = addMessage('assistant', '', true);

            // Reset the analyze bar
            analyzeRow.style.display = 'none';
            if (suggestChips) suggestChips.style.display = 'none';
            analyzeOpenBtn.style.display = '';
            if (resultArea.querySelector('.dr-analyze-prompt'))
                resultArea.querySelector('.dr-analyze-prompt').value = '';

            analyzeSendBtn.disabled = true;

            // ── Thinking panel + Cohere progress: start ──────────────
            ThinkingPanel.showThinking();
            CohereProgress.start();
            const _step2Timer = setTimeout(() => ThinkingPanel.advanceStep(2), 900);
            let _step3Timer = null;

            let fullText = '';
            let _httpStatus = null;
            try {
                const response = await fetch('/api/chat/analyze-image', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'Authorization': 'Bearer ' + (localStorage.getItem('cp_token') || '')
                    },
                    body: JSON.stringify({
                        imageDataUrl,
                        prompt,
                        workspaceId: currentWorkspaceId,
                        userId: user?.id || ''
                    })
                });
                _httpStatus = response.status;

                const reader = response.body.getReader();
                const decoder = new TextDecoder();
                if (aiBubble) aiBubble.innerHTML = '';

                while (true) {
                    const { done, value } = await reader.read();
                    if (done) break;
                    const text = decoder.decode(value);
                    for (const line of text.split('\n')) {
                        if (!line.startsWith('data: ')) continue;
                        const chunk = line.slice(6);
                        if (chunk === '[DONE]') break;
                        try {
                            const parsed = JSON.parse(chunk);
                            if (parsed.text && aiBubble) {
                                fullText += parsed.text;
                                aiBubble.textContent = fullText;
                                CohereProgress.onChunk(fullText);
                                const container = document.getElementById('chatMessages');
                                if (container) container.scrollTop = container.scrollHeight;
                            }
                        } catch {}
                    }
                }

                // ── Streaming done: advance to step 3 ────────────────
                clearTimeout(_step2Timer);
                ThinkingPanel.advanceStep(3);
                _step3Timer = setTimeout(() => ThinkingPanel.allDone(), 500);

                // Streaming done — render rich formatted analysis card
                if (aiBubble && fullText) {
                    clearTimeout(_step3Timer);
                    ThinkingPanel.allDone();
                    CohereProgress.done(false);
                    const _preview = fullText.length > 400 ? fullText.substring(0, 400) + '…' : fullText;
                    setTimeout(() => ThinkingPanel.showDetails({ prompt, description: _preview }), 350);
                    renderAnalysisResult(aiBubble, fullText);
                    const container = document.getElementById('chatMessages');
                    if (container) container.scrollTop = container.scrollHeight;
                    // Save analysis messages to chat history
                    if (currentChatId === null) {
                        currentChatId = ChatHistory.createChat(currentWorkspaceId);
                    }
                    if (currentChatId) {
                        ChatHistory.appendMessage(currentWorkspaceId, currentChatId, 'user', `📊 ${prompt}`, null);
                        ChatHistory.appendMessage(currentWorkspaceId, currentChatId, 'assistant', fullText, { type: 'analysis_result', text: fullText });
                        renderChatList(currentWorkspaceId);
                    }
                }
            } catch (err) {
                clearTimeout(_step2Timer);
                clearTimeout(_step3Timer);
                ThinkingPanel.showError({
                    message: prompt,
                    workspaceId: currentWorkspaceId,
                    userId: user?.id,
                    httpStatus: _httpStatus,
                    errorText: err?.message || 'Network error',
                    timestamp: new Date().toLocaleTimeString()
                });
                CohereProgress.error();
                renderErrorBubble(aiBubble, {
                    httpStatus: _httpStatus,
                    errorText: err?.message || 'Network error',
                    message: prompt,
                    timestamp: new Date().toLocaleTimeString()
                });
            } finally {
                analyzeSendBtn.disabled = false;
            }
        });
    }



  

    // ── Chat History UI helpers ─────────────────────────────────────
    function clearChatUI() {
        const container = document.getElementById('chatMessages');
        if (container) container.innerHTML = '';
    }

    function renderChatList(wsId) {
        const list = document.getElementById('chatHistoryList');
        if (!list) return;
        const chats = ChatHistory.getAll(wsId);
        if (!chats.length) {
            list.innerHTML = '<div class="panel-list-empty">No chats yet</div>';
            return;
        }
        list.innerHTML = chats.map(c => `
            <div class="panel-list-item chat-history-item ${c.id === currentChatId ? 'active' : ''}" data-chat-id="${c.id}">
                <i class="bi bi-chat-left-text me-2 flex-shrink-0"></i>
                <span class="chat-hist-title">${escapeHtml(c.title)}</span>
                <button class="chat-hist-del-btn" data-del-chat-id="${c.id}" title="Delete chat">
                    <i class="bi bi-trash"></i>
                </button>
            </div>`).join('');

        list.querySelectorAll('.chat-hist-del-btn').forEach(btn => {
            btn.addEventListener('click', function(e) {
                e.stopPropagation();
                const chatId = this.dataset.delChatId;
                ChatHistory.deleteChat(wsId, chatId);
                if (currentChatId === chatId) {
                    currentChatId = null;
                    clearChatUI();
                    if (window.ChatAgentContext) window.ChatAgentContext.rebuildWelcome();
                }
                renderChatList(wsId);
            });
        });
    }

    function loadChatSession(wsId, chatId) {
        const chat = ChatHistory.getChat(wsId, chatId);
        clearChatUI();
        if (!chat) return;
        for (const msg of chat.messages) {
            if (msg.role === 'user') {
                addMessage('user', msg.content, false);
            } else if (msg.role === 'assistant') {
                const bubble = addMessage('assistant', '', false);
                if (!bubble) continue;
                if (msg.dataObj) {
                    if (msg.dataObj.type === 'analysis_result') {
                        renderAnalysisResult(bubble, msg.dataObj.text);
                    } else {
                        renderDataResponse(bubble, msg.dataObj);
                    }
                } else {
                        const reparsed = tryParseDataResponse(msg.content || '');
                        if (reparsed) {
                            renderDataResponse(bubble, reparsed);
                        } else {
                            bubble.textContent = msg.content;
                        }
                    }
            }
        }
    }

    async function sendMessage(message) {
        if (!message.trim() || isStreaming) return;
        isStreaming = true;

        // Create a new chat session on first message
        if (currentChatId === null) {
            currentChatId = ChatHistory.createChat(currentWorkspaceId);
            renderChatList(currentWorkspaceId);
        }

        addMessage('user', message);
        if (currentChatId) {
            ChatHistory.appendMessage(currentWorkspaceId, currentChatId, 'user', message, null);
        }

        const aiBubble = addMessage('assistant', '', true);
        let fullText = '';

        const sendBtn = document.getElementById('chatSendBtn');
        if (sendBtn) {
            sendBtn.disabled = true;
            sendBtn.innerHTML = '<span class="spinner-border spinner-border-sm"></span>';
        }

        // ── Thinking panel: start step 1 immediately ─────────────────
            ThinkingPanel.showThinking();
            CohereProgress.start();
            const step2Timer = setTimeout(() => ThinkingPanel.advanceStep(2), 900);
            let step3Timer = null;

        let _httpStatus = null;
        try {
            const response = await fetch('/api/chat/send', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer ' + (localStorage.getItem('cp_token') || '')
                },
                    body: JSON.stringify({
                            message: message,
                            workspaceId: currentWorkspaceId,
                            agentId: window.currentAgentGuid || '',
                            userId: user?.id || ''
                        })
                });
                _httpStatus = response.status;

                const reader = response.body.getReader();
            const decoder = new TextDecoder();

            if (aiBubble) aiBubble.innerHTML = '';

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;
                const text = decoder.decode(value);
                const lines = text.split('\n');
                for (const line of lines) {
                    if (!line.startsWith('data: ')) continue;
                    const data = line.slice(6);
                    if (data === '[DONE]') break;
                    try {
                        const parsed = JSON.parse(data);
                        if (parsed.text && aiBubble) {
                            fullText += parsed.text;
                            aiBubble.textContent = fullText;
                            CohereProgress.onChunk(fullText);
                            const container = document.getElementById('chatMessages');
                            if (container) container.scrollTop = container.scrollHeight;
                        }
                    } catch {}
                }
            }

            // ── Streaming done: advance to step 3 ────────────────────
            clearTimeout(step2Timer);
            ThinkingPanel.advanceStep(3);
            step3Timer = setTimeout(() => ThinkingPanel.allDone(), 500);

            // Try to detect structured JSON response
            if (aiBubble && fullText) {
                const parsed = tryParseDataResponse(fullText);
                if (parsed) {
                    clearTimeout(step3Timer);
                    ThinkingPanel.allDone();
                    CohereProgress.done(true);
                    setTimeout(() => ThinkingPanel.showDetails(parsed), 350);
                    renderDataResponse(aiBubble, parsed);
                } else {
                    aiBubble.textContent = fullText;
                    CohereProgress.done(false);
                    const _preview = fullText.length > 400 ? fullText.substring(0, 400) + '…' : fullText;
                    setTimeout(() => ThinkingPanel.showDetails({ prompt: message, description: _preview }), 350);
                }
                // Save AI response to browser history
                if (currentChatId) {
                    ChatHistory.appendMessage(currentWorkspaceId, currentChatId, 'assistant', fullText, parsed || null);
                    renderChatList(currentWorkspaceId);
                }
            }

        } catch (err) {
            clearTimeout(step2Timer);
            clearTimeout(step3Timer);
            ThinkingPanel.showError({
                message: message,
                workspaceId: currentWorkspaceId,
                userId: user?.id,
                httpStatus: _httpStatus,
                errorText: err?.message || 'Network error',
                timestamp: new Date().toLocaleTimeString()
            });
            CohereProgress.error();
            renderErrorBubble(aiBubble, {
                httpStatus: _httpStatus,
                errorText: err?.message || 'Network error',
                message: message,
                timestamp: new Date().toLocaleTimeString()
            });
        } finally {
            isStreaming = false;
            if (sendBtn) {
                sendBtn.disabled = false;
                sendBtn.innerHTML = '<i class="bi bi-send-fill"></i>';
            }
        }
    }

    // Public API
    window.ChatApp = {
        copyMessage: function(btn) {
            const bubble = btn.closest('.msg-content').querySelector('.msg-bubble');
            navigator.clipboard.writeText(bubble?.textContent || '').then(() => {
                btn.innerHTML = '<i class="bi bi-check2 text-success"></i>';
                setTimeout(() => { btn.innerHTML = '<i class="bi bi-clipboard"></i>'; }, 2000);
            });
        }
    };

    // Expose ThinkingPanel for external modules (e.g. chat-agent-context.js)
    window.ChatThinkingPanel = {
        open: ThinkingPanel.open,
        close: ThinkingPanel.close,
        switchTab: ThinkingPanel.switchTab
    };

    document.addEventListener('DOMContentLoaded', function() {
        const input = document.getElementById('chatInput');
        const sendBtn = document.getElementById('chatSendBtn');

        if (input) {
            input.addEventListener('keydown', function(e) {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    const msg = this.value.trim();
                    if (msg) {
                        this.value = '';
                        this.style.height = 'auto';
                        sendMessage(msg);
                    }
                }
            });
        }

        if (sendBtn) {
            sendBtn.addEventListener('click', function() {
                const msg = input?.value.trim();
                if (msg) {
                    input.value = '';
                    if (input) input.style.height = 'auto';
                    sendMessage(msg);
                }
            });
        }

        // Sidebar toggle
        document.getElementById('sidebarToggle')?.addEventListener('click', function() {
            document.getElementById('leftPanel')?.classList.toggle('open');
        });

        // ── Workspace selection from left panel ──────────────────────
        document.getElementById('workspaceList')?.addEventListener('click', function(e) {
            const item = e.target.closest('[data-workspace-id]');
            if (!item) return;
            const wsId = item.dataset.workspaceId;
            if (!wsId || wsId === '0') return;

            // Highlight active item
            this.querySelectorAll('.panel-list-item').forEach(el => el.classList.remove('active'));
            item.classList.add('active');

            const wsName = item.textContent.trim();
            currentWorkspaceId = wsId;
            currentChatId = null;

            // Clear agent/datasource context from previous workspace
            window.currentAgentGuid = null;
            window.currentDatasourceId = null;
            window.currentDatasourceName = null;
            window.currentDatasourceType = null;

            // Update topbar + subnav title (only for named workspaces)
            if (wsId) {
                const titleEl = document.getElementById('chatWorkspaceTitle');
                if (titleEl) titleEl.textContent = wsName;
                const subnavName = document.getElementById('chatSubnavWorkspaceName');
                if (subnavName) subnavName.textContent = wsName;
            }

            // Reset to Chat tab when switching workspace
            switchTab('chat');

            // Load browser chat history for this workspace
            const wsChats = ChatHistory.getAll(wsId);
            if (wsChats.length) {
                currentChatId = wsChats[0].id;
                loadChatSession(wsId, currentChatId);
            } else {
                clearChatUI();
                if (window.ChatAgentContext) window.ChatAgentContext.rebuildWelcome();
            }
            renderChatList(wsId);
        });

        // ── Sub-navigation tab switching ─────────────────────────────
        document.getElementById('chatSubnavTabs')?.addEventListener('click', function(e) {
            const btn = e.target.closest('.chat-subnav-tab');
            if (!btn) return;
            switchTab(btn.dataset.tab);
        });

        function switchTab(tab) {
            // Update tab button states
            document.querySelectorAll('.chat-subnav-tab').forEach(t => {
                t.classList.toggle('active', t.dataset.tab === tab);
            });

            const chatWorkspace = document.getElementById('chatWorkspace');
            if (chatWorkspace) chatWorkspace.style.display = tab === 'chat' ? '' : 'none';
        }

        // ── Manage Users ─────────────────────────────────────────────
        async function loadWorkspaceUsers() {
            if (!currentWorkspaceId) {
                showUsersEmpty('Select a workspace first.');
                return;
            }
            const nameEl = document.getElementById('usersWorkspaceName');
            const titleEl = document.getElementById('chatWorkspaceTitle');
            if (nameEl && titleEl) nameEl.textContent = titleEl.textContent;

            try {
                const r = await fetch(`/api/workspaces/${currentWorkspaceId}/users`);
                const members = await r.json();
                renderUsersList(members);
            } catch {
                showUsersEmpty('Failed to load users.');
            }
        }

        function renderUsersList(members) {
            const list = document.getElementById('usersList');
            if (!list) return;
            if (!members.length) {
                showUsersEmpty('No users added yet. Add a user above to grant workspace access.');
                return;
            }
            list.innerHTML = members.map(m => {
                const initials = (m.fullName || m.email || '?').substring(0, 2).toUpperCase();
                const roleCls  = 'ws-role-' + (m.role || 'viewer').toLowerCase();
                return `<div class="ws-user-row" data-user-id="${escapeHtml(m.userId)}">
                    <div class="ws-user-avatar">${escapeHtml(initials)}</div>
                    <div class="ws-user-info">
                        <div class="ws-user-name">${escapeHtml(m.fullName || '—')}</div>
                        <div class="ws-user-email">${escapeHtml(m.email || '—')}</div>
                    </div>
                    <span class="ws-user-role-badge ${roleCls}">${escapeHtml(m.role)}</span>
                    <button class="ws-user-remove-btn" title="Remove access">
                        <i class="bi bi-person-dash"></i>
                    </button>
                </div>`;
            }).join('');

            list.querySelectorAll('.ws-user-remove-btn').forEach(btn => {
                btn.addEventListener('click', async function() {
                    const row = this.closest('.ws-user-row');
                    const uid = row?.dataset.userId;
                    if (!uid || !currentWorkspaceId) return;
                    this.disabled = true;
                    try {
                        await fetch(`/api/workspaces/${currentWorkspaceId}/users/${uid}`, { method: 'DELETE' });
                        row.remove();
                        if (!document.querySelector('.ws-user-row'))
                            showUsersEmpty('No users added yet. Add a user above to grant workspace access.');
                    } catch {
                        this.disabled = false;
                    }
                });
            });
        }

        function showUsersEmpty(msg) {
            const list = document.getElementById('usersList');
            if (list) list.innerHTML = `<div class="ws-users-empty"><i class="bi bi-person-x"></i><p>${escapeHtml(msg)}</p></div>`;
        }

        document.getElementById('addUserBtn')?.addEventListener('click', async function() {
            if (!currentWorkspaceId) return;
            const emailEl = document.getElementById('addUserEmail');
            const roleEl  = document.getElementById('addUserRole');
            const alertEl = document.getElementById('addUserAlert');
            const email   = emailEl?.value.trim();
            if (!email) return;

            this.disabled = true;
            if (alertEl) alertEl.style.display = 'none';

            try {
                const r = await fetch(`/api/workspaces/${currentWorkspaceId}/users`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ email, role: roleEl?.value || 'Viewer' })
                });
                const data = await r.json();
                if (!r.ok) {
                    if (alertEl) { alertEl.className = 'alert alert-danger'; alertEl.textContent = data.error || 'Failed to add user.'; alertEl.style.display = ''; }
                } else {
                    if (emailEl) emailEl.value = '';
                    await loadWorkspaceUsers();
                }
            } catch {
                if (alertEl) { alertEl.className = 'alert alert-danger'; alertEl.textContent = 'Network error. Please try again.'; alertEl.style.display = ''; }
            } finally { this.disabled = false; }
        });

        // ── Share as iframe ───────────────────────────────────────────
        let _wsAgents = [], _wsDatasources = [];

        async function loadEmbedInfo() {
            if (!currentWorkspaceId) {
                updateEmbedCode();
                return;
            }
            try {
                const r = await fetch(`/api/workspaces/${currentWorkspaceId}`);
                const data = await r.json();
                _wsAgents       = data.agents      || [];
                _wsDatasources  = data.datasources || [];

                // Populate agent selector
                const sel = document.getElementById('embedAgentSelect');
                if (sel) {
                    sel.innerHTML = '<option value="">— No specific agent —</option>'
                        + _wsAgents.map(a => `<option value="${a.id}">${escapeHtml(a.name)}</option>`).join('');
                    sel.dispatchEvent(new Event('change'));
                }
                updateEmbedCode();
            } catch {
                updateEmbedCode();
            }
        }

        document.getElementById('embedAgentSelect')?.addEventListener('change', function() {
            const agentId = parseInt(this.value, 10) || 0;
            const agent   = _wsAgents.find(a => a.id === agentId);
            const metaEl  = document.getElementById('embedAgentMeta');
            const dsMeta  = document.getElementById('embedDsMeta');

            if (agent && metaEl) {
                metaEl.innerHTML = `<strong>${escapeHtml(agent.name)}</strong>`;
            } else if (metaEl) {
                metaEl.innerHTML = '<span class="text-muted">No agent selected.</span>';
            }

            // Show linked datasource
            const ds = agent?.datasourceId
                ? _wsDatasources.find(d => d.id === agent.datasourceId)
                : null;
            if (dsMeta) {
                dsMeta.innerHTML = ds
                    ? `<strong>${escapeHtml(ds.name)}</strong> <span class="text-muted">(${escapeHtml(ds.type)})</span>`
                    : '<span class="text-muted small">No datasource linked to selected agent.</span>';
            }
            updateEmbedCode();
        });

        function updateEmbedCode() {
            const box     = document.getElementById('embedCodeBox');
            const agentId = parseInt(document.getElementById('embedAgentSelect')?.value, 10) || 0;
            const origin  = window.location.origin;
            let src = `${origin}/chat/embed?workspaceId=${currentWorkspaceId || 0}`;
            if (agentId) src += `&agentId=${agentId}`;
            const code = `<iframe\n  src="${src}"\n  width="420"\n  height="680"\n  frameborder="0"\n  allow="clipboard-write"\n  title="ChatPortal2 Assistant"\n></iframe>`;
            if (box) box.value = code;
        }

        document.getElementById('copyEmbedBtn')?.addEventListener('click', function() {
            const box = document.getElementById('embedCodeBox');
            if (!box) return;
            navigator.clipboard.writeText(box.value).then(() => {
                this.innerHTML = '<i class="bi bi-check2 text-success"></i> Copied!';
                setTimeout(() => { this.innerHTML = '<i class="bi bi-clipboard"></i> Copy'; }, 2500);
            });
        });

        document.getElementById('refreshEmbedPreview')?.addEventListener('click', function() {
            const frame = document.getElementById('embedPreviewFrame');
            const box   = document.getElementById('embedCodeBox');
            if (!frame || !box) return;
            const match = box.value.match(/src="([^"]+)"/);
            if (match) frame.src = match[1];
        });

        // AI Context panel toggle (topbar button)
        document.getElementById('aiContextToggle')?.addEventListener('click', function() {
            const p = document.getElementById('thinkingPanel');
            if (p) p.classList.toggle('tp-open');
        });

        // Close button inside the panel
        document.getElementById('thinkingPanelClose')?.addEventListener('click', function() {
            ThinkingPanel.close();
        });

        // ThinkingPanel tab switching
        document.querySelectorAll('.tp-tab').forEach(btn => {
            btn.addEventListener('click', function() {
                ThinkingPanel.switchTab(this.dataset.tpTab);
            });
        });

        // New Chat button
        document.getElementById('newChatBtn')?.addEventListener('click', function() {
            currentChatId = null;
            clearChatUI();
            if (window.ChatAgentContext) window.ChatAgentContext.rebuildWelcome();
            renderChatList(currentWorkspaceId);
        });

        // Chat history session switching
        document.getElementById('chatHistoryList')?.addEventListener('click', function(e) {
            if (e.target.closest('.chat-hist-del-btn')) return;
            const item = e.target.closest('.chat-history-item');
            if (!item) return;
            const chatId = item.dataset.chatId;
            if (!chatId) return;
            currentChatId = chatId;
            loadChatSession(currentWorkspaceId, chatId);
            renderChatList(currentWorkspaceId);
        });

        // Wire Cohere progress panel toggle
        CohereProgress.wireToggle();

        // Restore any previously saved chats for the default workspace
        renderChatList(currentWorkspaceId);

        // ── Agent switch: reload chat history scoped to the new agent ───
        document.addEventListener('chatAgentReady', function (e) {
            var detail = e.detail || {};
            currentWorkspaceId = detail.wsGuid || currentWorkspaceId;
            currentChatId = null;
            var wsChats = ChatHistory.getAll(currentWorkspaceId);
            if (wsChats.length) {
                currentChatId = wsChats[0].id;
                loadChatSession(currentWorkspaceId, currentChatId);
            } else {
                clearChatUI();
                if (window.ChatAgentContext) window.ChatAgentContext.rebuildWelcome();
            }
            renderChatList(currentWorkspaceId);
        });
    });
})();
