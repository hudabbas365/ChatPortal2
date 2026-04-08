// ChatPortal2 - Embedded Chat Widget
(function() {
    'use strict';

    let isStreaming = false;

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
    }

    function addMessage(role, content) {
        const container = document.getElementById('embedMessages');
        if (!container) return null;
        const welcome = container.querySelector('.embed-welcome');
        if (welcome && role === 'user') welcome.remove();
        const msgEl = document.createElement('div');
        msgEl.className = `embed-message ${role}-message`;
        msgEl.innerHTML = `<div class="embed-bubble">${role === 'assistant' && !content ? '<span class="typing-dot"></span>' : escapeHtml(content)}</div>`;
        container.appendChild(msgEl);
        container.scrollTop = container.scrollHeight;
        return msgEl.querySelector('.embed-bubble');
    }

    async function sendMessage(message) {
        if (!message.trim() || isStreaming) return;
        isStreaming = true;
        const btn = document.getElementById('embedSendBtn');
        if (btn) btn.disabled = true;

        addMessage('user', message);
        const aiBubble = addMessage('assistant', '');
        let fullText = '';

        try {
            const response = await fetch('/api/chat/send', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ message, workspaceId: 0, userId: '' })
            });

            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            if (aiBubble) aiBubble.textContent = '';

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;
                const text = decoder.decode(value);
                for (const line of text.split('\n')) {
                    if (!line.startsWith('data: ')) continue;
                    const data = line.slice(6);
                    if (data === '[DONE]') break;
                    try {
                        const parsed = JSON.parse(data);
                        if (parsed.text && aiBubble) {
                            fullText += parsed.text;
                            aiBubble.textContent = fullText;
                            const c = document.getElementById('embedMessages');
                            if (c) c.scrollTop = c.scrollHeight;
                        }
                    } catch {}
                }
            }
        } catch {
            if (aiBubble) aiBubble.textContent = 'Error. Please try again.';
        } finally {
            isStreaming = false;
            if (btn) btn.disabled = false;
        }
    }

    document.addEventListener('DOMContentLoaded', function() {
        const input = document.getElementById('embedInput');
        const btn = document.getElementById('embedSendBtn');

        input?.addEventListener('keydown', function(e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                const msg = this.value.trim();
                if (msg) { this.value = ''; sendMessage(msg); }
            }
        });

        btn?.addEventListener('click', function() {
            const msg = input?.value.trim();
            if (msg) { input.value = ''; sendMessage(msg); }
        });
    });
})();
