// Phase 33-B13 — Smart alignment guides during drag.
// Given the rect of the card being dragged, compares it against every other
// chart on the page and returns snap adjustments + the visible guide lines
// (edges, centers, and uniform-gap lines) that should be drawn.
(function (global) {
    'use strict';

    var SNAP_THRESHOLD = 6;   // px — how close an edge must be to snap
    var _overlay = null;

    function _ensureOverlay() {
        if (_overlay && document.body.contains(_overlay)) return _overlay;
        var dropZone = document.getElementById('chart-canvas-drop');
        if (!dropZone) return null;
        _overlay = document.createElement('div');
        _overlay.className = 'smart-guides-overlay';
        dropZone.appendChild(_overlay);
        return _overlay;
    }

    function _collectRects(excludeId) {
        var cm = global.canvasManager;
        if (!cm || !cm.charts) return [];
        var out = [];
        cm.charts.forEach(function (c) {
            if (c.id === excludeId) return;
            var el = document.querySelector('[data-chart-id="' + c.id + '"]');
            var w = el ? el.offsetWidth  : (cm.colsToPixels(c.width || 6));
            var h = el ? el.offsetHeight : ((c.height || 300) + 44);
            out.push({ id: c.id, x: c.posX || 0, y: c.posY || 0, w: w, h: h });
        });
        return out;
    }

    // Returns { x, y, guides } where guides are drawable segments.
    function compute(movingId, x, y, w, h) {
        var others = _collectRects(movingId);
        var guides = [];
        var snappedX = x, snappedY = y;
        var bestDx = SNAP_THRESHOLD + 1, bestDy = SNAP_THRESHOLD + 1;

        var mL = x, mR = x + w, mCX = x + w / 2;
        var mT = y, mB = y + h, mCY = y + h / 2;

        others.forEach(function (o) {
            var oL = o.x, oR = o.x + o.w, oCX = o.x + o.w / 2;
            var oT = o.y, oB = o.y + o.h, oCY = o.y + o.h / 2;

            // Vertical guides (match X positions)
            [
                { d: oL  - mL,  at: oL,  kind: 'edge' },
                { d: oR  - mR,  at: oR,  kind: 'edge' },
                { d: oCX - mCX, at: oCX, kind: 'center' },
                { d: oL  - mR,  at: oL,  kind: 'edge' }, // moving right edge to their left
                { d: oR  - mL,  at: oR,  kind: 'edge' }  // moving left edge to their right
            ].forEach(function (c) {
                if (Math.abs(c.d) <= SNAP_THRESHOLD && Math.abs(c.d) < bestDx) {
                    bestDx = Math.abs(c.d);
                    snappedX = x + c.d;
                }
            });

            // Horizontal guides (match Y positions)
            [
                { d: oT  - mT,  at: oT,  kind: 'edge' },
                { d: oB  - mB,  at: oB,  kind: 'edge' },
                { d: oCY - mCY, at: oCY, kind: 'center' },
                { d: oT  - mB,  at: oT,  kind: 'edge' },
                { d: oB  - mT,  at: oB,  kind: 'edge' }
            ].forEach(function (c) {
                if (Math.abs(c.d) <= SNAP_THRESHOLD && Math.abs(c.d) < bestDy) {
                    bestDy = Math.abs(c.d);
                    snappedY = y + c.d;
                }
            });
        });

        // After snap decisions are made, recompute the moving rect and emit
        // only the guide lines that are actually aligned (so the overlay
        // shows a clean set of guides matching the snapped position).
        var fL = snappedX, fR = snappedX + w, fCX = snappedX + w / 2;
        var fT = snappedY, fB = snappedY + h, fCY = snappedY + h / 2;

        others.forEach(function (o) {
            var oL = o.x, oR = o.x + o.w, oCX = o.x + o.w / 2;
            var oT = o.y, oB = o.y + o.h, oCY = o.y + o.h / 2;

            // Vertical matches
            var vxCandidates = [
                { mv: fL,  ov: oL,  at: oL  },
                { mv: fR,  ov: oR,  at: oR  },
                { mv: fCX, ov: oCX, at: oCX },
                { mv: fR,  ov: oL,  at: oL  },
                { mv: fL,  ov: oR,  at: oR  }
            ];
            vxCandidates.forEach(function (c) {
                if (Math.abs(c.mv - c.ov) < 0.5) {
                    var top    = Math.min(fT, oT) - 8;
                    var bottom = Math.max(fB, oB) + 8;
                    guides.push({ orient: 'v', x: c.at, y1: top, y2: bottom });
                }
            });

            // Horizontal matches
            var hyCandidates = [
                { mv: fT,  ov: oT,  at: oT  },
                { mv: fB,  ov: oB,  at: oB  },
                { mv: fCY, ov: oCY, at: oCY },
                { mv: fB,  ov: oT,  at: oT  },
                { mv: fT,  ov: oB,  at: oB  }
            ];
            hyCandidates.forEach(function (c) {
                if (Math.abs(c.mv - c.ov) < 0.5) {
                    var left  = Math.min(fL, oL) - 8;
                    var right = Math.max(fR, oR) + 8;
                    guides.push({ orient: 'h', y: c.at, x1: left, x2: right });
                }
            });
        });

        return { x: snappedX, y: snappedY, guides: guides };
    }

    function render(guides) {
        var overlay = _ensureOverlay();
        if (!overlay) return;
        overlay.innerHTML = '';
        if (!guides || guides.length === 0) return;
        guides.forEach(function (g) {
            var d = document.createElement('div');
            if (g.orient === 'v') {
                d.className = 'smart-guide smart-guide-v';
                d.style.left   = g.x + 'px';
                d.style.top    = g.y1 + 'px';
                d.style.height = (g.y2 - g.y1) + 'px';
            } else {
                d.className = 'smart-guide smart-guide-h';
                d.style.top   = g.y + 'px';
                d.style.left  = g.x1 + 'px';
                d.style.width = (g.x2 - g.x1) + 'px';
            }
            overlay.appendChild(d);
        });
    }

    function clear() {
        if (_overlay) _overlay.innerHTML = '';
    }

    global.smartGuides = { compute: compute, render: render, clear: clear };
}(window));
