// cp-confirm.js — Shared, well-styled confirm dialog + global autocomplete-off helper.
// Usage:
//   await cpConfirm({
//       title: 'Delete page',
//       message: 'All charts will be removed.',
//       confirmText: 'Delete',           // optional, default 'Confirm'
//       cancelText:  'Cancel',           // optional, default 'Cancel'
//       variant: 'danger' | 'warning' | 'primary',  // default 'danger'
//       icon: 'bi-trash3-fill',          // optional override
//   }) // -> Promise<boolean>
(function () {
    'use strict';

    if (window.cpConfirm) return; // already loaded

    // ── Inject styles once ─────────────────────────────────────────
    var STYLE_ID = 'cp-confirm-styles';
    if (!document.getElementById(STYLE_ID)) {
        var style = document.createElement('style');
        style.id = STYLE_ID;
        style.textContent = [
            '.cpc-overlay{position:fixed;inset:0;background:rgba(15,23,42,.55);backdrop-filter:blur(2px);z-index:9999;display:flex;align-items:center;justify-content:center;opacity:0;transition:opacity .18s ease}',
            '.cpc-overlay.cpc-open{opacity:1}',
            '.cpc-modal{width:420px;max-width:calc(100% - 32px);background:#fff;border-radius:14px;box-shadow:0 24px 64px rgba(0,0,0,.28);overflow:hidden;transform:translateY(8px) scale(.98);transition:transform .18s ease;font-family:inherit}',
            '.cpc-overlay.cpc-open .cpc-modal{transform:translateY(0) scale(1)}',
            '.cpc-head{display:flex;align-items:center;gap:10px;padding:14px 18px;color:#fff;font-weight:600;font-size:.95rem;letter-spacing:.2px}',
            '.cpc-head i{font-size:1.1rem}',
            '.cpc-head.danger{background:linear-gradient(135deg,#dc3545 0%,#c82333 100%)}',
            '.cpc-head.warning{background:linear-gradient(135deg,#f0ad4e 0%,#d99030 100%)}',
            '.cpc-head.primary{background:linear-gradient(135deg,#4f46e5 0%,#3b34c4 100%)}',
            '.cpc-body{padding:22px 22px 18px;text-align:center}',
            '.cpc-icon{font-size:2.2rem;margin-bottom:10px;display:block}',
            '.cpc-icon.danger{color:#dc3545}.cpc-icon.warning{color:#d99030}.cpc-icon.primary{color:#4f46e5}',
            '.cpc-msg{font-size:.88rem;font-weight:600;color:#1e2d3d;margin-bottom:6px;line-height:1.4}',
            '.cpc-sub{font-size:.78rem;color:#6c757d;line-height:1.5}',
            '.cpc-foot{display:flex;justify-content:flex-end;gap:10px;padding:12px 18px 18px;border-top:1px solid #f1f3f5;background:#fafbfc}',
            '.cpc-btn{min-width:96px;padding:8px 14px;border-radius:8px;font-size:.83rem;font-weight:600;border:1px solid transparent;cursor:pointer;transition:filter .12s ease,transform .12s ease;display:inline-flex;align-items:center;justify-content:center;gap:6px}',
            '.cpc-btn:active{transform:translateY(1px)}',
            '.cpc-btn-cancel{background:#fff;color:#475569;border-color:#cbd5e1}',
            '.cpc-btn-cancel:hover{background:#f1f5f9}',
            '.cpc-btn-confirm.danger{background:#dc3545;color:#fff}',
            '.cpc-btn-confirm.warning{background:#d99030;color:#fff}',
            '.cpc-btn-confirm.primary{background:#4f46e5;color:#fff}',
            '.cpc-btn-confirm:hover{filter:brightness(1.05)}',
            '.cpc-btn-confirm:focus,.cpc-btn-cancel:focus{outline:2px solid #94a3b8;outline-offset:2px}'
        ].join('');
        document.head.appendChild(style);
    }

    function _esc(s) {
        var d = document.createElement('div');
        d.appendChild(document.createTextNode(String(s == null ? '' : s)));
        return d.innerHTML;
    }

    var DEFAULT_ICONS = {
        danger:  'bi-exclamation-triangle-fill',
        warning: 'bi-exclamation-circle-fill',
        primary: 'bi-question-circle-fill'
    };

    function cpConfirm(opts) {
        opts = opts || {};
        var title       = opts.title       || 'Confirm';
        var message     = opts.message     || '';
        var subMessage  = opts.subMessage  || opts.detail || '';
        var confirmText = opts.confirmText || 'Confirm';
        var cancelText  = opts.cancelText  || 'Cancel';
        var variant     = (opts.variant || 'danger').toLowerCase();
        if (!DEFAULT_ICONS[variant]) variant = 'danger';
        var icon        = opts.icon || DEFAULT_ICONS[variant];

        return new Promise(function (resolve) {
            var overlay = document.createElement('div');
            overlay.className = 'cpc-overlay';
            overlay.setAttribute('role', 'dialog');
            overlay.setAttribute('aria-modal', 'true');
            overlay.innerHTML =
                '<div class="cpc-modal">' +
                    '<div class="cpc-head ' + variant + '">' +
                        '<i class="bi ' + _esc(icon) + '"></i>' +
                        '<span>' + _esc(title) + '</span>' +
                    '</div>' +
                    '<div class="cpc-body">' +
                        '<i class="cpc-icon bi ' + _esc(icon) + ' ' + variant + '"></i>' +
                        '<div class="cpc-msg">' + _esc(message) + '</div>' +
                        (subMessage ? '<div class="cpc-sub">' + _esc(subMessage) + '</div>' : '') +
                    '</div>' +
                    '<div class="cpc-foot">' +
                        '<button class="cpc-btn cpc-btn-cancel" data-cpc="cancel">' + _esc(cancelText) + '</button>' +
                        '<button class="cpc-btn cpc-btn-confirm ' + variant + '" data-cpc="ok">' + _esc(confirmText) + '</button>' +
                    '</div>' +
                '</div>';

            document.body.appendChild(overlay);
            requestAnimationFrame(function () { overlay.classList.add('cpc-open'); });

            var okBtn = overlay.querySelector('[data-cpc="ok"]');
            var cancelBtn = overlay.querySelector('[data-cpc="cancel"]');
            setTimeout(function () { okBtn && okBtn.focus(); }, 60);

            function close(result) {
                overlay.classList.remove('cpc-open');
                document.removeEventListener('keydown', onKey, true);
                setTimeout(function () { overlay.remove(); }, 200);
                resolve(!!result);
            }
            function onKey(e) {
                if (e.key === 'Escape') { e.preventDefault(); close(false); }
                else if (e.key === 'Enter') { e.preventDefault(); close(true); }
            }
            okBtn.addEventListener('click', function () { close(true); });
            cancelBtn.addEventListener('click', function () { close(false); });
            overlay.addEventListener('click', function (e) { if (e.target === overlay) close(false); });
            document.addEventListener('keydown', onKey, true);
        });
    }

    window.cpConfirm = cpConfirm;

    // ── Disable autocomplete globally on all inputs / textareas ───
    // The product is a private workspace where browser autofill on
    // datasource credentials, agent names, etc. is undesirable.
    function _disableAutocompleteOn(el) {
        if (!el || el.nodeType !== 1) return;
        var tag = el.tagName;
        if (tag !== 'INPUT' && tag !== 'TEXTAREA' && tag !== 'SELECT' && tag !== 'FORM') return;
        // Honour explicit opt-in via data-cp-autofill="on"
        if (el.dataset && el.dataset.cpAutofill === 'on') return;
        if (tag === 'FORM') {
            if (!el.hasAttribute('autocomplete')) el.setAttribute('autocomplete', 'off');
            return;
        }
        // Password fields: use 'new-password' to defeat aggressive Chrome autofill
        var type = (el.getAttribute('type') || '').toLowerCase();
        var val = (type === 'password') ? 'new-password' : 'off';
        el.setAttribute('autocomplete', val);
        // Defeat browser-specific heuristics
        if (!el.hasAttribute('autocorrect'))    el.setAttribute('autocorrect', 'off');
        if (!el.hasAttribute('autocapitalize')) el.setAttribute('autocapitalize', 'off');
        if (!el.hasAttribute('spellcheck'))     el.setAttribute('spellcheck', 'false');
    }

    function _scanRoot(root) {
        if (!root || !root.querySelectorAll) return;
        if (root.nodeType === 1) _disableAutocompleteOn(root);
        root.querySelectorAll('input, textarea, select, form').forEach(_disableAutocompleteOn);
    }

    function _initAutocompleteOff() {
        _scanRoot(document.body);
        try {
            var mo = new MutationObserver(function (mutations) {
                for (var i = 0; i < mutations.length; i++) {
                    var m = mutations[i];
                    if (m.type === 'childList') {
                        m.addedNodes.forEach(function (n) { _scanRoot(n); });
                    } else if (m.type === 'attributes' && m.target && m.target.tagName === 'INPUT') {
                        _disableAutocompleteOn(m.target);
                    }
                }
            });
            mo.observe(document.body, { childList: true, subtree: true });
        } catch { /* ignore */ }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', _initAutocompleteOff);
    } else {
        _initAutocompleteOff();
    }
})();
