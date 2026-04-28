// ============================================================
// AI Insights — About Organization popup
// Wires the user-menu "About" entry to a Bootstrap modal that
// fetches /api/org/about and surfaces non-sensitive metadata
// (org name + id, EU hosting region, encryption + data-storage
// notices). Falls back to localStorage `cp_user` cache when the
// network is unavailable so the popup always shows something.
// ============================================================
(function () {
    'use strict';

    var ABOUT_FALLBACK = {
        dataRegion: 'European Union (EU)',
        hostingNotice: 'Your data is hosted exclusively in European Union data centers, in compliance with GDPR.',
        encryptionNotice: 'Connection strings and credentials are encrypted at rest using AES-256.',
        dataStorageNotice: 'AI Insights does not retain copies of your business data. Queries run live against your own datasource.'
    };

    function copyToClipboard(text, btn) {
        if (window.copyGuidToClipboard) {
            window.copyGuidToClipboard(text, btn);
            return;
        }
        if (!navigator.clipboard) return;
        navigator.clipboard.writeText(text).then(function () {
            var icon = btn && btn.querySelector('i');
            if (icon) {
                icon.classList.replace('bi-clipboard', 'bi-check');
                setTimeout(function () { icon.classList.replace('bi-check', 'bi-clipboard'); }, 1500);
            }
        });
    }

    function $(id) { return document.getElementById(id); }

    function setText(id, value) {
        var el = $(id);
        if (el) el.textContent = (value == null || value === '') ? '—' : value;
    }

    function showError(message) {
        var loading = $('cpAboutLoading');
        var content = $('cpAboutContent');
        var error = $('cpAboutError');
        if (loading) loading.classList.add('d-none');
        if (content) content.classList.add('d-none');
        if (error) {
            error.textContent = message || 'Failed to load organization details.';
            error.classList.remove('d-none');
        }
    }

    function showContent(data) {
        var loading = $('cpAboutLoading');
        var content = $('cpAboutContent');
        var error = $('cpAboutError');
        if (loading) loading.classList.add('d-none');
        if (error) error.classList.add('d-none');
        if (content) content.classList.remove('d-none');

        setText('cpAboutOrgName', data.name);
        var guidVal = data.organizationGuid || data.id || null;
        setText('cpAboutOrgGuid', guidVal);
        setText('cpAboutRegion', data.dataRegion || ABOUT_FALLBACK.dataRegion);
        setText('cpAboutHostingNotice', data.hostingNotice || ABOUT_FALLBACK.hostingNotice);
        setText('cpAboutEncryption', data.encryptionNotice || ABOUT_FALLBACK.encryptionNotice);
        setText('cpAboutDataStored', data.dataStorageNotice || ABOUT_FALLBACK.dataStorageNotice);

        // Wire copy button
        var copyBtn = $('cpAboutOrgGuidCopy');
        if (copyBtn && guidVal) {
            copyBtn.onclick = function () { copyToClipboard(String(guidVal), copyBtn); };
        }

        // Update nav dropdown Org ID row
        var navRow = $('navOrgGuidRow');
        var navCode = $('navOrgGuid');
        var navCopy = $('navOrgGuidCopy');
        if (navRow && navCode && guidVal) {
            navCode.textContent = guidVal;
            navRow.classList.remove('d-none');
            if (navCopy) navCopy.onclick = function () { copyToClipboard(String(guidVal), navCopy); };
        }
    }

    function showLoading() {
        var loading = $('cpAboutLoading');
        var content = $('cpAboutContent');
        var error = $('cpAboutError');
        if (loading) loading.classList.remove('d-none');
        if (content) content.classList.add('d-none');
        if (error) error.classList.add('d-none');
    }

    function fallbackFromCache() {
        try {
            var cached = JSON.parse(localStorage.getItem('cp_user') || 'null');
            if (cached) {
                return {
                    id: cached.organizationId || null,
                    name: cached.orgName || null
                };
            }
        } catch { /* ignore */ }
        return { id: null, name: null };
    }

    async function loadAbout() {
        showLoading();
        try {
            var resp = await fetch('/api/org/about', { credentials: 'same-origin' });
            if (resp.status === 401) {
                showError('You must be signed in to view organization details.');
                return;
            }
            if (!resp.ok) throw new Error('HTTP ' + resp.status);
            var data = await resp.json();
            // Top-up from cache when the API returns nothing useful (e.g.
            // user has no organization yet but localStorage cached an id).
            if (!data.id || !data.name) {
                var fb = fallbackFromCache();
                data.id = data.id ?? fb.id;
                data.name = data.name ?? fb.name;
            }
            showContent(data);
        } catch (e) {
            // Network/server error → still render whatever we know locally
            // so the user sees something useful instead of a hard error.
            var fb = fallbackFromCache();
            if (fb.id || fb.name) {
                showContent(Object.assign({}, ABOUT_FALLBACK, fb));
            } else {
                showError('Could not load organization details. Please try again.');
            }
        }
    }

    function openAbout() {
        var modalEl = $('cpAboutModal');
        if (!modalEl || !window.bootstrap) return;
        // The modal markup lives inside the navbar partial. The navbar uses
        // `backdrop-filter`, which creates a CSS stacking context and traps
        // the modal beneath Bootstrap's body-level `.modal-backdrop`
        // (resulting in a fully grayed-out page). Move the modal directly
        // under <body> so it escapes that stacking context.
        if (modalEl.parentNode !== document.body) {
            document.body.appendChild(modalEl);
        }
        var modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        modal.show();
        loadAbout();

        // Close the user-menu dropdown so it doesn't sit open behind the modal.
        var dropdown = $('navUserMenuDropdown');
        var btn = $('navUserMenuBtn');
        if (dropdown) dropdown.hidden = true;
        if (btn) btn.setAttribute('aria-expanded', 'false');
    }

    function init() {
        var trigger = $('navUserMenuAbout');
        if (trigger && !trigger.dataset.cpAboutBound) {
            trigger.dataset.cpAboutBound = '1';
            trigger.addEventListener('click', function (e) {
                e.preventDefault();
                openAbout();
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Expose for programmatic open from elsewhere (e.g. tests, footer link).
    window.cpAbout = { open: openAbout };
})();
