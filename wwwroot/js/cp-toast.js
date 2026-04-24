// ============================================================================
// cp-toast.js — window.cpToast() and window.cpSuccessModal() helpers
// Usage:
//   cpToast({ title: 'Saved', message: 'Changes applied.', variant: 'success' });
//   cpSuccessModal({
//       title: 'License assigned',
//       message: 'Professional plan granted to user.',
//       details: [['User','jane@acme.io'], ['Plan','Professional']],
//       primaryText: 'Great',
//       variant: 'success'
//   });
// ============================================================================

(function () {
    'use strict';

    // ---------- TOAST ----------
    function ensureStack() {
        let stack = document.querySelector('.cp-toast-stack');
        if (!stack) {
            stack = document.createElement('div');
            stack.className = 'cp-toast-stack';
            document.body.appendChild(stack);
        }
        return stack;
    }

    const VARIANT_ICONS = {
        success: 'bi-check-circle-fill',
        info:    'bi-info-circle-fill',
        warn:    'bi-exclamation-triangle-fill',
        error:   'bi-x-circle-fill'
    };

    function cpToast(opts) {
        opts = opts || {};
        const variant  = opts.variant || 'success';
        const title    = opts.title   || (variant === 'error' ? 'Error' : variant === 'warn' ? 'Warning' : variant === 'info' ? 'Info' : 'Success');
        const message  = opts.message || '';
        const duration = typeof opts.duration === 'number' ? opts.duration : 4000;
        const iconCls  = opts.icon || VARIANT_ICONS[variant] || VARIANT_ICONS.success;

        const stack = ensureStack();
        const el = document.createElement('div');
        el.className = 'cp-toast cp-toast-' + variant;
        el.style.setProperty('--cp-toast-duration', duration + 'ms');
        el.innerHTML =
            '<div class="cp-toast-icon"><i class="bi ' + iconCls + '"></i></div>' +
            '<div class="cp-toast-body">' +
                '<p class="cp-toast-title">' + escapeHtml(title) + '</p>' +
                (message ? '<p class="cp-toast-msg">' + escapeHtml(message) + '</p>' : '') +
            '</div>' +
            '<button type="button" class="cp-toast-close" aria-label="Dismiss">&times;</button>' +
            (duration > 0 ? '<div class="cp-toast-progress"></div>' : '');

        stack.appendChild(el);
        // Trigger enter animation
        requestAnimationFrame(() => el.classList.add('show'));

        let timer = null;
        const dismiss = () => {
            if (el.classList.contains('hiding')) return;
            el.classList.add('hiding');
            if (timer) clearTimeout(timer);
            setTimeout(() => el.remove(), 350);
        };
        el.querySelector('.cp-toast-close').addEventListener('click', dismiss);
        if (duration > 0) timer = setTimeout(dismiss, duration);

        return { dismiss };
    }

    // ---------- SUCCESS MODAL ----------
    function cpSuccessModal(opts) {
        return new Promise(resolve => {
            opts = opts || {};
            const variant      = opts.variant || 'success';
            const title        = opts.title || 'Done';
            const message      = opts.message || '';
            const details      = Array.isArray(opts.details) ? opts.details : [];
            const primaryText  = opts.primaryText || 'OK';
            const secondary    = opts.secondaryText || null;
            const iconCls      = opts.icon || (variant === 'info' ? 'bi-info-circle' : 'bi-check-lg');

            const backdrop = document.createElement('div');
            backdrop.className = 'cp-success-backdrop';

            const detailsHtml = details.length
                ? '<div class="cp-success-details">' +
                    details.map(([k, v]) => '<div class="row-line"><span>' + escapeHtml(k) + '</span><strong>' + escapeHtml(v) + '</strong></div>').join('') +
                  '</div>'
                : '';

            backdrop.innerHTML =
                '<div class="cp-success-card cp-variant-' + variant + '">' +
                    '<div class="cp-success-check cp-variant-' + variant + '"><i class="bi ' + iconCls + '"></i></div>' +
                    '<h3 class="cp-success-title">' + escapeHtml(title) + '</h3>' +
                    (message ? '<p class="cp-success-msg">' + escapeHtml(message) + '</p>' : '') +
                    detailsHtml +
                    '<div class="cp-success-actions">' +
                        (secondary ? '<button type="button" class="btn btn-outline-secondary" data-cp-action="secondary">' + escapeHtml(secondary) + '</button>' : '') +
                        '<button type="button" class="btn cp-btn-gradient" data-cp-action="primary">' + escapeHtml(primaryText) + '</button>' +
                    '</div>' +
                '</div>';

            document.body.appendChild(backdrop);
            requestAnimationFrame(() => backdrop.classList.add('show'));

            const close = (result) => {
                backdrop.classList.remove('show');
                setTimeout(() => { backdrop.remove(); resolve(result); }, 220);
            };

            backdrop.addEventListener('click', e => {
                if (e.target === backdrop) close(false);
                const action = e.target.closest('[data-cp-action]')?.getAttribute('data-cp-action');
                if (action === 'primary') close(true);
                if (action === 'secondary') close(false);
            });

            document.addEventListener('keydown', function onKey(ev) {
                if (ev.key === 'Escape') { document.removeEventListener('keydown', onKey); close(false); }
            });
        });
    }

    function escapeHtml(s) {
        return String(s ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    window.cpToast = cpToast;
    window.cpSuccessModal = cpSuccessModal;
})();
