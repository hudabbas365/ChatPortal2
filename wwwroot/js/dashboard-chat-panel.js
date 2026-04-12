// Dashboard Chat Panel
// Slide-in AI Report Assistant for the Dashboard — helps design reports
(function () {
    'use strict';

    const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
    let _isStreaming = false;
    let _isOpen      = false;

    // ── Helpers ──────────────────────────────────────────────────────
    function _esc(str) {
        if (typeof escapeHtml === 'function') return escapeHtml(str);
        const d = document.createElement('div');
        d.appendChild(document.createTextNode(String(str ?? '')));
        return d.innerHTML;
    }

    function _findCol(cols, name) {
        if (!name) return null;
        if (cols.indexOf(name) !== -1) return name;
        const lower = name.toLowerCase();
        return cols.find(function (c) { return c.toLowerCase() === lower; }) || null;
    }

    function _toast(msg, type) {
        if (window.dashboardChartTransfer) {
            dashboardChartTransfer.showToast(msg, type || 'success');
        }
    }

    // ── Resolve workspace ID from URL ─────────────────────────────────
    function _getWorkspaceId() {
        const params = new URLSearchParams(window.location.search);
        const wsGuid = params.get('workspace') || params.get('ws') || '';
        if (wsGuid) return wsGuid;
        // Fallback: check cached workspace data or global context
        if (window._dashboardWsData && window._dashboardWsData.guid) return window._dashboardWsData.guid;
        if (window.currentWorkspaceGuid) return window.currentWorkspaceGuid;
        return null; // never return 0
    }

    // ── Open / close / toggle ─────────────────────────────────────────
    function open() {
        const panel   = document.getElementById('dcpPanel');
        const overlay = document.getElementById('dcpOverlay');
        const btn     = document.getElementById('dcpToggleBtn');
        if (!panel) return;
        panel.classList.add('dcp-open');
        if (overlay) overlay.classList.add('dcp-open');
        if (btn)     btn.classList.add('dcp-active');
        _isOpen = true;
        setTimeout(function () { document.getElementById('dcpInput')?.focus(); }, 280);
    }

    function close() {
        const panel   = document.getElementById('dcpPanel');
        const overlay = document.getElementById('dcpOverlay');
        const btn     = document.getElementById('dcpToggleBtn');
        if (!panel) return;
        panel.classList.remove('dcp-open');
        if (overlay) overlay.classList.remove('dcp-open');
        if (btn)     btn.classList.remove('dcp-active');
        _isOpen = false;
    }

    function toggle() {
        _isOpen ? close() : open();
    }

    // ── Message bubbles ───────────────────────────────────────────────
    function _clearWelcome() {
        document.querySelector('#dcpMessages .dcp-welcome')?.remove();
    }

    function addUserBubble(text) {
        _clearWelcome();
        const container = document.getElementById('dcpMessages');
        if (!container) return;
        const el = document.createElement('div');
        el.className = 'dcp-msg dcp-user-msg';
        el.innerHTML =
            '<div class="dcp-avatar dcp-user-avatar"><i class="bi bi-person"></i></div>' +
            '<div class="dcp-bubble">' + _esc(text) + '</div>';
        container.appendChild(el);
        container.scrollTop = container.scrollHeight;
    }

    function addAIBubble(streaming) {
        _clearWelcome();
        const container = document.getElementById('dcpMessages');
        if (!container) return null;
        const id = 'dcp-b-' + Date.now();
        const el = document.createElement('div');
        el.className = 'dcp-msg dcp-ai-msg';
        el.innerHTML =
            '<div class="dcp-avatar dcp-ai-avatar"><i class="bi bi-robot"></i></div>' +
            '<div class="dcp-bubble" id="' + id + '">' +
            (streaming ? '<div class="dcp-typing"><span></span><span></span><span></span></div>' : '') +
            '</div>';
        container.appendChild(el);
        container.scrollTop = container.scrollHeight;
        return document.getElementById(id);
    }

    // ── Parse structured data_response JSON from streamed text ────────
    function _tryParseDataResponse(text) {
        let start = text.indexOf('{');
        while (start !== -1) {
            let depth = 0, inString = false, escape = false;
            for (let i = start; i < text.length; i++) {
                const ch = text[i];
                if (escape)                      { escape = false; continue; }
                if (ch === '\\' && inString)     { escape = true;  continue; }
                if (ch === '"')                  { inString = !inString; continue; }
                if (inString) continue;
                if (ch === '{') depth++;
                else if (ch === '}') {
                    depth--;
                    if (depth === 0) {
                        const candidate = text.slice(start, i + 1);
                        try {
                            const obj = JSON.parse(candidate);
                            if (obj && obj.type === 'data_response') return obj;
                        } catch {}
                        break;
                    }
                }
            }
            start = text.indexOf('{', start + 1);
        }
        return null;
    }

    // ── Render structured data card — two-step Execute → Add flow ──────
    function renderDataCard(bubble, jsonObj) {
        const q      = jsonObj.query || '';
        const desc   = _esc(jsonObj.description || '');
        const prompt = _esc(jsonObj.prompt || '');
        const chart  = jsonObj.suggestedChart || 'bar';

        bubble.innerHTML =
            '<div class="dcp-data-card">' +
              '<div class="dcp-dc-header">' +
                '<i class="bi bi-bar-chart-line"></i>Data Response' +
              '</div>' +
              '<div class="dcp-dc-body">' +
                '<div class="dcp-dc-row">' +
                  '<span class="dcp-dc-label">Intent</span>' +
                  '<span class="dcp-dc-value">' + prompt + '</span>' +
                '</div>' +
                '<div class="dcp-dc-row">' +
                  '<span class="dcp-dc-label">Query</span>' +
                  '<textarea class="dcp-dc-code" spellcheck="false">' + _esc(q) + '</textarea>' +
                '</div>' +
                '<div class="dcp-dc-row">' +
                  '<span class="dcp-dc-label">Description</span>' +
                  '<span class="dcp-dc-value">' + desc + '</span>' +
                '</div>' +
                '<div class="dcp-dc-actions">' +
                  '<button class="dcp-add-btn dcp-exec-btn">' +
                    '<i class="bi bi-play-fill"></i>Execute' +
                  '</button>' +
                '</div>' +
                '<div class="dcp-dc-feedback" style="display:none"></div>' +
              '</div>' +
            '</div>';

        bubble.querySelector('.dcp-exec-btn').addEventListener('click', async function () {
            const btn         = this;
            const editedQuery = bubble.querySelector('.dcp-dc-code')?.value || q;
            const feedbackEl  = bubble.querySelector('.dcp-dc-feedback');

            btn.disabled = true;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Executing…';

            try {
                const r = await fetch('/api/data/execute', {
                    method : 'POST',
                    headers: {
                        'Content-Type' : 'application/json',
                        'Authorization': 'Bearer ' + (localStorage.getItem('cp_token') || '')
                    },
                    body: JSON.stringify({ query: editedQuery, datasourceId: window.currentDatasourceId || null, userId: user?.id || '' })
                });
                const result = await r.json();

                if (result.success && result.data && result.data.length) {
                    const data   = result.data;
                    const cols   = Object.keys(data[0]);
                    const lfDef  = _findCol(cols, jsonObj.suggestedFields?.label) || cols[0];
                    const vfDef  = _findCol(cols, jsonObj.suggestedFields?.value) || cols[1] || cols[0];

                    // Mini preview table (first 8 rows)
                    const thead = '<tr>' + cols.map(function (c) { return '<th>' + _esc(c) + '</th>'; }).join('') + '</tr>';
                    const tbody = data.slice(0, 8).map(function (row) {
                        return '<tr>' + cols.map(function (c) {
                            return '<td>' + _esc(String(row[c] ?? '')) + '</td>';
                        }).join('') + '</tr>';
                    }).join('');
                    const colOpts = cols.map(function (c) {
                        return '<option value="' + _esc(c) + '">' + _esc(c) + '</option>';
                    }).join('');

                    feedbackEl.style.display = '';
                    feedbackEl.innerHTML =
                        '<div class="dcp-dc-ok"><i class="bi bi-check-circle-fill"></i>' + data.length + ' rows returned</div>' +
                        '<div class="dcp-dc-result-table"><table><thead>' + thead + '</thead><tbody>' + tbody + '</tbody></table></div>' +
                        '<div class="dcp-dc-field-pickers">' +
                          '<label class="dcp-dc-picker-label">Label' +
                            '<select class="dcp-dc-select dcp-lf-select">' + colOpts + '</select>' +
                          '</label>' +
                          '<label class="dcp-dc-picker-label">Value' +
                            '<select class="dcp-dc-select dcp-vf-select">' + colOpts + '</select>' +
                          '</label>' +
                        '</div>' +
                        '<div class="dcp-dc-add-row">' +
                          '<button class="dcp-add-btn dcp-add-chart-btn">' +
                            '<i class="bi bi-plus-circle-fill me-1"></i>Add to Dashboard' +
                          '</button>' +
                        '</div>';

                    feedbackEl.querySelector('.dcp-lf-select').value = lfDef;
                    feedbackEl.querySelector('.dcp-vf-select').value = vfDef;

                    btn.innerHTML = '<i class="bi bi-arrow-repeat me-1"></i>Re-execute';
                    btn.disabled  = false;

                    feedbackEl.querySelector('.dcp-add-chart-btn').addEventListener('click', function () {
                        const addBtn = this;
                        const lf     = feedbackEl.querySelector('.dcp-lf-select').value;
                        const vf     = feedbackEl.querySelector('.dcp-vf-select').value;
                        const labels = data.map(function (row) { return String(row[lf] ?? ''); });
                        const values = data.map(function (row) { return parseFloat(row[vf]) || 0; });
                        const title  = (jsonObj.prompt || 'Chart').substring(0, 50);

                        if (!window.canvasManager) {
                            feedbackEl.querySelector('.dcp-dc-add-row').insertAdjacentHTML(
                                'beforeend',
                                '<div class="dcp-dc-err"><i class="bi bi-exclamation-circle me-1"></i>Canvas not ready — try again.</div>'
                            );
                            return;
                        }

                        addBtn.disabled = true;
                        addBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Adding…';

                        // Ensure the chart goes to the last existing page (not a new one)
                        if (canvasManager.pages && canvasManager.pages.length > 1) {
                            var lastIdx = canvasManager.pages.length - 1;
                            if (canvasManager.activePageIndex !== lastIdx) {
                                canvasManager.switchPage(lastIdx);
                            }
                        }

                        const successCard =
                            '<div class="dcp-data-card">' +
                              '<div class="dcp-dc-header dcp-dc-header-done">' +
                                '<i class="bi bi-check-circle-fill me-1"></i>Chart Added' +
                              '</div>' +
                              '<div class="dcp-dc-body">' +
                                '<span class="dcp-dc-value">' + _esc(title) + '</span>' +
                              '</div>' +
                            '</div>';

                        canvasManager.addChart({
                            chartType     : chart,
                            title         : title,
                            datasourceId  : window.currentDatasourceId || null,
                            datasetName   : (window._realTableNames && window._realTableNames[0]) || '',
                            dataQuery     : editedQuery,
                            customJsonData: JSON.stringify({ labels: labels, values: values }),
                            mapping: {
                                labelField      : lf,
                                valueField      : vf,
                                groupByField    : '',
                                xField          : '',
                                yField          : '',
                                rField          : '',
                                multiValueFields: []
                            }
                        }).then(function (added) {
                            _toast('"' + (added.title || 'Chart') + '" added to dashboard', 'success');
                            bubble.innerHTML = successCard;
                        }).catch(function () {
                            _toast('Chart added to dashboard', 'success');
                            bubble.innerHTML = successCard;
                        });
                    });
                } else {
                    const msg = result.error || 'No data returned';
                    feedbackEl.style.display = '';
                    feedbackEl.innerHTML = '<div class="dcp-dc-err"><i class="bi bi-exclamation-triangle me-1"></i>' + _esc(msg) + '</div>';
                    btn.disabled = false;
                    btn.innerHTML = '<i class="bi bi-play-fill"></i>Execute';
                }
            } catch (err) {
                feedbackEl.style.display = '';
                feedbackEl.innerHTML = '<div class="dcp-dc-err"><i class="bi bi-exclamation-triangle me-1"></i>' + _esc(err?.message || 'Network error') + '</div>';
                btn.disabled = false;
                btn.innerHTML = '<i class="bi bi-play-fill"></i>Execute';
            }
        });
    }

    // ── Send message to AI via SSE stream ─────────────────────────────
    async function sendMessage(text) {
        if (!text.trim() || _isStreaming) return;

        const wsId = _getWorkspaceId();
        if (wsId === null) {
            _toast('No workspace connected. Please open the dashboard from a workspace.', 'warn');
            return;
        }

        _isStreaming = true;

        const sendBtn = document.getElementById('dcpSendBtn');
        const input   = document.getElementById('dcpInput');
        if (sendBtn) { sendBtn.disabled = true; sendBtn.innerHTML = '<span class="spinner-border spinner-border-sm"></span>'; }
        if (input)   { input.disabled = true; }

        addUserBubble(text);
        const aiBubble = addAIBubble(true);
        let fullText   = '';
        let _httpStatus = null;

        try {
            const response = await fetch('/api/chat/send', {
                method : 'POST',
                headers: {
                    'Content-Type' : 'application/json',
                    'Authorization': 'Bearer ' + (localStorage.getItem('cp_token') || '')
                },
                body: JSON.stringify({
                    message    : text,
                    workspaceId: wsId,
                    userId     : user?.id || ''
                })
            });
            _httpStatus = response.status;

            const reader  = response.body.getReader();
            const decoder = new TextDecoder();
            if (aiBubble) aiBubble.innerHTML = '';

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;
                const raw = decoder.decode(value);
                for (const line of raw.split('\n')) {
                    if (!line.startsWith('data: ')) continue;
                    const data = line.slice(6);
                    if (data === '[DONE]') break;
                    try {
                        const parsed = JSON.parse(data);
                        if (parsed.text && aiBubble) {
                            fullText += parsed.text;
                            aiBubble.textContent = fullText;
                            const container = document.getElementById('dcpMessages');
                            if (container) container.scrollTop = container.scrollHeight;
                        }
                    } catch {}
                }
            }

            // Streaming done — check for structured response
            if (aiBubble && fullText) {
                const parsed = _tryParseDataResponse(fullText);
                if (parsed) {
                    renderDataCard(aiBubble, parsed);
                } else {
                    aiBubble.textContent = fullText;
                }
                const container = document.getElementById('dcpMessages');
                if (container) container.scrollTop = container.scrollHeight;
            }
        } catch (err) {
            if (aiBubble) {
                aiBubble.innerHTML =
                    '<span style="color:var(--cp-danger)">' +
                    '<i class="bi bi-exclamation-triangle me-1"></i>' +
                    _esc(err?.message || 'Request failed') +
                    '</span>';
            }
        } finally {
            _isStreaming = false;
            if (sendBtn) { sendBtn.disabled = false; sendBtn.innerHTML = '<i class="bi bi-send-fill"></i>'; }
            if (input)   { input.disabled = false; input.style.height = ''; input.focus(); }
        }
    }

    // ── Wire suggestion chips ─────────────────────────────────────────
    function _wireChips() {
        document.querySelectorAll('#dcpMessages .dcp-chip').forEach(function (chip) {
            chip.addEventListener('click', function () {
                const txt   = this.textContent.trim();
                const input = document.getElementById('dcpInput');
                open();
                if (input) { input.value = ''; }
                sendMessage(txt);
            });
        });
    }

    // ── Wire textarea auto-resize and keyboard shortcut ───────────────
    function _wireInput() {
        const input = document.getElementById('dcpInput');
        if (!input) return;

        input.addEventListener('input', function () {
            this.style.height = '';
            this.style.height = Math.min(this.scrollHeight, 110) + 'px';
        });

        input.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                const val = this.value.trim();
                if (val) { this.value = ''; this.style.height = ''; sendMessage(val); }
            }
        });

        document.getElementById('dcpSendBtn')?.addEventListener('click', function () {
            const val = input.value.trim();
            if (val) { input.value = ''; input.style.height = ''; sendMessage(val); }
        });
    }

    // ── Public API ────────────────────────────────────────────────────
    window.dashboardChatPanel = { open: open, close: close, toggle: toggle, sendMessage: sendMessage };

    // ── Init on DOM ready ─────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', function () {
        document.getElementById('dcpToggleBtn')?.addEventListener('click', toggle);
        document.getElementById('dcpCloseBtn')?.addEventListener('click', close);
        document.getElementById('dcpOverlay')?.addEventListener('click', close);
        _wireChips();
        _wireInput();
    });
})();
