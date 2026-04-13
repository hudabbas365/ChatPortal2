// report-share.js — Share link functionality for the Report Viewer
(function (global) {
    'use strict';

    const overlay = document.getElementById('rvShareOverlay');
    const wsNameEl = document.getElementById('rvShareWsName');
    const shareUrlInput = document.getElementById('rvShareUrlInput');
    const copyBtn = document.getElementById('rvShareCopyBtn');
    const generateBtn = document.getElementById('rvShareGenerateBtn');
    const statusEl = document.getElementById('rvShareStatus');
    const closeBtn = document.getElementById('rvShareCloseBtn');

    const reportGuid = global._rvReportGuid || '';
    const workspaceName = global._rvWorkspaceName || '';

    if (wsNameEl && workspaceName) wsNameEl.textContent = workspaceName;

    // ── Open / Close modal ───────────────────────────────────
    function openShareModal() {
        if (!overlay) return;
        overlay.classList.add('active');
        loadShareLink();
    }

    function closeShareModal() {
        if (!overlay) return;
        overlay.classList.remove('active');
    }

    // Click outside to close
    if (overlay) {
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) closeShareModal();
        });
    }
    if (closeBtn) closeBtn.addEventListener('click', closeShareModal);

    // ── Load existing share link ─────────────────────────────
    async function loadShareLink() {
        if (!reportGuid) return;
        setStatus('');
        try {
            const resp = await fetch('/api/reports/' + reportGuid + '/share');
            if (resp.ok) {
                const data = await resp.json();
                if (data.shareToken) {
                    showShareUrl(data.shareToken);
                    if (generateBtn) generateBtn.style.display = 'none';
                } else {
                    clearShareUrl();
                    if (generateBtn) generateBtn.style.display = '';
                }
            } else {
                clearShareUrl();
                if (generateBtn) generateBtn.style.display = '';
            }
        } catch {
            clearShareUrl();
            if (generateBtn) generateBtn.style.display = '';
        }
    }

    // ── Generate share link ──────────────────────────────────
    async function generateShareLink() {
        if (!reportGuid) return;
        setStatus('Generating…');
        if (generateBtn) generateBtn.disabled = true;
        try {
            const resp = await fetch('/api/reports/' + reportGuid + '/share', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });
            if (resp.ok) {
                const data = await resp.json();
                showShareUrl(data.shareToken);
                if (generateBtn) generateBtn.style.display = 'none';
                setStatus('Share link created! Anyone with this link will be added as a viewer.');
            } else {
                const err = await resp.json().catch(() => ({}));
                setStatus(err.error || 'Failed to generate link.', true);
            }
        } catch {
            setStatus('Network error. Please try again.', true);
        } finally {
            if (generateBtn) generateBtn.disabled = false;
        }
    }

    // ── Copy to clipboard ────────────────────────────────────
    async function copyShareUrl() {
        if (!shareUrlInput || !shareUrlInput.value) return;
        try {
            await navigator.clipboard.writeText(shareUrlInput.value);
            if (copyBtn) {
                const orig = copyBtn.innerHTML;
                copyBtn.innerHTML = '<i class="bi bi-check-lg"></i> Copied';
                copyBtn.classList.add('copied');
                setTimeout(() => {
                    copyBtn.innerHTML = orig;
                    copyBtn.classList.remove('copied');
                }, 2000);
            }
        } catch {
            shareUrlInput.select();
            document.execCommand('copy');
        }
    }

    // ── Helpers ──────────────────────────────────────────────
    function showShareUrl(token) {
        if (shareUrlInput) {
            shareUrlInput.value = window.location.origin + '/report/share/' + token;
            shareUrlInput.parentElement.style.display = '';
        }
    }

    function clearShareUrl() {
        if (shareUrlInput) {
            shareUrlInput.value = '';
            shareUrlInput.parentElement.style.display = 'none';
        }
    }

    function setStatus(msg, isError) {
        if (!statusEl) return;
        statusEl.textContent = msg;
        statusEl.style.color = isError ? '#dc2626' : '#0369a1';
        statusEl.style.display = msg ? '' : 'none';
    }

    // ── Wire up buttons ──────────────────────────────────────
    if (generateBtn) generateBtn.addEventListener('click', generateShareLink);
    if (copyBtn) copyBtn.addEventListener('click', copyShareUrl);

    // Expose to global for toolbar button onclick
    global.openShareModal = openShareModal;
    global.closeShareModal = closeShareModal;

}(window));
