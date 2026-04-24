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
    // Track last rendered markdown per AI bubble for copy/export
    const _aiTextMap = new WeakMap();

    // ── Build DOM ──────────────────────────────────────────────────────────────
    function _buildPanel() {
        // FAB
        const fab = document.getElementById('rv-qa-fab');
        if (fab) {
            _makeFabDraggable(fab, toggle);
        }

        // Panel
        _panelEl = document.getElementById('rv-qa-panel');
        if (!_panelEl) return;

        // Close button
        const closeBtn = _panelEl.querySelector('.rv-qa-close');
        if (closeBtn) closeBtn.addEventListener('click', close);

        // Copy-all and Export-PDF buttons
        const copyAllBtn = document.getElementById('rvQaCopyAllBtn');
        if (copyAllBtn) copyAllBtn.addEventListener('click', _copyAll);
        const exportBtn = document.getElementById('rvQaExportBtn');
        if (exportBtn) exportBtn.addEventListener('click', _exportPdf);

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

    // ── Draggable "tracer" FAB ────────────────────────────────────────────────
    // The FAB can be dragged anywhere on screen. On release it snaps to the
    // nearest horizontal edge (left or right), stays inside the viewport,
    // persists its position in localStorage, and leaves a brief sparkle trail
    // while moving. A click that didn't move enough still toggles the panel.
    const _FAB_STORAGE_KEY = 'rv-qa-fab-pos';
    const _FAB_DRAG_THRESHOLD = 5; // px — below this, treated as a click
    const _FAB_EDGE_MARGIN = 16;   // px — distance from viewport edges when snapped

    function _makeFabDraggable(fab, onClick) {
        _restoreFabPosition(fab);
        window.addEventListener('resize', () => _restoreFabPosition(fab));

        let startX = 0, startY = 0;
        let originLeft = 0, originTop = 0;
        let moved = false;
        let dragging = false;
        let pointerId = null;
        let lastTrailAt = 0;

        const onPointerDown = (e) => {
            if (e.pointerType === 'mouse' && e.button !== 0) return;
            dragging = true;
            moved = false;
            pointerId = e.pointerId;
            const rect = fab.getBoundingClientRect();
            originLeft = rect.left;
            originTop  = rect.top;
            startX = e.clientX;
            startY = e.clientY;

            // Switch from bottom/right anchoring to top/left for free movement
            fab.style.right  = 'auto';
            fab.style.bottom = 'auto';
            fab.style.left   = originLeft + 'px';
            fab.style.top    = originTop  + 'px';
            fab.style.transition = 'none';
            fab.classList.add('rv-qa-fab-dragging');
            try { fab.setPointerCapture(pointerId); } catch {}
            e.preventDefault();
        };

        const onPointerMove = (e) => {
            if (!dragging) return;
            const dx = e.clientX - startX;
            const dy = e.clientY - startY;
            if (!moved && Math.hypot(dx, dy) >= _FAB_DRAG_THRESHOLD) moved = true;
            if (!moved) return;

            const size = fab.offsetWidth;
            let nx = originLeft + dx;
            let ny = originTop  + dy;
            nx = Math.max(_FAB_EDGE_MARGIN, Math.min(window.innerWidth  - size - _FAB_EDGE_MARGIN, nx));
            ny = Math.max(_FAB_EDGE_MARGIN, Math.min(window.innerHeight - size - _FAB_EDGE_MARGIN, ny));
            fab.style.left = nx + 'px';
            fab.style.top  = ny + 'px';

            // Lightweight sparkle trail (throttled ~22fps)
            const now = performance.now();
            if (now - lastTrailAt > 45) {
                lastTrailAt = now;
                _emitFabSparkle(nx + size / 2, ny + size / 2);
            }
        };

        const onPointerUp = () => {
            if (!dragging) return;
            dragging = false;
            try { fab.releasePointerCapture(pointerId); } catch {}
            fab.classList.remove('rv-qa-fab-dragging');
            fab.style.transition = '';

            if (!moved) {
                if (typeof onClick === 'function') onClick();
                return;
            }

            // Edge-snap horizontally to whichever side is closest
            const size = fab.offsetWidth;
            const rect = fab.getBoundingClientRect();
            const centerX = rect.left + size / 2;
            const snapLeft = centerX < window.innerWidth / 2;
            const finalLeft = snapLeft
                ? _FAB_EDGE_MARGIN
                : window.innerWidth - size - _FAB_EDGE_MARGIN;
            const finalTop = Math.max(
                _FAB_EDGE_MARGIN,
                Math.min(window.innerHeight - size - _FAB_EDGE_MARGIN, rect.top)
            );

            fab.classList.add('rv-qa-fab-snapping');
            fab.style.left = finalLeft + 'px';
            fab.style.top  = finalTop  + 'px';
            setTimeout(() => fab.classList.remove('rv-qa-fab-snapping'), 320);

            _saveFabPosition(finalLeft, finalTop);
        };

        fab.addEventListener('pointerdown',      onPointerDown);
        window.addEventListener('pointermove',   onPointerMove);
        window.addEventListener('pointerup',     onPointerUp);
        window.addEventListener('pointercancel', onPointerUp);

        // Keyboard accessibility
        fab.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                if (typeof onClick === 'function') onClick();
            }
        });
    }

    function _saveFabPosition(left, top) {
        try {
            const size = 58;
            localStorage.setItem(_FAB_STORAGE_KEY, JSON.stringify({
                xPct: (left + size / 2) / window.innerWidth,
                yPct: (top  + size / 2) / window.innerHeight,
            }));
        } catch {}
    }

    function _restoreFabPosition(fab) {
        let saved = null;
        try { saved = JSON.parse(localStorage.getItem(_FAB_STORAGE_KEY) || 'null'); } catch {}
        if (!saved || typeof saved.xPct !== 'number' || typeof saved.yPct !== 'number') return;

        const size = fab.offsetWidth || 58;
        let left = (saved.xPct * window.innerWidth)  - size / 2;
        let top  = (saved.yPct * window.innerHeight) - size / 2;

        left = (left + size / 2) < window.innerWidth / 2
            ? _FAB_EDGE_MARGIN
            : window.innerWidth - size - _FAB_EDGE_MARGIN;
        top = Math.max(
            _FAB_EDGE_MARGIN,
            Math.min(window.innerHeight - size - _FAB_EDGE_MARGIN, top)
        );

        fab.style.right  = 'auto';
        fab.style.bottom = 'auto';
        fab.style.left   = left + 'px';
        fab.style.top    = top  + 'px';
    }

    function _emitFabSparkle(cx, cy) {
        const s = document.createElement('span');
        s.className = 'rv-qa-fab-sparkle';
        const jitterX = (Math.random() - 0.5) * 14;
        const jitterY = (Math.random() - 0.5) * 14;
        s.style.left = (cx + jitterX) + 'px';
        s.style.top  = (cy + jitterY) + 'px';
        document.body.appendChild(s);
        setTimeout(() => s.remove(), 700);
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
        document.body.classList.add('rv-qa-open');
        if (_inputEl) setTimeout(() => _inputEl.focus(), 300);
    }

    function close() {
        if (!_panelEl) return;
        _panelEl.classList.remove('open');
        document.body.classList.remove('rv-qa-open');
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
        // Action bar (appears on hover)
        const actions = document.createElement('div');
        actions.className = 'rv-qa-msg-actions';
        const copyBtn = document.createElement('button');
        copyBtn.className = 'rv-qa-msg-copy-btn';
        copyBtn.type = 'button';
        copyBtn.title = 'Copy this answer';
        copyBtn.innerHTML = '<i class="bi bi-clipboard"></i> Copy';
        copyBtn.addEventListener('click', () => _copyBubble(bubble, copyBtn));
        actions.appendChild(copyBtn);
        msg.appendChild(avatar);
        msg.appendChild(bubble);
        msg.appendChild(timeEl);
        msg.appendChild(actions);
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
                        _aiTextMap.set(aiBubble, fullText);
                        _scrollToBottom();
                    });
                } else {
                    // Fallback: read full body
                    const body = await resp.text();
                    aiBubble.innerHTML = _renderMarkdown(body);
                    _aiTextMap.set(aiBubble, body);
                }
                aiBubble.classList.remove('streaming');
                _scrollToBottom();
            }
        } catch (err) {
            if (thinking) thinking.remove();
            if (!aiBubble) aiBubble = addAIBubble();
            if (aiBubble) {
                aiBubble.classList.remove('streaming');
                if (err?.name === 'AbortError') {
                    aiBubble.innerHTML =
                        '<span style="display:inline-flex;align-items:center;gap:.4rem;color:#64748b;background:rgba(148,163,184,.12);border:1px solid rgba(148,163,184,.35);border-radius:10px;padding:.4rem .65rem;font-size:.85rem">' +
                        '<i class="bi bi-pause-circle"></i>' +
                        "You stopped this response. Ask me something else when you're ready." +
                        '</span>';
                } else {
                    aiBubble.textContent = 'Something went wrong. Please try again.';
                }
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

    // ── Copy / Export helpers ──────────────────────────────────────────────────
    function _flashCopied(btn) {
        if (!btn) return;
        btn.classList.add('copied');
        const original = btn.innerHTML;
        btn.innerHTML = '<i class="bi bi-check2"></i> Copied';
        setTimeout(() => {
            btn.classList.remove('copied');
            btn.innerHTML = original;
        }, 1400);
    }

    function _copyText(text, btn) {
        if (!text) return;
        const done = () => _flashCopied(btn);
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(done).catch(() => _fallbackCopy(text, btn));
        } else {
            _fallbackCopy(text, btn);
        }
    }
    function _fallbackCopy(text, btn) {
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.select();
        try { document.execCommand('copy'); _flashCopied(btn); } catch { /* ignore */ }
        document.body.removeChild(ta);
    }

    function _copyBubble(bubble, btn) {
        const text = _aiTextMap.get(bubble) || bubble.innerText || '';
        if (window.copyRichContent && bubble && bubble.nodeType === 1) {
            window.copyRichContent(bubble, text, function () { _flashCopied(btn); });
            return;
        }
        _copyText(text, btn);
    }

    function _buildTranscriptText() {
        if (!_msgsEl) return '';
        const lines = [];
        const reportName = global._rvReportName || 'Report';
        lines.push('Q&A — ' + reportName);
        lines.push('Generated: ' + new Date().toLocaleString());
        lines.push('');
        _msgsEl.querySelectorAll('.rv-qa-msg').forEach(m => {
            const isUser = m.classList.contains('user');
            const bubble = m.querySelector('.rv-qa-bubble');
            if (!bubble) return;
            const text = (isUser ? null : _aiTextMap.get(bubble)) || bubble.innerText || '';
            lines.push((isUser ? 'You: ' : 'AI: ') + text.trim());
            lines.push('');
        });
        return lines.join('\n').trim();
    }

    function _copyAll() {
        const btn = document.getElementById('rvQaCopyAllBtn');
        const text = _buildTranscriptText();
        if (!text) return;
        _copyText(text, btn);
    }

    function _exportPdf() {
        if (!_msgsEl || !_msgsEl.children.length) return;
        const btn = document.getElementById('rvQaExportBtn');
        const reportName = global._rvReportName || 'Report';
        const title = 'Q&A — ' + reportName;

        // Prefer jsPDF (already loaded by View.cshtml). Fallback: printable window.
        const jsPdfCtor = (global.jspdf && global.jspdf.jsPDF) || global.jsPDF;
        if (jsPdfCtor) {
            try {
                const doc = new jsPdfCtor({ unit: 'pt', format: 'a4' });
                const pageW = doc.internal.pageSize.getWidth();
                const pageH = doc.internal.pageSize.getHeight();
                const margin = 40;
                const maxW = pageW - margin * 2;
                let y = margin;

                doc.setFont('helvetica', 'bold');
                doc.setFontSize(14);
                doc.text(title, margin, y); y += 20;
                doc.setFont('helvetica', 'normal');
                doc.setFontSize(9);
                doc.setTextColor(120);
                doc.text('Generated: ' + new Date().toLocaleString(), margin, y);
                doc.setTextColor(0);
                y += 18;

                _msgsEl.querySelectorAll('.rv-qa-msg').forEach(m => {
                    const isUser = m.classList.contains('user');
                    const bubble = m.querySelector('.rv-qa-bubble');
                    if (!bubble) return;
                    const text = (isUser ? null : _aiTextMap.get(bubble)) || bubble.innerText || '';
                    doc.setFont('helvetica', 'bold');
                    doc.setFontSize(10);
                    doc.setTextColor(isUser ? 60 : 30);
                    doc.text(isUser ? 'You' : 'AI', margin, y);
                    y += 13;
                    doc.setFont('helvetica', 'normal');
                    doc.setFontSize(10);
                    doc.setTextColor(20);
                    const lines = doc.splitTextToSize(text.trim(), maxW);
                    lines.forEach(ln => {
                        if (y > pageH - margin) { doc.addPage(); y = margin; }
                        doc.text(ln, margin, y);
                        y += 13;
                    });
                    y += 8;
                });

                const safeName = String(reportName).replace(/[^\w\-]+/g, '_');
                doc.save('QA-' + safeName + '.pdf');
                _flashCopied(btn); // brief visual ack (reuses same class)
                return;
            } catch (e) {
                console.warn('[report-qa-panel] jsPDF export failed, falling back to print:', e);
            }
        }

        // Fallback: open print-friendly window
        const win = window.open('', '_blank');
        if (!win) return;
        const esc = s => String(s || '').replace(/[&<>]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
        let html = '<html><head><title>' + esc(title) + '</title>' +
            '<style>body{font-family:Inter,Arial,sans-serif;padding:32px;color:#1e293b;} h1{font-size:18px;margin:0 0 4px;} .meta{color:#64748b;font-size:11px;margin-bottom:20px;} .blk{margin-bottom:14px;} .who{font-weight:600;margin-bottom:4px;} .you{color:#475569;} .ai{color:#1e293b;} .msg{white-space:pre-wrap;font-size:13px;line-height:1.55;}</style>' +
            '</head><body><h1>' + esc(title) + '</h1>' +
            '<div class="meta">Generated: ' + esc(new Date().toLocaleString()) + '</div>';
        _msgsEl.querySelectorAll('.rv-qa-msg').forEach(m => {
            const isUser = m.classList.contains('user');
            const bubble = m.querySelector('.rv-qa-bubble');
            if (!bubble) return;
            const text = (isUser ? null : _aiTextMap.get(bubble)) || bubble.innerText || '';
            html += '<div class="blk"><div class="who ' + (isUser ? 'you' : 'ai') + '">' +
                (isUser ? 'You' : 'AI') + '</div><div class="msg">' + esc(text) + '</div></div>';
        });
        html += '<script>window.onload=function(){setTimeout(function(){window.print();},200);};<\/script></body></html>';
        win.document.write(html);
        win.document.close();
        _flashCopied(btn);
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
