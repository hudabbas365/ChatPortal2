// ChatPortal2 - Chat Workspace JS
(function() {
    'use strict';

    const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
    let currentWorkspaceId = 0;
    let isStreaming = false;

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
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
                <div class="dr-section">
                    <div class="dr-label"><i class="bi bi-code-slash me-1"></i>Generated Query</div>
                    <pre class="dr-code">${q}</pre>
                </div>
                <div class="dr-section">
                    <div class="dr-label"><i class="bi bi-info-circle me-1"></i>Description</div>
                    <div class="dr-value">${desc}</div>
                </div>
                <div class="dr-actions mt-2">
                    <button class="btn btn-sm btn-outline-primary me-1 dr-execute-btn" title="Execute query">
                        <i class="bi bi-play-fill me-1"></i>Execute Query
                    </button>
                </div>
                <div class="dr-result-area" style="display:none"></div>
            </div>`;

        // Execute query on button click
        bubble.querySelector('.dr-execute-btn')?.addEventListener('click', async function() {
            const btn = this;
            btn.disabled = true;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Executing...';
            try {
                const r = await fetch('/api/data/execute', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ query: jsonObj.query, userId: user?.id || '' })
                });
                const result = await r.json();
                if (result.success && result.data) {
                    renderResultTable(bubble, result.data, jsonObj, chart);
                }
            } catch {
                btn.innerHTML = '<i class="bi bi-exclamation-triangle me-1"></i>Execution failed';
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
                        <option value="doughnut" ${suggestedChart==='donut'?'selected':''}>Donut</option>
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
                    options: { responsive: true, maintainAspectRatio: true, plugins: { legend: { display: chartType === 'pie' || chartType === 'donut' } } }
                });
            }
        }

        resultArea.querySelector('.dr-render-chart-btn')?.addEventListener('click', renderChart);
        resultArea.querySelector('.dr-send-dashboard-btn')?.addEventListener('click', function() {
            window.open('/dashboard', '_blank');
        });
    }

    async function sendMessage(message) {
        if (!message.trim() || isStreaming) return;
        isStreaming = true;

        addMessage('user', message);

        const aiBubble = addMessage('assistant', '', true);
        let fullText = '';

        const sendBtn = document.getElementById('chatSendBtn');
        if (sendBtn) {
            sendBtn.disabled = true;
            sendBtn.innerHTML = '<span class="spinner-border spinner-border-sm"></span>';
        }

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
                    userId: user?.id || ''
                })
            });

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
                            const container = document.getElementById('chatMessages');
                            if (container) container.scrollTop = container.scrollHeight;
                        }
                    } catch {}
                }
            }

            // Try to detect structured JSON response
            if (aiBubble && fullText) {
                const jsonMatch = fullText.match(/\{[\s\S]*"type"\s*:\s*"data_response"[\s\S]*\}/);
                if (jsonMatch) {
                    try {
                        const parsed = JSON.parse(jsonMatch[0]);
                        renderDataResponse(aiBubble, parsed);
                    } catch { /* not valid JSON, keep as text */ }
                } else {
                    aiBubble.textContent = fullText;
                }
            }

        } catch (err) {
            if (aiBubble) aiBubble.textContent = 'Sorry, there was an error. Please try again.';
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
    });
})();
