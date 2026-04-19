// report-qa-panel.js — AI Q&A chat panel for the Report Viewer
// Self-contained IIFE; relies on window.aiStream (aiStream.js) for SSE streaming.
(function (global) {
    'use strict';

    // Config is set by View.cshtml before this script runs:
    //   window._rvReportGuid, window._rvWorkspaceGuid,
    //   window._rvDatasourceId, window._rvReportName

    const CHIPS = [
        'Summarize this report',
        'What are the key trends?',
        'Explain the largest values',
        'Compare the top categories',
    ];

    let _panelEl   = null;
    let _msgsEl    = null;
    let _inputEl   = null;
    let _sendBtn   = null;
    let _stopBtn   = null;
    let _isBusy    = false;
    let _abortController = null;

    // ── Build DOM ──────────────────────────────────────────────────────────────
    function _buildPanel() {
        // FAB
        const fab = document.getElementById('rv-qa-fab');
        if (fab) fab.addEventListener('click', toggle);

        // Panel
        _panelEl = document.getElementById('rv-qa-panel');
        if (!_panelEl) return;

        // Close button
        const closeBtn = _panelEl.querySelector('.rv-qa-close');
        if (closeBtn) closeBtn.addEventListener('click', close);

        // Messages area
        _msgsEl = _panelEl.querySelector('.rv-qa-messages');

        // Chips
        const chipsEl = _panelEl.querySelector('.rv-qa-chips');
        if (chipsEl) {
            CHIPS.forEach(text => {
                const chip = document.createElement('button');
                chip.className = 'rv-qa-chip';
                chip.textContent = text;
                chip.addEventListener('click', () => sendMessage(text));
                chipsEl.appendChild(chip);
            });
        }

        // Input + send + stop
        _inputEl = _panelEl.querySelector('.rv-qa-textarea');
        _sendBtn = _panelEl.querySelector('.rv-qa-send-btn');
        _stopBtn = _panelEl.querySelector('.rv-qa-stop-btn');

        if (_stopBtn) {
            _stopBtn.addEventListener('click', () => { if (_abortController) _abortController.abort(); });
        }

        if (_inputEl) {
            _inputEl.addEventListener('keydown', e => {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    _triggerSend();
                }
            });
            // Auto-resize
            _inputEl.addEventListener('input', () => {
                _inputEl.style.height = 'auto';
                _inputEl.style.height = Math.min(_inputEl.scrollHeight, 100) + 'px';
            });
        }
        if (_sendBtn) {
            _sendBtn.addEventListener('click', _triggerSend);
        }
    }

    function _triggerSend() {
        if (!_inputEl || _isBusy) return;
        const text = _inputEl.value.trim();
        if (!text) return;
        _inputEl.value = '';
        _inputEl.style.height = 'auto';
        sendMessage(text);
    }

    // ── Panel open / close / toggle ────────────────────────────────────────────
    function open() {
        if (!_panelEl) return;
        _panelEl.classList.add('open');
        if (_inputEl) setTimeout(() => _inputEl.focus(), 300);
    }

    function close() {
        if (!_panelEl) return;
        _panelEl.classList.remove('open');
    }

    function toggle() {
        if (!_panelEl) return;
        _panelEl.classList.contains('open') ? close() : open();
    }

    // ── Message helpers ────────────────────────────────────────────────────────
    function _now() {
        return new Date().toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });
    }

    function addUserBubble(text) {
        if (!_msgsEl) return;
        const msg = document.createElement('div');
        msg.className = 'rv-qa-msg user';
        msg.innerHTML =
            '<div class="rv-qa-bubble">' + _esc(text) + '</div>' +
            '<div class="rv-qa-msg-time">' + _now() + '</div>';
        _msgsEl.appendChild(msg);
        _scrollToBottom();
        return msg;
    }

    function addAIBubble() {
        if (!_msgsEl) return null;
        const msg = document.createElement('div');
        msg.className = 'rv-qa-msg ai';
        const avatar = document.createElement('div');
        avatar.className = 'rv-qa-avatar';
        avatar.innerHTML = '<i class="bi bi-stars"></i>';
        const bubble = document.createElement('div');
        bubble.className = 'rv-qa-bubble streaming';
        const timeEl = document.createElement('div');
        timeEl.className = 'rv-qa-msg-time';
        timeEl.textContent = _now();
        msg.appendChild(avatar);
        msg.appendChild(bubble);
        msg.appendChild(timeEl);
        _msgsEl.appendChild(msg);
        _scrollToBottom();
        return bubble;
    }

    function _addThinkingBubble() {
        if (!_msgsEl) return null;
        const wrapper = document.createElement('div');
        wrapper.className = 'rv-qa-msg ai';
        wrapper.innerHTML =
            '<div class="rv-qa-thinking"><span></span><span></span><span></span></div>';
        _msgsEl.appendChild(wrapper);
        _scrollToBottom();
        return wrapper;
    }

    function _scrollToBottom() {
        if (_msgsEl) _msgsEl.scrollTop = _msgsEl.scrollHeight;
    }

    function _esc(str) {
        const d = document.createElement('div');
        d.appendChild(document.createTextNode(String(str ?? '')));
        return d.innerHTML;
    }

    function _renderMarkdown(text) {
        const raw = String(text || '');
        if (global.marked && global.DOMPurify) {
            const html = global.marked.parse(raw, { breaks: true, gfm: true });
            return global.DOMPurify.sanitize(html);
        }
        return _esc(raw).replace(/\n/g, '<br>');
    }

    // ── Send message ───────────────────────────────────────────────────────────
    async function sendMessage(text) {
        if (_isBusy || !text) return;
        _isBusy = true;
        if (_sendBtn) _sendBtn.style.display = 'none';
        if (_stopBtn) _stopBtn.style.display = '';

        _abortController = new AbortController();

        addUserBubble(text);
        open(); // ensure panel is open

        const thinking = _addThinkingBubble();

        // Gather config from globals set by the view
        const userId = (() => {
            try { return JSON.parse(localStorage.getItem('cp_user') || '{}').id || ''; } catch { return ''; }
        })();

        const payload = {
            message:      text,
            workspaceId:  global._rvWorkspaceGuid  || '',
            userId:       userId,
            datasourceId: global._rvDatasourceId   || null,
            reportGuid:   global._rvReportGuid     || '',
            context:      'report_viewer',
        };

        let aiBubble = null;
        try {
            const resp = await fetch('/api/chat/send', {
                method:  'POST',
                headers: { 'Content-Type': 'application/json' },
                body:    JSON.stringify(payload),
                signal:  _abortController.signal,
            });

            // Remove thinking indicator and add real bubble
            if (thinking) thinking.remove();
            aiBubble = addAIBubble();

            if (!aiBubble) throw new Error('DOM error');

            if (!resp.ok) {
                aiBubble.classList.remove('streaming');
                aiBubble.textContent = 'Request failed (HTTP ' + resp.status + '). Please try again.';
            } else {
                // Stream tokens via aiStream helper
                const streamHelper = global.aiStream;
                if (streamHelper && streamHelper.readSseText) {
                    let fullText = '';
                    await streamHelper.readSseText(resp, chunk => {
                        fullText += chunk;
                        aiBubble.innerHTML = _renderMarkdown(fullText);
                        _scrollToBottom();
                    });
                } else {
                    // Fallback: read full body
                    const body = await resp.text();
                    aiBubble.innerHTML = _renderMarkdown(body);
                }
                aiBubble.classList.remove('streaming');
                _scrollToBottom();
            }
        } catch (err) {
            if (thinking) thinking.remove();
            if (!aiBubble) aiBubble = addAIBubble();
            if (aiBubble) {
                aiBubble.classList.remove('streaming');
                aiBubble.textContent = 'Something went wrong. Please try again.';
            }
            console.warn('[report-qa-panel] send error:', err);
        } finally {
            _isBusy = false;
            _abortController = null;
            if (_sendBtn) { _sendBtn.style.display = ''; _sendBtn.disabled = false; }
            if (_stopBtn) _stopBtn.style.display = 'none';
            if (_inputEl) _inputEl.focus();
            // Re-show suggestion chips after each response
            const chipsEl = _panelEl?.querySelector('.rv-qa-chips');
            if (chipsEl) chipsEl.style.display = '';
        }
    }

    // ── Init ───────────────────────────────────────────────────────────────────
    function init() {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', _buildPanel);
        } else {
            _buildPanel();
        }
    }

    init();

    // Expose API
    global.reportQAPanel = { open, close, toggle, sendMessage };

}(window));
