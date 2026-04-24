/**
 * copyRichContent.js — copy DOM content to clipboard with design preserved.
 *
 * Usage:
 *   window.copyRichContent(element, optionalPlainTextFallback, onDone(success));
 *
 * - Writes BOTH text/html (styled) and text/plain (raw text) clipboard items
 *   so pasting into Word / Outlook / Gmail / Teams / Notion keeps the design,
 *   while pasting into a terminal / plain input yields clean text.
 * - Inlines computed CSS onto a deep clone of the element so the copy renders
 *   standalone outside this page (recipient has no access to our stylesheets).
 * - Gracefully falls back to navigator.clipboard.writeText and execCommand.
 */
(function (g) {
    'use strict';

    var STYLE_PROPS = [
        'color', 'background-color', 'background-image',
        'font-family', 'font-size', 'font-weight', 'font-style',
        'text-align', 'text-decoration', 'text-transform', 'letter-spacing',
        'line-height', 'white-space',
        'padding', 'padding-top', 'padding-right', 'padding-bottom', 'padding-left',
        'margin', 'margin-top', 'margin-right', 'margin-bottom', 'margin-left',
        'border', 'border-top', 'border-right', 'border-bottom', 'border-left',
        'border-radius', 'border-color', 'border-width', 'border-style',
        'display', 'width', 'max-width'
    ];

    function shouldKeep(prop, value) {
        if (!value) return false;
        if (value === 'none' || value === 'normal' || value === 'auto') return false;
        if (prop === 'background-color' && /rgba\(0,\s*0,\s*0,\s*0\)|transparent/i.test(value)) return false;
        if (prop === 'background-image' && value === 'none') return false;
        return true;
    }

    function inlineStyles(srcEl, destEl) {
        if (!srcEl || srcEl.nodeType !== 1) return;
        var cs = window.getComputedStyle(srcEl);
        var style = '';
        for (var i = 0; i < STYLE_PROPS.length; i++) {
            var p = STYLE_PROPS[i];
            var v = cs.getPropertyValue(p);
            if (shouldKeep(p, v)) style += p + ':' + v + ';';
        }
        if (style) {
            var existing = destEl.getAttribute('style') || '';
            destEl.setAttribute('style', style + existing);
        }
        var sKids = srcEl.children;
        var dKids = destEl.children;
        var n = Math.min(sKids.length, dKids.length);
        for (var j = 0; j < n; j++) inlineStyles(sKids[j], dKids[j]);
    }

    function buildStyledHtml(el) {
        if (!el) return '';
        var clone = el.cloneNode(true);
        // Strip interactive elements that don't make sense pasted elsewhere
        clone.querySelectorAll('button, .msg-actions, .chat-msg-actions, .chat-code-actions, .dr-collapse-btn, .dr-actions').forEach(function (n) {
            n.remove();
        });
        inlineStyles(el, clone);
        return (
            '<div style="font-family: Inter, -apple-system, \'Segoe UI\', Roboto, sans-serif;' +
            'color:#1E2D3D;font-size:14px;line-height:1.6;">' +
            clone.outerHTML +
            '</div>'
        );
    }

    function writePlainFallback(text, onDone) {
        try {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text).then(
                    function () { onDone && onDone(true); },
                    function () { execFallback(text, onDone); }
                );
                return;
            }
        } catch (e) { /* ignore */ }
        execFallback(text, onDone);
    }

    function execFallback(text, onDone) {
        var ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        ta.style.pointerEvents = 'none';
        document.body.appendChild(ta);
        ta.select();
        var ok = false;
        try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
        document.body.removeChild(ta);
        onDone && onDone(!!ok);
    }

    function copyRich(elOrHtml, plainTextOrOnDone, maybeOnDone) {
        var onDone, plain, html, srcEl;
        if (typeof plainTextOrOnDone === 'function') {
            onDone = plainTextOrOnDone;
            plain = null;
        } else {
            plain = plainTextOrOnDone;
            onDone = maybeOnDone;
        }

        if (typeof elOrHtml === 'string') {
            html = elOrHtml;
            plain = plain || html.replace(/<[^>]+>/g, '').trim();
        } else {
            srcEl = elOrHtml;
            html = buildStyledHtml(srcEl);
            if (plain == null) plain = (srcEl && (srcEl.innerText || srcEl.textContent) || '').trim();
        }

        try {
            if (g.ClipboardItem && navigator.clipboard && navigator.clipboard.write) {
                var item = new ClipboardItem({
                    'text/html': new Blob([html], { type: 'text/html' }),
                    'text/plain': new Blob([plain], { type: 'text/plain' })
                });
                navigator.clipboard.write([item]).then(
                    function () { onDone && onDone(true); },
                    function () { writePlainFallback(plain, onDone); }
                );
                return;
            }
        } catch (e) { /* fall through */ }
        writePlainFallback(plain, onDone);
    }

    g.copyRichContent = copyRich;
})(window);
