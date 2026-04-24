// Phase 34-A3: Autosave indicator.
// Monkey-patches `window.fetch` to count in-flight writes (POST/PUT/DELETE)
// against chart/page API endpoints and reflect status in a small pill
// pinned to the canvas toolbar.
(function (global) {
    'use strict';

    const PILL_ID = 'autosave-indicator';
    const TRACKED = /\/api\/(chart|page|reports)(\b|\/|\?|$)/i;

    let pending = 0;
    let lastSaved = null;
    let errorFlash = false;

    function ensurePill() {
        let pill = document.getElementById(PILL_ID);
        if (pill) return pill;
        pill = document.createElement('div');
        pill.id = PILL_ID;
        pill.className = 'autosave-pill idle';
        pill.innerHTML =
            '<i class="bi bi-cloud-check-fill autosave-ico"></i>' +
            '<span class="autosave-text">All changes saved</span>';
        const toolbar = document.querySelector('.canvas-toolbar');
        const host = toolbar || document.body;
        host.appendChild(pill);
        return pill;
    }

    function fmtAgo(ts) {
        if (!ts) return '';
        const diff = Math.max(0, Math.round((Date.now() - ts) / 1000));
        if (diff < 5)     return 'just now';
        if (diff < 60)    return diff + 's ago';
        if (diff < 3600)  return Math.round(diff / 60) + 'm ago';
        return Math.round(diff / 3600) + 'h ago';
    }

    function render() {
        const pill = ensurePill();
        const ico  = pill.querySelector('.autosave-ico');
        const txt  = pill.querySelector('.autosave-text');
        pill.classList.remove('idle', 'saving', 'error', 'saved');
        if (errorFlash) {
            pill.classList.add('error');
            ico.className = 'bi bi-exclamation-triangle-fill autosave-ico';
            txt.textContent = 'Save failed';
        } else if (pending > 0) {
            pill.classList.add('saving');
            ico.className = 'bi bi-arrow-repeat autosave-ico spinning';
            txt.textContent = 'Saving\u2026';
        } else if (lastSaved) {
            pill.classList.add('saved');
            ico.className = 'bi bi-cloud-check-fill autosave-ico';
            txt.textContent = 'Saved ' + fmtAgo(lastSaved);
        } else {
            pill.classList.add('idle');
            ico.className = 'bi bi-cloud-fill autosave-ico';
            txt.textContent = 'All changes saved';
        }
    }

    // Refresh "Xs ago" every 10s while idle.
    setInterval(() => { if (pending === 0 && lastSaved) render(); }, 10000);

    // Patch fetch.
    const originalFetch = global.fetch.bind(global);
    global.fetch = function (input, init) {
        const url    = typeof input === 'string' ? input : (input && input.url) || '';
        const method = (init && init.method) || (typeof input === 'object' && input && input.method) || 'GET';
        const isWrite = /^(POST|PUT|DELETE|PATCH)$/i.test(method);
        const tracked = isWrite && TRACKED.test(url);

        if (!tracked) return originalFetch(input, init);

        pending += 1;
        errorFlash = false;
        render();
        return originalFetch(input, init).then(
            (resp) => {
                pending = Math.max(0, pending - 1);
                if (resp && resp.ok) {
                    lastSaved = Date.now();
                } else {
                    errorFlash = true;
                    setTimeout(() => { errorFlash = false; render(); }, 3500);
                }
                render();
                return resp;
            },
            (err) => {
                pending = Math.max(0, pending - 1);
                errorFlash = true;
                setTimeout(() => { errorFlash = false; render(); }, 3500);
                render();
                throw err;
            }
        );
    };

    function init() { ensurePill(); render(); }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    global.autosaveIndicator = { render };
}(window));
