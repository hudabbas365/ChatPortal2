// Phase 33-B15 — Canvas rulers.
// Top + left ruler strips showing pixel positions. Redraws on scroll/resize
// and (as a light cursor hint) tracks the pointer with a thin red tick.
(function (global) {
    'use strict';

    var STEP = 50;            // major tick every 50px, minor every 10
    var RULER_SIZE = 18;      // px thickness
    var _scrollEl = null;
    var _dropZone = null;
    var _topRuler = null;
    var _leftRuler = null;
    var _cornerBox = null;
    var _enabled = false;
    var _cursorXLine = null;
    var _cursorYLine = null;
    var _onScroll = null;
    var _onResize = null;
    var _onMove = null;

    function _buildRuler(orient) {
        var r = document.createElement('canvas');
        r.className = 'canvas-ruler canvas-ruler-' + orient;
        return r;
    }

    function _drawTop() {
        if (!_topRuler || !_scrollEl) return;
        var width = _scrollEl.clientWidth - RULER_SIZE;
        var dpr = window.devicePixelRatio || 1;
        _topRuler.width = width * dpr;
        _topRuler.height = RULER_SIZE * dpr;
        _topRuler.style.width = width + 'px';
        _topRuler.style.height = RULER_SIZE + 'px';
        var ctx = _topRuler.getContext('2d');
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.fillStyle = '#f8fafc';
        ctx.fillRect(0, 0, width, RULER_SIZE);
        ctx.strokeStyle = '#e2e8f0';
        ctx.beginPath(); ctx.moveTo(0, RULER_SIZE - 0.5); ctx.lineTo(width, RULER_SIZE - 0.5); ctx.stroke();

        ctx.fillStyle = '#6c757d';
        ctx.font = '9px Inter, sans-serif';
        ctx.textBaseline = 'top';
        var scrollX = _scrollEl.scrollLeft;
        var start = Math.floor(scrollX / 10) * 10;
        for (var v = start; v < scrollX + width + 10; v += 10) {
            var x = v - scrollX;
            var major = (v % STEP) === 0;
            ctx.strokeStyle = major ? '#94a3b8' : '#cbd5e1';
            ctx.beginPath();
            ctx.moveTo(x + 0.5, major ? 6 : 12);
            ctx.lineTo(x + 0.5, RULER_SIZE);
            ctx.stroke();
            if (major && v >= 0) ctx.fillText(v, x + 2, 1);
        }
    }

    function _drawLeft() {
        if (!_leftRuler || !_scrollEl) return;
        var height = _scrollEl.clientHeight - RULER_SIZE;
        var dpr = window.devicePixelRatio || 1;
        _leftRuler.width = RULER_SIZE * dpr;
        _leftRuler.height = height * dpr;
        _leftRuler.style.width = RULER_SIZE + 'px';
        _leftRuler.style.height = height + 'px';
        var ctx = _leftRuler.getContext('2d');
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.fillStyle = '#f8fafc';
        ctx.fillRect(0, 0, RULER_SIZE, height);
        ctx.strokeStyle = '#e2e8f0';
        ctx.beginPath(); ctx.moveTo(RULER_SIZE - 0.5, 0); ctx.lineTo(RULER_SIZE - 0.5, height); ctx.stroke();

        ctx.fillStyle = '#6c757d';
        ctx.font = '9px Inter, sans-serif';
        ctx.textBaseline = 'top';
        var scrollY = _scrollEl.scrollTop;
        var start = Math.floor(scrollY / 10) * 10;
        for (var v = start; v < scrollY + height + 10; v += 10) {
            var y = v - scrollY;
            var major = (v % STEP) === 0;
            ctx.strokeStyle = major ? '#94a3b8' : '#cbd5e1';
            ctx.beginPath();
            ctx.moveTo(major ? 6 : 12, y + 0.5);
            ctx.lineTo(RULER_SIZE, y + 0.5);
            ctx.stroke();
            if (major && v >= 0) {
                ctx.save();
                ctx.translate(1, y + 2);
                ctx.fillText(v, 0, 0);
                ctx.restore();
            }
        }
    }

    function _redraw() { _drawTop(); _drawLeft(); }

    function _enable() {
        if (_enabled) return;
        _scrollEl = document.querySelector('.canvas-scroll');
        _dropZone = document.getElementById('chart-canvas-drop');
        if (!_scrollEl || !_dropZone) return;

        // Build ruler DOM if missing
        if (!_topRuler) {
            _topRuler = _buildRuler('top');
            _leftRuler = _buildRuler('left');
            _cornerBox = document.createElement('div');
            _cornerBox.className = 'canvas-ruler-corner';
            _cursorXLine = document.createElement('div');
            _cursorXLine.className = 'canvas-ruler-cursor-x';
            _cursorYLine = document.createElement('div');
            _cursorYLine.className = 'canvas-ruler-cursor-y';
            _scrollEl.appendChild(_topRuler);
            _scrollEl.appendChild(_leftRuler);
            _scrollEl.appendChild(_cornerBox);
            _scrollEl.appendChild(_cursorXLine);
            _scrollEl.appendChild(_cursorYLine);
        }

        _scrollEl.classList.add('with-rulers');
        _enabled = true;

        _onScroll = function () { _redraw(); };
        _onResize = function () { _redraw(); };
        _onMove = function (e) {
            if (!_topRuler) return;
            var rect = _scrollEl.getBoundingClientRect();
            var relX = e.clientX - rect.left;
            var relY = e.clientY - rect.top;
            if (relX < RULER_SIZE || relY < RULER_SIZE) {
                _cursorXLine.style.display = 'none';
                _cursorYLine.style.display = 'none';
                return;
            }
            _cursorXLine.style.display = 'block';
            _cursorYLine.style.display = 'block';
            _cursorXLine.style.left = relX + 'px';
            _cursorYLine.style.top  = relY + 'px';
        };

        _scrollEl.addEventListener('scroll', _onScroll);
        window.addEventListener('resize', _onResize);
        _scrollEl.addEventListener('mousemove', _onMove);
        _scrollEl.addEventListener('mouseleave', function () {
            if (_cursorXLine) _cursorXLine.style.display = 'none';
            if (_cursorYLine) _cursorYLine.style.display = 'none';
        });

        _redraw();
    }

    function _disable() {
        if (!_enabled || !_scrollEl) return;
        _scrollEl.classList.remove('with-rulers');
        _enabled = false;
        if (_onScroll) _scrollEl.removeEventListener('scroll', _onScroll);
        if (_onResize) window.removeEventListener('resize', _onResize);
        if (_onMove)   _scrollEl.removeEventListener('mousemove', _onMove);
        if (_cursorXLine) _cursorXLine.style.display = 'none';
        if (_cursorYLine) _cursorYLine.style.display = 'none';
    }

    function toggle() {
        var wantOn = !_enabled;
        localStorage.setItem('cp.canvas.rulers', wantOn ? '1' : '0');
        if (wantOn) _enable(); else _disable();
        var btn = document.getElementById('btn-rulers');
        if (btn) btn.classList.toggle('snap-active', wantOn);
    }

    function init() {
        var btn = document.getElementById('btn-rulers');
        if (btn) btn.addEventListener('click', toggle);
        // Auto-enable if user had them on previously
        if (localStorage.getItem('cp.canvas.rulers') === '1') {
            _enable();
            if (btn) btn.classList.add('snap-active');
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    global.canvasRulers = { toggle: toggle, redraw: _redraw };
}(window));
