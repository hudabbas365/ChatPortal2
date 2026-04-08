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

    function addMessage(role, content, isStreaming) {
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
        msgEl.innerHTML = `
            ${role === 'assistant' ? avatar : ''}
            <div class="msg-content">
                <div class="msg-bubble" id="msg-${Date.now()}">${isStreaming ? '<span class="typing-indicator"><span></span><span></span><span></span></span>' : escapeHtml(content)}</div>
                ${role === 'assistant' ? `<div class="msg-actions">
                    <button class="msg-action-btn" title="Pin to Dashboard" onclick="ChatApp.pinMessage(this)">
                        <i class="bi bi-pin"></i>
                    </button>
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
        pinMessage: function(btn) {
            const bubble = btn.closest('.msg-content').querySelector('.msg-bubble');
            const content = bubble?.textContent || '';
            const dataset = {
                name: 'chat-result-' + Date.now(),
                data: [{ content }]
            };
            fetch('/api/chat/pin', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    datasetName: dataset.name,
                    jsonData: JSON.stringify(dataset.data),
                    chatMessageId: 0,
                    workspaceId: currentWorkspaceId,
                    userId: user?.id || ''
                })
            }).then(() => {
                btn.innerHTML = '<i class="bi bi-pin-fill text-primary"></i>';
                btn.disabled = true;
            });
        },
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
