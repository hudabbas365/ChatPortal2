// ── Chart renderer using ApexCharts ──────────────────────────────────────────

// Install ApexCharts error suppressor FIRST (before class definition and
// before any chart instance exists). ApexCharts' internal async work (inside
// apexcharts.min.js) can reject with "Cannot read properties of undefined
// (reading 'colors')" or "reading 'w'" after an instance is destroyed during
// rapid re-renders (theme switches, resize cascades). Those rejections are
// orphaned — they have no caller try/catch — and surface as
// "Uncaught (in promise)" console noise. Swallow only ApexCharts-origin
// failures; everything else passes through untouched.
(function installApexErrorSuppressor() {
    if (window.__apexRejectionHandlerInstalled) return;
    window.__apexRejectionHandlerInstalled = true;

    const isApexError = (reason) => {
        if (!reason) return false;
        const msg = String(reason.message || reason);
        const stack = String(reason.stack || '').toLowerCase();
        return stack.includes('apexcharts') ||
               msg.includes("reading 'colors'") ||
               msg.includes("reading 'w'") ||
               msg.includes("reading 'call'") ||
               msg.includes("reading 'getAttribute'") ||
               msg.includes("getComputedStyle");
    };

    window.addEventListener('unhandledrejection', (e) => {
        if (isApexError(e.reason)) {
            e.preventDefault();
            e.stopImmediatePropagation();
        }
    }, true);

    window.addEventListener('error', (e) => {
        const src = String(e.filename || '').toLowerCase();
        const msg = String(e.message || '');
        if (src.includes('apexcharts') ||
            msg.includes("reading 'colors'") ||
            msg.includes("reading 'w'") ||
            msg.includes("reading 'getAttribute'") ||
            msg.includes("getComputedStyle")) {
            e.preventDefault();
            e.stopImmediatePropagation();
            return true;
        }
    }, true);
})();

// Chart-render diagnostic logs are off by default. Enable at runtime with
// `window.__chartRendererDebug = true` in the DevTools console to surface the
// full [ChartRender] / [CardStyle] trace (used when diagnosing ApexCharts
// structural crashes like "Cannot read properties of undefined (reading
// 'colors')").
if (typeof window.__chartRendererDebug === 'undefined') {
    window.__chartRendererDebug = false;
}

// Chart rendering engine using ApexCharts
class ChartRenderer {
    constructor() {
        this.instances = {};
        this.colorPalettes = {
            default: ['#4A90D9','#E87C3E','#4CAF50','#9C27B0','#FF5722','#00BCD4','#FFC107','#795548'],
            ocean:   ['#006994','#0099CC','#00BFFF','#40E0D0','#20B2AA','#008080','#4682B4','#5F9EA0'],
            sunset:  ['#FF6B6B','#FF8E53','#FF6B35','#F7C59F','#EFEFD0','#4ECDC4','#45B7D1','#96CEB4'],
            forest:  ['#2D6A4F','#40916C','#52B788','#74C69D','#95D5B2','#B7E4C7','#D8F3DC','#1B4332'],
            rainbow: ['#E63946','#E76F51','#F4A261','#2A9D8F','#457B9D','#6A4C93','#BC4749','#264653'],
            pastel:  ['#FFB3BA','#FFDFBA','#FFFFBA','#BAFFC9','#BAE1FF','#D4BAFF','#FFBAF0','#C9F0FF'],
            midnight: ['#0f172a','#1e293b','#334155','#475569','#64748b','#0ea5e9','#6366f1','#a855f7'],
            corporate: ['#1f2937','#374151','#4b5563','#6b7280','#9ca3af','#2563eb','#0f766e','#7c3aed'],
            retro: ['#8c564b','#bcbd22','#17becf','#ff7f0e','#d62728','#9467bd','#2ca02c','#1f77b4'],
            neon: ['#00f5d4','#00bbf9','#9b5de5','#f15bb5','#fee440','#38bdf8','#22d3ee','#a3e635'],
            earth: ['#7f5539','#9c6644','#b08968','#ddb892','#a98467','#6c584c','#adc178','#588157'],
            monochrome: ['#111827','#1f2937','#374151','#4b5563','#6b7280','#9ca3af','#d1d5db','#e5e7eb'],
        };
        this._customRenderTypes = new Set([
            'treemap','heatmap','sankey','sunburst',
            'boxPlot','violin','stemLeaf',
            'candlestick','ohlc','eventTimeline',
            'choropleth','bubbleMap','heatMapGeo','flowMap','spikeMap',
            'networkGraph','chordDiagram','arcDiagram','forceDirected','matrix',
            'waffleChart','pictograph',
            'kpi','kpiCard','metricTile','card',
            'marimekko','dumbbell',
            'table','slicer','navigation'
        ]);
    }

    // Safe HTML escaping — works even if the global escapeHtml is not loaded yet
    _esc(str) {
        if (typeof escapeHtml === 'function') return escapeHtml(str);
        const d = document.createElement('div');
        d.appendChild(document.createTextNode(String(str ?? '')));
        return d.innerHTML;
    }

    // Compact numeric formatter — turns 1234 → "1.2k", 1500000 → "1.5M", etc.
    // Used on y-axis labels, tooltips, and data labels so charts don't render
    // walls of "100000.000000000000" digits. Keeps small values (<1000)
    // readable with up to 2 fraction digits, drops trailing zeros.
    _formatNumber(raw) {
        const v = Number(raw);
        if (!isFinite(v)) return String(raw ?? '');
        const abs = Math.abs(v);
        const sign = v < 0 ? '-' : '';
        const trim = (n, d) => n.toFixed(d).replace(/\.?0+$/, '');
        if (abs >= 1e12) return sign + trim(abs / 1e12, 2) + 'T';
        if (abs >= 1e9)  return sign + trim(abs / 1e9, 2)  + 'B';
        if (abs >= 1e6)  return sign + trim(abs / 1e6, 2)  + 'M';
        if (abs >= 1e3)  return sign + trim(abs / 1e3, 2)  + 'k';
        if (abs === 0)   return '0';
        if (abs >= 1)    return sign + trim(abs, 2);
        return sign + trim(abs, 4);
    }

    // Shorten long ISO/date-like strings so chart axes don't overlap.
    // "2008-06-01T00:00:00(.xxx)(Z)" → "2008-06-01" ; "2008-06-01 00:00:00" → "2008-06-01".
    // Pure numeric and short labels pass through unchanged.
    _formatLabel(raw) {
        const s = String(raw ?? '');
        if (s.length < 11) return s;
        // Strip trailing midnight time component from ISO/SQL datetimes
        const isoMid = s.match(/^(\d{4}-\d{2}-\d{2})[T\s]00:00(?::00(?:\.\d+)?)?Z?$/);
        if (isoMid) return isoMid[1];
        // Any ISO datetime → "YYYY-MM-DD HH:mm" (drop seconds/ms/Z)
        const iso = s.match(/^(\d{4}-\d{2}-\d{2})[T\s](\d{2}:\d{2})/);
        if (iso) return iso[1] + ' ' + iso[2];
        return s;
    }

    getColors(palette, count) {
        let colors;
        if (palette && palette.startsWith('custom:')) {
            const custom = palette.substring('custom:'.length).split(',').map(c => c.trim()).filter(c => /^#([0-9a-fA-F]{6})$/.test(c));
            colors = custom.length ? custom : this.colorPalettes.default;
        } else if (palette && palette.startsWith('#')) {
            // Hex color provided — use it as the first color, then append distinct palette colors
            const base = this.colorPalettes.default;
            colors = [palette, ...base.filter(c => c !== palette)];
        } else {
            colors = this.colorPalettes[palette] || this.colorPalettes.default;
        }
        const result = [];
        for (let i = 0; i < count; i++) result.push(colors[i % colors.length]);
        return result;
    }

    _generateColorShades(hex, count) {
        try {
            const r = parseInt(hex.slice(1, 3), 16);
            const g = parseInt(hex.slice(3, 5), 16);
            const b = parseInt(hex.slice(5, 7), 16);
            const shades = [];
            for (let i = 0; i < count; i++) {
                // factor ranges from 0.5 (darker) to 1.3 (lighter) across the palette
                const factor = 0.5 + (i / count) * 0.8;
                // blend the channel with 30% white at darker end to avoid pure black
                const mix = (channel) => Math.min(255, Math.round(channel * factor + 255 * (1 - factor) * 0.3));
                const nr = mix(r);
                const ng = mix(g);
                const nb = mix(b);
                shades.push(`#${nr.toString(16).padStart(2,'0')}${ng.toString(16).padStart(2,'0')}${nb.toString(16).padStart(2,'0')}`);
            }
            shades[0] = hex;
            return shades;
        } catch (e) {
            return this.colorPalettes.default;
        }
    }

    hexToRgba(hex, alpha) {
        try {
            const r = parseInt(hex.slice(1,3), 16);
            const g = parseInt(hex.slice(3,5), 16);
            const b = parseInt(hex.slice(5,7), 16);
            return `rgba(${r},${g},${b},${alpha})`;
        } catch(e) { return `rgba(74,144,217,${alpha})`; }
    }

    destroy(chartId) {
        if (this.instances[chartId]) {
            try { this.instances[chartId].destroy(); } catch (_) {}
            delete this.instances[chartId];
        }
        // Remove document-level filter listeners registered for this chart
        // so deleted charts don't keep handling cross-filter / filter-panel
        // events (memory leak + potential errors operating on disposed instance).
        if (this._crossFilterListener && this._crossFilterListener[chartId]) {
            try { document.removeEventListener('crossfilter:change', this._crossFilterListener[chartId]); } catch (_) {}
            delete this._crossFilterListener[chartId];
        }
        if (this._filterPanelListener && this._filterPanelListener[chartId]) {
            try { document.removeEventListener('filters:change', this._filterPanelListener[chartId]); } catch (_) {}
            delete this._filterPanelListener[chartId];
        }
    }

    async render(chartDef, containerEl) {
        this.destroy(chartDef.id);

        if (window.__chartRendererDebug) {
            console.log('[ChartRender] start', {
                id: chartDef && chartDef.id,
                chartType: chartDef && chartDef.chartType,
                title: chartDef && chartDef.title
            });
        }

        // Accept either a <canvas> (legacy call) or a wrapper div.
        // For ApexCharts we always use the wrapper div as the mount point.
        let wrap, canvasEl;
        if (containerEl && containerEl.tagName === 'CANVAS') {
            canvasEl = containerEl;
            wrap = containerEl.parentElement;
            // Hide the canvas — ApexCharts renders its own SVG into the div
            canvasEl.style.display = 'none';
        } else {
            wrap = containerEl;
            // Make sure there is no stale canvas blocking layout
            const staleCanvas = wrap ? wrap.querySelector('canvas') : null;
            if (staleCanvas) staleCanvas.style.display = 'none';
            canvasEl = null;
        }

        if (!wrap) return;

        // Remove stale overlays / skeletons
        const existingEmpty = wrap.querySelector('.chart-no-data-overlay');
        if (existingEmpty) existingEmpty.remove();
        const existingSkeleton = wrap.querySelector('.chart-loading-skeleton');
        if (existingSkeleton) existingSkeleton.remove();

        // Show loading skeleton while fetching
        const skeleton = document.createElement('div');
        skeleton.className = 'chart-loading-skeleton';
        wrap.appendChild(skeleton);

        let data;
        try { data = await this.fetchData(chartDef); }
        catch(e) { console.warn('Data fetch failed:', e); data = { labels: [], values: [] }; }

        // Remove skeleton
        const sk = wrap.querySelector('.chart-loading-skeleton');
        if (sk) sk.remove();

        // Show empty-data overlay when no data is available, OR when the
        // datasource returned a hard error (defensive — a partial labels/values
        // array can survive a failed fetch and would otherwise plot as bogus).
        const hasRawRows = Array.isArray(data.rawData) && data.rawData.length > 0;
        const skipNoData = chartDef.chartType === 'navigation';
        const noUsableData = !hasRawRows
            && (!data.labels || !data.labels.length)
            && (!data.values || !data.values.length);
        if (!skipNoData && (noUsableData || data.error)) {
            const noData = document.createElement('div');
            noData.className = 'chart-no-data-overlay';
            const errMsg = data.error
                ? `<i class="bi bi-exclamation-triangle" style="font-size:2rem;display:block;margin-bottom:8px;color:#dc3545"></i><span style="color:#dc3545;font-weight:500">Connection Error</span><br><span style="font-size:0.78rem;color:#6c757d;margin-top:4px;display:inline-block">${data.error.replace(/</g,'&lt;')}</span>`
                : '<i class="bi bi-inbox" style="font-size:2rem;display:block;margin-bottom:8px;"></i>No records returned on this query.';
            noData.innerHTML = '<div class="chart-no-data">' + errMsg + '</div>';
            wrap.appendChild(noData);
            // Even with no data, custom-rendered charts must keep listening
            // to crossfilter:change / filters:change so they can RECOVER when
            // a filter is cleared (or when a different filter brings data
            // back). Without this, a transient empty result permanently
            // disconnects the chart from the filter cascade.
            if (this._customRenderTypes.has(chartDef.chartType)) {
                this._attachCustomChartFilters(chartDef, wrap, data);
            }
            return;
        }

        // Custom chart types use their own HTML/canvas rendering (unchanged)
        if (this._customRenderTypes.has(chartDef.chartType)) {
            // Strip ALL prior renders — both custom and ApexCharts — otherwise
            // when a chart type changes from Apex to custom (e.g. bar → treemap)
            // the previous .apex-chart-wrap stays mounted behind the new custom
            // render and shows through as a "hidden chart behind it".
            wrap.querySelectorAll('.custom-chart-render, .apex-chart-wrap').forEach(n => n.remove());
            const div = document.createElement('div');
            div.className = 'custom-chart-render';
            div.style.cssText = 'position:absolute;inset:0;overflow:hidden;';
            wrap.appendChild(div);
            this.renderCustomChart(chartDef, div, data);
            // Custom-rendered charts (KPI, Card, MetricTile, Table, Treemap,
            // Heatmap, Sankey, Slicer, …) ALSO need to react to slicer / filter-panel
            // changes. The Apex path below registers its own listeners further
            // down, but this branch returns early — so without this call those
            // chart types would never participate in the filter cascade.
            this._attachCustomChartFilters(chartDef, wrap, data);
            return;
        }

        // CRITICAL: destroy the previous ApexCharts instance for this chart id
        // BEFORE removing its DOM. Leaving it alive keeps internal refs to nodes we
        // are about to detach; the next render() then calls getComputedStyle on a
        // detached element (falls back to Window) and blows up with:
        //   "Failed to execute 'getComputedStyle' on 'Window'"
        //   "Cannot read properties of undefined (reading 'colors')"
        const prevInst = this.instances[chartDef.id];
        if (prevInst) {
            try { prevInst.destroy(); } catch (_) {}
            delete this.instances[chartDef.id];
        }

        // Bump the render-generation token for this chart id. A newer render()
        // call will increment this again, and any still-pending _renderWhenReady
        // closure detects the mismatch and bails out — preventing ApexCharts from
        // mounting on a detached node (the root cause of the
        // "getComputedStyle on Window" and "undefined.colors" errors).
        this._renderSeq = this._renderSeq || {};
        const mySeq = (this._renderSeq[chartDef.id] = (this._renderSeq[chartDef.id] || 0) + 1);

        // Remove any previous ApexCharts mount and custom render. Use
        // querySelectorAll so racing re-renders (theme switches / rapid resize)
        // never leave stale .apex-chart-wrap or .custom-chart-render layers
        // behind the new chart.
        wrap.querySelectorAll('.apex-chart-wrap, .custom-chart-render').forEach(n => n.remove());

        // Create a fresh div for ApexCharts
        const apexDiv = document.createElement('div');
        apexDiv.className = 'apex-chart-wrap';
        apexDiv.style.cssText = 'width:100%;height:100%;position:relative;';
        wrap.appendChild(apexDiv);

        const options = this.buildApexConfig(chartDef, data);

        // Defensive: ApexCharts throws "Cannot read properties of undefined (reading 'colors')"
        // if options.colors is missing or empty on certain chart types, so guarantee a palette.
        if (!Array.isArray(options.colors) || options.colors.length === 0) {
            options.colors = this.colorPalettes.default.slice();
        }

        // Wire up cross-filter click events via ApexCharts events.
        // Resolve the actual SQL column name. When mapping.labelField is empty
        // (legacy chart, AI-built chart with missing mapping, etc.) the literal
        // string "label" used to leak into CrossFilter.apply, which then built
        // WHERE clauses like [label] = 'Europe' — invalid because no real
        // table has a column called "label". Fall back to the first key of
        // rawData (the real column name returned by the executed query) so
        // downstream filterWhere fragments use [Region] = 'Europe'.
        let labelField = chartDef.mapping?.labelField;
        if (!labelField && Array.isArray(data.rawData) && data.rawData.length > 0) {
            labelField = Object.keys(data.rawData[0])[0] || '';
        }
        if (!labelField) labelField = 'label';
        if (!options.chart) options.chart = {};
        if (!options.chart.events) options.chart.events = {};
        const _onDataPointSelection = (event, chartCtx, config) => {
            const labels = options.xaxis?.categories || options.labels || [];
            const label = labels[config.dataPointIndex];
            if (label !== undefined && window.CrossFilter) {
                const af = window.CrossFilter.activeFilter;
                // Toggle-clear only when the SAME chart re-selects the SAME value.
                if (af && af.sourceChartId === chartDef.id && String(af.value) === String(label)) {
                    window.CrossFilter.clear();
                } else {
                    const sourceRow = (data.rawData && data.rawData[config.dataPointIndex]) || null;
                    window.CrossFilter.apply(labelField, label, label, {
                        sourceChartId: chartDef.id,
                        sourceDatasetName: chartDef.datasetName,
                        sourceDatasourceId: chartDef.datasourceId || window.currentDatasourceId || null,
                        sourceRow
                    });
                }
            }
        };
        options.chart.events.dataPointSelection = _onDataPointSelection;

        // Wait until the host wrapper actually has layout. If any ancestor is
        // display:none or zero width, ApexCharts' computed-style probes collapse
        // to the Window object and throw.
        const _renderWhenReady = async (attempt = 0) => {
            const rect = wrap.getBoundingClientRect();
            if (window.__chartRendererDebug) {
                console.log('[ChartRender] ready-check', {
                    chartId: chartDef.id,
                    chartType: chartDef.chartType,
                    attempt,
                    rectW: Math.round(rect.width),
                    rectH: Math.round(rect.height),
                    hasOffsetParent: !!wrap.offsetParent,
                    apexConnected: apexDiv.isConnected,
                    seqMatch: this._renderSeq[chartDef.id] === mySeq
                });
            }
            if ((!wrap.offsetParent || rect.width < 10 || rect.height < 10) && attempt < 20) {
                return new Promise(resolve => {
                    requestAnimationFrame(() => requestAnimationFrame(() => {
                        _renderWhenReady(attempt + 1).then(resolve);
                    }));
                });
            }
            try {
                // Bail out if a newer render() has superseded this one, or if
                // our mount node was detached from the DOM while we were waiting.
                // Mounting ApexCharts on a detached element causes its internal
                // getComputedStyle() probes to collapse to Window and throw.
                if (this._renderSeq[chartDef.id] !== mySeq) return;
                if (!apexDiv.isConnected) return;
                // NOTE: do NOT JSON.parse(JSON.stringify(options)) here — it drops
                // formatter functions (yaxis labels, tooltip.custom, radialBar value
                // formatter, etc.) and ApexCharts then throws:
                //   "Cannot read properties of undefined (reading 'call')"
                // and emits "Expected length, NaN" SVG warnings. Each render()
                // already produces a FRESH options via buildApexConfig(), so a
                // clone is unnecessary.
                // Preflight-sanitize to prevent ApexCharts' internal
                // "Cannot read properties of undefined (reading 'colors')" crash,
                // which happens when series/colors/labels arrays are malformed
                // (sparse, empty, or with undefined entries).
                this._sanitizeApexOptions(options);
                // Force explicit pixel dimensions so ApexCharts never resolves
                // '100%' against a zero/unset-height ancestor — that produces
                // NaN in SVG width/height/foreignObject attributes and triggers
                // the "getComputedStyle on Window" crash during layout probes.
                options.chart = options.chart || {};
                const _w = Math.max(10, Math.round(rect.width));
                const _h = Math.max(10, Math.round(rect.height));
                options.chart.width = _w;
                options.chart.height = _h;
                if (window.__chartRendererDebug) {
                    try {
                        console.log('[ChartRender] mounting', {
                            id: chartDef.id,
                            chartType: chartDef.chartType,
                            chartConfig: options.chart,
                            seriesSample: Array.isArray(options.series)
                                ? options.series.slice(0, 2)
                                : options.series,
                            seriesLen: Array.isArray(options.series) ? options.series.length : 'n/a',
                            labels: options.labels,
                            colors: options.colors,
                            stroke: options.stroke,
                            fill: options.fill,
                            plotOptions: options.plotOptions,
                            theme: options.theme,
                            xaxisCategoriesSample: options.xaxis && options.xaxis.categories
                                ? options.xaxis.categories.slice(0, 5)
                                : undefined,
                            optionKeys: Object.keys(options)
                        });
                    } catch (_) { /* logging must never throw */ }
                }
                const chart = new ApexCharts(apexDiv, options);
                await chart.render();
                // After the async render, re-check — if a newer render started
                // while we were rendering, dispose this instance instead of
                // storing it (otherwise we'd leak and paint stale data).
                if (this._renderSeq[chartDef.id] !== mySeq || !apexDiv.isConnected) {
                    try { chart.destroy(); } catch (_) {}
                    return;
                }
                this.instances[chartDef.id] = chart;
            } catch (e) {
                // Classify: transient DOM races retry; structural options errors
                // (the "reading 'colors'" / "reading 'w'" / "reading 'call'"
                // family) will NEVER succeed on retry — fail fast and paint the
                // fallback so we don't spam the console with 25 identical errors.
                const msg = String((e && e.message) || e || '');
                const isStructural =
                    msg.includes("reading 'colors'") ||
                    msg.includes("reading 'w'") ||
                    msg.includes("reading 'call'") ||
                    msg.includes('getComputedStyle');
                if (!isStructural && attempt < 25) {
                    await new Promise(r => setTimeout(r, 120));
                    if (this._renderSeq[chartDef.id] !== mySeq || !apexDiv.isConnected) return;
                    return _renderWhenReady(attempt + 5);
                }
                // Demoted to debug so it doesn't pollute the console. The
                // fallback UI below already tells the user what to do.
                if (window.__chartRendererDebug) {
                    console.warn('[ChartRender] fallback painted', {
                        chartId: chartDef.id,
                        chartType: chartDef.chartType,
                        attempt,
                        isStructural,
                        errorMsg: msg,
                        rectW: Math.round(rect.width),
                        rectH: Math.round(rect.height),
                        optionKeys: Object.keys(options || {}),
                        seriesLen: Array.isArray(options.series) ? options.series.length : 'n/a',
                        labelsLen: Array.isArray(options.labels) ? options.labels.length : 'n/a',
                        colorsLen: Array.isArray(options.colors) ? options.colors.length : 'n/a',
                        error: e
                    });
                }
                // Render a readable fallback so the panel doesn't stay blank.
                apexDiv.innerHTML =
                    '<div class="chart-no-data" style="padding:1rem;color:#6c757d">' +
                    '<i class="bi bi-exclamation-triangle me-1"></i>' +
                    'Unable to render chart — please try a different type or refresh.' +
                    '</div>';
            }
        };
        await _renderWhenReady();

        // Helper: push updated data into the existing ApexCharts instance
        const _updateApex = (inst, newLabels, newValues) => {
            const apexType = inst.w?.config?.chart?.type || 'bar';
            if (['pie', 'donut', 'polarArea'].includes(apexType)) {
                inst.updateOptions({ labels: newLabels }, false, false, false);
                inst.updateSeries(newValues);
            } else {
                const sName = (inst.w?.globals?.seriesNames || [])[0] || 'Data';
                inst.updateOptions({ xaxis: { categories: newLabels } }, false, false, false);
                inst.updateSeries([{ name: sName, data: newValues }]);
            }
        };

        // Cross-filter listener
        this._crossFilterListener = this._crossFilterListener || {};
        if (this._crossFilterListener[chartDef.id]) {
            document.removeEventListener('crossfilter:change', this._crossFilterListener[chartDef.id]);
        }
        this._crossFilterListener[chartDef.id] = (e) => {
            const filter = e.detail;
            const inst = this.instances[chartDef.id];
            if (!inst) return;

            // Stash the original filterWhere on first invocation so we can
            // restore it when the cross-filter is cleared.
            if (chartDef._origFilterWhere === undefined) {
                chartDef._origFilterWhere = chartDef.filterWhere || '';
            }

            // Filter cleared
            if (!filter) {
                if (chartDef._cfServerApplied) {
                    chartDef.filterWhere = chartDef._origFilterWhere || '';
                    chartDef._cfServerApplied = false;
                    this.render(chartDef, wrap);
                    return;
                }
                _updateApex(inst, data.labels || [], data.values || []);
                return;
            }

            // SOURCE chart: don't mutate or refilter its own data — keep all
            // bars visible so the user sees the clicked category in context
            // (ApexCharts already paints the selection state on the clicked
            // point).
            if (filter.sourceChartId && filter.sourceChartId === chartDef.id) {
                return;
            }

            // Power BI HIGHLIGHT MODE (UI parity):
            // Related charts keep their FULL original data on screen. The
            // global `.cf-dimmed` class on `.chart-card` (added by the
            // canvasManager listener) already provides the visual cue that
            // these charts are related-but-not-the-source. Mutating their
            // data to the filtered subset (previous behaviour) made charts
            // shrink to one or two bars and looked broken. We intentionally
            // do NOTHING here for related charts.
            //
            // If a previous run had pushed a server-side WHERE into this
            // chart, restore its original filter so it shows full data again.
            if (chartDef._cfServerApplied) {
                chartDef.filterWhere = chartDef._origFilterWhere || '';
                chartDef._cfServerApplied = false;
                this.render(chartDef, wrap);
            }
        };
        document.addEventListener('crossfilter:change', this._crossFilterListener[chartDef.id]);

        // Filter-panel listener
        this._filterPanelListener = this._filterPanelListener || {};
        if (this._filterPanelListener[chartDef.id]) {
            document.removeEventListener('filters:change', this._filterPanelListener[chartDef.id]);
        }
        this._filterPanelListener[chartDef.id] = () => {
            if (!window.filterPanel) return;
            const filters = filterPanel.getFiltersForChart(chartDef.id) || [];
            const inst = this.instances[chartDef.id];
            if (!inst) return;

            if (chartDef._origFilterWhere === undefined) {
                chartDef._origFilterWhere = chartDef.filterWhere || '';
            }

            // No filters — clear server-applied state if needed
            if (filters.length === 0) {
                if (chartDef._fpServerApplied) {
                    chartDef.filterWhere = chartDef._origFilterWhere || '';
                    chartDef._fpServerApplied = false;
                    this.render(chartDef, wrap);
                    return;
                }
                _updateApex(inst, data.labels || [], data.values || []);
                return;
            }

            const vField = chartDef.mapping?.valueField || 'value';

            // In-memory fast path: only when ALL filter fields exist in rawData
            if (data.rawData && data.rawData.length > 0) {
                const rowKeys = Object.keys(data.rawData[0]);
                const allApplicable = filters.every(f => {
                    const fld = f && (f.field || f.column || f.name);
                    if (!fld) return false;
                    return rowKeys.some(k => k.toLowerCase() === String(fld).toLowerCase());
                });
                if (allApplicable) {
                    const fData = FilterPanel.filterData(data.rawData, filters);
                    if (fData && fData.length > 0) {
                        const newLabels = fData.map(r => String(r[labelField] ?? ''));
                        const newValues = fData.map(r => parseFloat(r[vField]) || 0);
                        // If we'd previously pushed the filter to the server,
                        // restore filterWhere now that local can handle it.
                        if (chartDef._fpServerApplied) {
                            chartDef.filterWhere = chartDef._origFilterWhere || '';
                            chartDef._fpServerApplied = false;
                            this.render(chartDef, wrap);
                            return;
                        }
                        _updateApex(inst, newLabels, newValues);
                        return;
                    }
                }
            }

            // Server-refetch fallback for SQL-backed datasources
            if (this._isSqlBackedDatasource()) {
                const frags = filters.map(f => this._filterToWhereFragment(f)).filter(Boolean);
                if (frags.length > 0) {
                    const base = chartDef._origFilterWhere || '';
                    const combined = frags.map(f => `(${f})`).join(' AND ');
                    chartDef.filterWhere = base ? `(${base}) AND ${combined}` : combined;
                    chartDef._fpServerApplied = true;
                    this.render(chartDef, wrap);
                    return;
                }
            }

            // Last resort: leave chart visible with original data
            _updateApex(inst, data.labels || [], data.values || []);
        };
        document.addEventListener('filters:change', this._filterPanelListener[chartDef.id]);
    }

    // Register cross-filter and filter-panel listeners for custom-rendered
    // charts (KPI, Card, MetricTile, Table, Treemap, Heatmap, Sankey, Slicer,
    // Marimekko, Dumbbell, etc.). Custom renderers don't have an ApexCharts
    // instance to update incrementally, so we re-render the chart in place
    // after pushing the translated WHERE clause into chartDef.filterWhere.
    _attachCustomChartFilters(chartDef, wrap, data) {
        this._crossFilterListener = this._crossFilterListener || {};
        this._filterPanelListener = this._filterPanelListener || {};
        if (this._crossFilterListener[chartDef.id]) {
            document.removeEventListener('crossfilter:change', this._crossFilterListener[chartDef.id]);
        }
        if (this._filterPanelListener[chartDef.id]) {
            document.removeEventListener('filters:change', this._filterPanelListener[chartDef.id]);
        }
        if (chartDef._origFilterWhere === undefined) {
            chartDef._origFilterWhere = chartDef.filterWhere || '';
        }
        // Stash the unfiltered dataset so the in-memory fast path can re-derive
        // labels/values without a server round-trip on every cross-filter event.
        const origData = data && Array.isArray(data.rawData) && data.rawData.length > 0
            ? data
            : (chartDef._origData || data);
        if (origData && Array.isArray(origData.rawData) && origData.rawData.length > 0) {
            chartDef._origData = origData;
        }

        // In-memory re-render: filter rawData by (field,value) and redraw
        // the custom chart in place. Works for ALL datasource kinds (SQL,
        // REST API, File URL) and avoids a server round-trip.
        const _renderInMemory = (fField, fValue) => {
            const od = chartDef._origData;
            if (!od || !Array.isArray(od.rawData) || od.rawData.length === 0) return false;
            const rowKeys = Object.keys(od.rawData[0]);
            // null fField/fValue ⇒ restore original
            if (fField == null) {
                const div = wrap.querySelector('.custom-chart-render');
                if (!div) return false;
                this.renderCustomChart(chartDef, div, od);
                return true;
            }
            const resolved = rowKeys.find(k => k.toLowerCase() === String(fField).toLowerCase());
            if (!resolved) return false;
            const filteredRows = od.rawData.filter(r => String(r[resolved]) === String(fValue));
            if (filteredRows.length === 0) return false;
            const m = chartDef.mapping || {};
            const labelKey = rowKeys.find(k => k.toLowerCase() === String(m.labelField || '').toLowerCase()) || rowKeys[0];
            const valueKey = rowKeys.find(k => k.toLowerCase() === String(m.valueField || '').toLowerCase()) || rowKeys[1] || rowKeys[0];
            const filteredData = {
                ...od,
                labels: filteredRows.map(r => r[labelKey]),
                values: filteredRows.map(r => { const n = Number(r[valueKey]); return isNaN(n) ? r[valueKey] : n; }),
                rawData: filteredRows
            };
            const div = wrap.querySelector('.custom-chart-render');
            if (!div) return false;
            this.renderCustomChart(chartDef, div, filteredData);
            return true;
        };

        const cfHandler = (e) => {
            const filter = e.detail;
            // Self-fire guard: a slicer must not re-render itself when it
            // emits its own filter (would wipe its chip active state mid-click).
            if (filter && filter.sourceChartId === chartDef.id) return;

            // Filter cleared — restore original
            if (!filter) {
                if (chartDef._cfInMemoryApplied) {
                    if (_renderInMemory(null, null)) {
                        chartDef._cfInMemoryApplied = false;
                        return;
                    }
                }
                if (chartDef._cfServerApplied) {
                    chartDef.filterWhere = chartDef._origFilterWhere || '';
                    chartDef._cfServerApplied = false;
                    this.render(chartDef, wrap);
                }
                return;
            }

            // SOURCE chart: don't refilter itself — keep its full data so the
            // user sees the click in context (Power BI parity).
            if (filter.sourceChartId && filter.sourceChartId === chartDef.id) {
                return;
            }

            // Power BI HIGHLIGHT MODE (UI parity):
            // Related charts keep their full original data; the .cf-dimmed
            // class on `.chart-card` is the only visual signal of relation.
            // No data mutation, no server refetch. If a previous run pushed
            // an in-memory or server filter, restore the original view.
            if (chartDef._cfInMemoryApplied) {
                if (_renderInMemory(null, null)) chartDef._cfInMemoryApplied = false;
            }
            if (chartDef._cfServerApplied) {
                chartDef.filterWhere = chartDef._origFilterWhere || '';
                chartDef._cfServerApplied = false;
                this.render(chartDef, wrap);
            }
        };

        const fpHandler = () => {
            if (!window.filterPanel) return;
            const filters = filterPanel.getFiltersForChart(chartDef.id) || [];
            if (filters.length === 0) {
                if (chartDef._fpServerApplied) {
                    chartDef.filterWhere = chartDef._origFilterWhere || '';
                    chartDef._fpServerApplied = false;
                    this.render(chartDef, wrap);
                }
                return;
            }
            if (this._isSqlBackedDatasource()) {
                const frags = filters.map(f => this._filterToWhereFragment(f)).filter(Boolean);
                if (frags.length > 0) {
                    const base = chartDef._origFilterWhere || '';
                    const combined = frags.map(f => `(${f})`).join(' AND ');
                    chartDef.filterWhere = base ? `(${base}) AND ${combined}` : combined;
                    chartDef._fpServerApplied = true;
                    this.render(chartDef, wrap);
                }
            }
        };

        this._crossFilterListener[chartDef.id] = cfHandler;
        this._filterPanelListener[chartDef.id] = fpHandler;
        document.addEventListener('crossfilter:change', cfHandler);
        document.addEventListener('filters:change', fpHandler);
    }

    // SQL-backed datasource = anything that supports SQL WHERE pushdown
    // (i.e. not Power BI / DAX, not REST API, not File URL).
    _isSqlBackedDatasource() {
        return !this._isPowerBi() && !this._isRestApi() && !this._isFileUrl();
    }

    _sqlEscape(v) {
        if (v === null || v === undefined) return 'NULL';
        if (typeof v === 'number' && isFinite(v)) return String(v);
        if (typeof v === 'boolean') return v ? '1' : '0';
        return "'" + String(v).replace(/'/g, "''") + "'";
    }

    // Cross-DB identifier quoting. SQL Server uses [name], PostgreSQL/Oracle
    // use "name", MySQL/MariaDB use `name`. Falls back to SQL Server style
    // when the datasource type is unknown (matches the legacy behaviour).
    _quoteIdentifier(name) {
        const n = String(name);
        const dsType = (window.currentDatasourceType || '').toLowerCase();
        if (dsType.includes('postgres') || dsType.includes('oracle')) {
            return '"' + n.replace(/"/g, '""') + '"';
        }
        if (dsType.includes('mysql') || dsType.includes('mariadb')) {
            return '`' + n.replace(/`/g, '``') + '`';
        }
        return '[' + n.replace(/]/g, ']]') + ']';
    }

    _cfWhereFragment(field, value) {
        if (!field) return '';
        const safeField = this._quoteIdentifier(field);
        if (value === null || value === undefined) return `${safeField} IS NULL`;
        return `${safeField} = ${this._sqlEscape(value)}`;
    }

    _filterToWhereFragment(f) {
        if (!f) return '';
        const field = f.field || f.column || f.name;
        if (!field) return '';
        const safeField = this._quoteIdentifier(field);
        const op = String(f.op || f.operator || 'eq').toLowerCase();
        const v = f.value;
        switch (op) {
            case 'eq': case '=': case 'equals':  return `${safeField} = ${this._sqlEscape(v)}`;
            case 'ne': case '!=': case '<>': case 'notequals': return `${safeField} <> ${this._sqlEscape(v)}`;
            case 'gt': case '>':  return `${safeField} > ${this._sqlEscape(v)}`;
            case 'gte': case '>=': return `${safeField} >= ${this._sqlEscape(v)}`;
            case 'lt': case '<':  return `${safeField} < ${this._sqlEscape(v)}`;
            case 'lte': case '<=': return `${safeField} <= ${this._sqlEscape(v)}`;
            case 'contains':   return `${safeField} LIKE ${this._sqlEscape('%' + v + '%')}`;
            case 'startswith': return `${safeField} LIKE ${this._sqlEscape(v + '%')}`;
            case 'endswith':   return `${safeField} LIKE ${this._sqlEscape('%' + v)}`;
            case 'in': {
                const vals = Array.isArray(f.values) ? f.values : (Array.isArray(v) ? v : [v]);
                if (!vals.length) return '';
                return `${safeField} IN (${vals.map(x => this._sqlEscape(x)).join(', ')})`;
            }
            case 'between': {
                const vals = Array.isArray(f.values) ? f.values : (Array.isArray(v) ? v : []);
                if (vals.length < 2) return '';
                return `${safeField} BETWEEN ${this._sqlEscape(vals[0])} AND ${this._sqlEscape(vals[1])}`;
            }
            case 'isnull':    return `${safeField} IS NULL`;
            case 'isnotnull': return `${safeField} IS NOT NULL`;
            default: return `${safeField} = ${this._sqlEscape(v)}`;
        }
    }

    // ── Power BI detection helper ──
    _isPowerBi() {
        var dsType = (window.currentDatasourceType || '').toLowerCase();
        return dsType.indexOf('power bi') !== -1 || dsType.indexOf('powerbi') !== -1;
    }

    // ── REST API detection helper ──
    _isRestApi() {
        var dsType = (window.currentDatasourceType || '').toLowerCase();
        return dsType.indexOf('rest api') !== -1 || dsType.indexOf('restapi') !== -1;
    }

    // ── File URL detection helper ──
    // File URL datasources (CSV/XLSX) are parsed by the server which returns
    // ALL rows raw — any GROUP BY in the SQL is ignored — so we must aggregate
    // client-side just like REST API.
    _isFileUrl() {
        var dsType = (window.currentDatasourceType || '').toLowerCase();
        return dsType.indexOf('file url') !== -1 || dsType.indexOf('fileurl') !== -1;
    }

    // Aggregate raw rows in JavaScript — used for File URL and REST API
    // datasources, which return un-aggregated rows from the server. Groups by
    // labelField, applies aggFn to valueField (and per-field agg to multi-value
    // fields), sorts by aggregated primary value desc, and clamps to rowLimit.
    _aggregateRowsClientSide(rows, labelField, valueField, aggFn, mvFields, mvFieldAggs, rowLimit) {
        if (!Array.isArray(rows) || rows.length === 0) return rows || [];
        if (!labelField || !valueField) return rows;
        if (!aggFn || aggFn === 'None') return rows;

        // Resolve actual keys case-insensitively against the first row.
        const sampleKeys = Object.keys(rows[0]);
        const findKey = (n) => n && (sampleKeys.find(k => k.toLowerCase() === String(n).toLowerCase()) || n);
        const lk = findKey(labelField);
        const vk = findKey(valueField);
        const mvk = (mvFields || []).map(findKey);

        const apply = (fn, vals) => {
            const nums = vals.map(v => parseFloat(v)).filter(n => !isNaN(n));
            switch ((fn || 'SUM').toUpperCase()) {
                case 'SUM':            return nums.reduce((a, b) => a + b, 0);
                case 'AVG':            return nums.length ? nums.reduce((a, b) => a + b, 0) / nums.length : 0;
                case 'MIN':            return nums.length ? Math.min.apply(null, nums) : 0;
                case 'MAX':            return nums.length ? Math.max.apply(null, nums) : 0;
                case 'COUNT':          return vals.filter(v => v !== null && v !== undefined && v !== '').length;
                case 'COUNT_DISTINCT': return new Set(vals.map(String)).size;
                case 'MEDIAN': {
                    const s = nums.slice().sort((a, b) => a - b);
                    if (s.length === 0) return 0;
                    const m = Math.floor(s.length / 2);
                    return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
                }
                case 'STDEV':
                case 'VAR': {
                    if (nums.length < 2) return 0;
                    const mean = nums.reduce((a, b) => a + b, 0) / nums.length;
                    const variance = nums.reduce((a, b) => a + (b - mean) * (b - mean), 0) / (nums.length - 1);
                    return fn.toUpperCase() === 'VAR' ? variance : Math.sqrt(variance);
                }
                default: return nums.reduce((a, b) => a + b, 0);
            }
        };

        const groups = new Map();
        rows.forEach(r => {
            const key = String(r[lk] ?? '');
            if (!groups.has(key)) groups.set(key, []);
            groups.get(key).push(r);
        });

        const out = [];
        groups.forEach((grp, key) => {
            const obj = {};
            obj[lk] = key;
            obj[vk] = apply(aggFn, grp.map(r => r[vk]));
            mvk.forEach((m, i) => {
                if (m && m !== vk) obj[m] = apply((mvFieldAggs && mvFieldAggs[i]) || 'SUM', grp.map(r => r[m]));
            });
            out.push(obj);
        });

        out.sort((a, b) => (parseFloat(b[vk]) || 0) - (parseFloat(a[vk]) || 0));
        return rowLimit > 0 ? out.slice(0, rowLimit) : out;
    }

    // ── DAX table name: single-quoted for Power BI ──
    _formatDaxTableName(tableName) {
        return "'" + String(tableName).replace(/'/g, "''") + "'";
    }

    // ── Build DAX query for Power BI datasources ──
    _buildDaxQuery(tableName, limit, labelField, valueField, agg, mvFields) {
        var tbl = this._formatDaxTableName(tableName);
        if (agg && agg.enabled && labelField && valueField) {
            var aggFn = agg.function || 'SUM';
            var mvCols = '';
            if (mvFields && mvFields.length > 0) {
                mvCols = mvFields.filter(function (f) { return f !== valueField; })
                    .map(function (f) { return ', "' + f + '", ' + aggFn + '(' + tbl + '[' + f + '])'; }).join('');
            }
            return 'EVALUATE TOPN(' + limit + ', SUMMARIZECOLUMNS(' + tbl + '[' + labelField + '], "' + valueField + '", ' + aggFn + '(' + tbl + '[' + valueField + '])' + mvCols + '))';
        }
        if (labelField && valueField) {
            var mvCols2 = '';
            if (mvFields && mvFields.length > 0) {
                mvCols2 = mvFields.filter(function (f) { return f !== valueField; })
                    .map(function (f) { return ', "' + f + '", ' + tbl + '[' + f + ']'; }).join('');
            }
            return 'EVALUATE TOPN(' + limit + ', SELECTCOLUMNS(' + tbl + ', "' + labelField + '", ' + tbl + '[' + labelField + '], "' + valueField + '", ' + tbl + '[' + valueField + ']' + mvCols2 + '))';
        }
        return 'EVALUATE TOPN(' + limit + ', ' + tbl + ')';
    }

    _formatTableName(tableName) {
        if (!tableName) return '[]';
        // Defensive: extract string from object (e.g. {name:'TableName'} from API)
        if (typeof tableName === 'object') {
            tableName = tableName.name || tableName.Name || String(tableName);
        }
        tableName = String(tableName);
        if (tableName.includes('.')) {
            return tableName.split('.').map(part => `[${part}]`).join('.');
        }
        return `[${tableName}]`;
    }

    _buildLimitQuery(columns, tableName, limit = 15, whereClause = '') {
        // Power BI — generate DAX instead of SQL
        if (this._isPowerBi()) {
            return this._buildDaxQuery(tableName, limit, null, null, null, null);
        }
        // SQL datasources — unchanged
        const dsType = (window.currentDatasourceType || '').toLowerCase();
        const isSqlServer = dsType.includes('sql server') || dsType.includes('sqlserver') || dsType.includes('mssql');
        const cols = columns || '*';
        const fmtTable = this._formatTableName(tableName);
        const where = whereClause ? ` WHERE ${whereClause}` : '';
        if (isSqlServer) {
            return `SELECT TOP ${limit} ${cols} FROM ${fmtTable}${where}`;
        }
        return `SELECT ${cols} FROM ${fmtTable}${where} LIMIT ${limit}`;
    }

    // Resolve the data-execute endpoint. When the report viewer is rendering a
    // Published report for an anonymous viewer (public link / iframe embed),
    // /api/data/execute is gated by [Authorize] and would 302 to login, so we
    // route through the per-report public endpoint instead.
    _chartDataUrl() {
        if (typeof window !== 'undefined' && window._rvIsPublic && window._rvReportGuid) {
            return '/api/reports/public/' + encodeURIComponent(window._rvReportGuid) + '/data';
        }
        return '/api/data/execute';
    }

    // Headers for chart data requests. In public mode the per-report endpoint
    // requires a signed embed token — preferred via Authorization header so it
    // never lands in browser history / referer logs. Falls back to whatever
    // ?t=… is already in the page URL when reload-on-401 hasn't happened yet.
    _chartDataHeaders() {
        const h = { 'Content-Type': 'application/json' };
        if (typeof window !== 'undefined' && window._rvIsPublic && window._rvEmbedToken) {
            h['Authorization'] = 'Bearer ' + window._rvEmbedToken;
        }
        return h;
    }

    /**
     * Lazily fetch and cache the column list for a given table on a datasource.
     * Used by the agg query builder to drop/replace mapping fields that don't
     * exist on the active table (e.g. stale KPI 'Value' alias, cross-table
     * drag-drop remnants) before they poison SQL with "Invalid column name".
     */
    async _getTableColumns(dsId, tableName) {
        if (!dsId || !tableName) return [];
        this._schemaCache = this._schemaCache || {};
        if (!this._schemaCache[dsId]) {
            try {
                const r = await fetch('/api/datasources/' + dsId + '/schema');
                this._schemaCache[dsId] = r.ok ? await r.json() : { tables: [] };
            } catch { this._schemaCache[dsId] = { tables: [] }; }
        }
        // Strip identifier brackets/quotes so '[SalesLT].[ProductModel]' or
        // '"dbo"."Product"' match the schema's plain 'SalesLT.ProductModel'.
        const stripBrackets = (s) => String(s || '').replace(/[\[\]"`]/g, '').trim();
        const tn = stripBrackets(tableName).toLowerCase();
        const matchTable = (tables) => (tables || []).find(t => {
            const n = stripBrackets(t.name).toLowerCase();
            return n === tn || n.endsWith('.' + tn) || tn.endsWith('.' + n);
        });
        let tbl = matchTable(this._schemaCache[dsId].tables);
        // Fallback: reuse the schema the properties panel already loaded.
        // Its loadFields() fetches the same endpoint and stores the parsed
        // shape on _schemaTables; using it avoids a race where this lookup
        // runs before our own fetch resolves and misses the validation pass.
        if ((!tbl || !(tbl.columns || []).length) && window.propertiesPanel?._schemaTables) {
            tbl = matchTable(window.propertiesPanel._schemaTables) || tbl;
        }
        return tbl?.columns || [];
    }

    async fetchData(chartDef) {
        if (chartDef.customJsonData) {
            try {
                const parsed = JSON.parse(chartDef.customJsonData);
                // Only use customJsonData if it has real labels (not the old-bug "undefined" strings)
                if (parsed &&
                    Array.isArray(parsed.labels) && parsed.labels.length > 0 &&
                    Array.isArray(parsed.values) && parsed.values.length > 0 &&
                    parsed.labels.some(function (l) { return l !== '' && l !== 'undefined'; })) {
                    return parsed;
                }
            } catch(e) {}
        }
        const mapping = chartDef.mapping || {};
        const agg = chartDef.aggregation || {};
        // NOTE: declared as `let` (not `const`) — the schema-validation block
        // below reassigns these when the saved mapping references columns
        // that don't exist on the active table (e.g. stale KPI alias 'Value'
        // carried into a bar chart). With `const`, the reassignment throws
        // TypeError, gets swallowed by the surrounding try/catch, and the
        // bad column name leaks into SQL like
        //   SELECT TOP 15 [Value], SUM([Value]) AS [Value] FROM [SalesLT].[Product] GROUP BY [Value]
        // → "Invalid column name 'Value'".
        let labelField = mapping.labelField || '';
        let valueField = mapping.valueField || '';

        // ── Real datasource path ──
        const dsId = chartDef.datasourceId || window.currentDatasourceId || null;

        // ── REST API & File URL path ──
        // Both datasource types return all rows un-aggregated from the server
        // (the server ignores any SQL/DAX for them), so we fetch the raw rows
        // and aggregate them client-side using the chart's labelField/valueField
        // and aggregation function — otherwise the chart plots one point per
        // row and dimension labels appear duplicated (e.g. "Germany" 30×).
        if (dsId && (this._isRestApi() || this._isFileUrl())) {
            const isRest = this._isRestApi();
            const sentinelQuery = isRest ? 'REST_API' : 'FILE_URL';
            try {
                const r = await fetch(this._chartDataUrl(), {
                    method: 'POST',
                    headers: this._chartDataHeaders(),
                    body: JSON.stringify({ query: sentinelQuery, datasourceId: dsId })
                });
                const result = await r.json();
                if (result.success && result.data && result.data.length > 0) {
                    const keys = Object.keys(result.data[0]);
                    const resolvedLabel = (labelField && keys.find(k => k.toLowerCase() === labelField.toLowerCase())) || keys.find(k => typeof result.data[0][k] === 'string') || keys[0];
                    const resolvedValue = (valueField && keys.find(k => k.toLowerCase() === valueField.toLowerCase())) || keys.find(k => typeof result.data[0][k] === 'number') || keys[1] || keys[0];
                    // Aggregate client-side by the dimension. Mirrors the SQL
                    // GROUP BY path that other datasources get for free.
                    const mvFieldsRaw = (chartDef.mapping?.multiValueFields || []).filter(Boolean);
                    const mvFields = mvFieldsRaw.map(f => typeof f === 'string' ? f : (f.field || ''));
                    const mvFieldAggs = mvFieldsRaw.map(f => typeof f === 'object' ? (f.agg || 'SUM') : 'SUM');
                    const primaryAgg = chartDef.mapping?.valueFieldAgg || agg.function || 'SUM';
                    const rowLimit = chartDef.rowLimit || 15;
                    const aggregatedRows = (chartDef.chartType === 'table' || primaryAgg === 'None')
                        ? result.data
                        : this._aggregateRowsClientSide(result.data, resolvedLabel, resolvedValue, primaryAgg, mvFields, mvFieldAggs, rowLimit);
                    const out = {
                        labels: aggregatedRows.map(r => String(r[resolvedLabel] ?? '')),
                        values: aggregatedRows.map(r => parseFloat(r[resolvedValue]) || 0),
                        rawData: aggregatedRows
                    };
                    if (mvFields.length > 0) {
                        out.multiValues = mvFields.map(f => {
                            const rk = keys.find(k => k.toLowerCase() === f.toLowerCase()) || f;
                            return { field: rk, values: aggregatedRows.map(r => parseFloat(r[rk]) || 0) };
                        });
                    }
                    return out;
                }
                return { labels: [], values: [], error: result.error || (isRest ? 'REST API returned no data.' : 'File returned no data.') };
            } catch(e) {
                console.warn((isRest ? 'REST API' : 'File URL') + ' data fetch failed:', e);
                return { labels: [], values: [], error: e.message || (isRest ? 'REST API request failed.' : 'File URL request failed.') };
            }
        }

        // ── Table-based path (PRIMARY): always build query from table + current filterWhere/rowLimit ──
        let lastFetchError = null;
        if (dsId && chartDef.datasetName) {
            try {
                let tableName = chartDef.datasetName;
                if (typeof tableName === 'object') {
                    tableName = tableName.name || tableName.Name || '';
                }
                tableName = String(tableName);
                if (!tableName || tableName === '[object Object]') {
                    throw new Error('Invalid table name');
                }
                const fmtTable = this._formatTableName(tableName);
                const rowLimit = chartDef.rowLimit || 15;
                const whereClause = chartDef.filterWhere || '';
                const mvFieldsRaw = (chartDef.mapping?.multiValueFields || []).filter(Boolean);
                // Normalize multi-value fields: support both string[] and {field,agg}[]
                const mvFields = mvFieldsRaw.map(f => typeof f === 'string' ? f : (f.field || ''));
                const mvFieldAggs = mvFieldsRaw.map(f => typeof f === 'object' ? (f.agg || 'SUM') : 'SUM');
                // Per-field aggregation for primary value field
                const primaryAgg = chartDef.mapping?.valueFieldAgg || agg.function || 'SUM';
                let query;

                // ── Power BI → DAX (parallel path, SQL untouched) ──
                if (this._isPowerBi()) {
                    query = this._buildDaxQuery(tableName, rowLimit, labelField, valueField, agg, mvFields);
                } else {
                    // ── SQL datasources ──
                    const whereSQL = whereClause ? ` WHERE ${whereClause}` : '';
                    const dsType = (window.currentDatasourceType || '').toLowerCase();
                    const isSqlServer = dsType.includes('sql server') || dsType.includes('sqlserver') || dsType.includes('mssql');
                    // Schema-validation guard: drop label/value fields that don't
                    // exist on the active table and auto-pick replacements from the
                    // real schema. Without this, stale KPI aliases ('Value') or
                    // cross-table drag remnants ('Id') produce queries like
                    // SELECT TOP 15 [Id], COUNT([Value]) ... FROM [SalesLT].[Product]
                    // → "Invalid column name 'Id'/'Value'" SQL errors.
                    // Track whether the schema validator could verify columns.
                    // When schema is unavailable we MUST NOT emit an agg query that
                    // references possibly-stale fields ([Id]/[Value] from a prior
                    // KPI mapping) — we'll fall through to a safe SELECT * instead.
                    let schemaVerified = false;
                    let validMvFields = mvFields.slice();
                    let validMvAggs   = mvFieldAggs.slice();
                    try {
                        const schemaCols = await this._getTableColumns(dsId, tableName);
                        if (schemaCols.length > 0) {
                            schemaVerified = true;
                            const has = (n) => n && schemaCols.some(c => String(c.name).toLowerCase() === String(n).toLowerCase());
                            const resolveCase = (n) => {
                                if (!n) return n;
                                const m = schemaCols.find(c => String(c.name).toLowerCase() === String(n).toLowerCase());
                                return m ? m.name : n;
                            };
                            const isNum = (c) => /int|decimal|float|numeric|money|double|real/i.test(c.dataType || '');
                            if (labelField && !has(labelField)) {
                                const pick = schemaCols.find(c => !isNum(c) && !c.isPrimaryKey) || schemaCols[0];
                                console.warn(`[chartRenderer] labelField "${labelField}" not on [${tableName}] — using "${pick?.name}"`);
                                labelField = pick?.name || labelField;
                            } else if (labelField) {
                                labelField = resolveCase(labelField);
                            }
                            if (valueField && !has(valueField)) {
                                const pick = schemaCols.find(isNum) || schemaCols.find(c => !c.isPrimaryKey) || schemaCols[0];
                                console.warn(`[chartRenderer] valueField "${valueField}" not on [${tableName}] — using "${pick?.name}"`);
                                valueField = pick?.name || valueField;
                            } else if (valueField) {
                                valueField = resolveCase(valueField);
                            }
                            // Drop mv fields that don't exist on the active table —
                            // referencing them would poison the agg SQL with the
                            // same "Invalid column name" error we're guarding against.
                            validMvFields = [];
                            validMvAggs   = [];
                            mvFields.forEach((f, i) => {
                                if (!f) return;
                                if (has(f)) {
                                    validMvFields.push(resolveCase(f));
                                    validMvAggs.push(mvFieldAggs[i] || 'SUM');
                                } else {
                                    console.warn(`[chartRenderer] multi-value field "${f}" not on [${tableName}] — dropped`);
                                }
                            });
                        }
                    } catch (e) { /* schema lookup is best-effort */ }
                    let selectedCols;
                    if (chartDef.chartType === 'table') {
                        selectedCols = '*';
                    } else if (labelField && valueField) {
                        const allCols = [`[${labelField}]`, `[${valueField}]`];
                        validMvFields.forEach(f => { if (f && f !== valueField) allCols.push(`[${f}]`); });
                        selectedCols = allCols.join(', ');
                    } else {
                        selectedCols = '*';
                    }
                    // Build per-field aggregated query when aggregation is enabled.
                    // Refuse to emit an agg query when schema couldn't be verified —
                    // the bindings may still reference columns that don't exist on
                    // the active table (the very root cause of the recurring
                    // "Invalid column name 'Value'/'Id'" failures). Falling back to
                    // SELECT * lets the row-resolver pick valid columns at runtime.
                    const useAgg = schemaVerified && primaryAgg !== 'None' && labelField && valueField && chartDef.chartType !== 'table';
                    if (useAgg) {
                        const aggExpr = (fn, col) => {
                            if (fn === 'COUNT_DISTINCT') return `COUNT(DISTINCT [${col}])`;
                            if (fn === 'MEDIAN') return `PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY [${col}])`;
                            return `${fn}([${col}])`;
                        };
                        let aggColParts = [`[${labelField}]`, `${aggExpr(primaryAgg, valueField)} as [${valueField}]`];
                        validMvFields.forEach((f, i) => {
                            if (f && f !== valueField) {
                                const mAgg = validMvAggs[i] || 'SUM';
                                aggColParts.push(`${aggExpr(mAgg, f)} as [${f}]`);
                            }
                        });
                        const aggCols = aggColParts.join(', ');
                        if (isSqlServer) {
                            query = `SELECT TOP ${rowLimit} ${aggCols} FROM ${fmtTable}${whereSQL} GROUP BY [${labelField}]`;
                        } else {
                            query = `SELECT ${aggCols} FROM ${fmtTable}${whereSQL} GROUP BY [${labelField}] LIMIT ${rowLimit}`;
                        }
                    } else {
                        query = this._buildLimitQuery(selectedCols, tableName, rowLimit, whereClause);
                    }
                }
                const r = await fetch(this._chartDataUrl(), {
                    method: 'POST',
                    headers: this._chartDataHeaders(),
                    body: JSON.stringify({ query: query, datasourceId: dsId })
                });
                const result = await r.json();
                if (result.success && result.data && result.data.length > 0) {
                    const keys = Object.keys(result.data[0]);
                    const resolvedLabel = (labelField && keys.find(k => k.toLowerCase() === labelField.toLowerCase())) || keys[0];
                    const resolvedValue = (valueField && keys.find(k => k.toLowerCase() === valueField.toLowerCase())) ||
                        keys.find(k => typeof result.data[0][k] === 'number') || keys[1] || keys[0];
                    const out = {
                        labels: result.data.map(r => String(r[resolvedLabel] ?? '')),
                        values: result.data.map(r => parseFloat(r[resolvedValue]) || 0),
                        rawData: result.data
                    };
                    // Extract additional value fields for multi-series
                    const mvf = (chartDef.mapping?.multiValueFields || []).filter(Boolean);
                    if (mvf.length > 0) {
                        out.multiValues = mvf.map(f => {
                            const rk = keys.find(k => k.toLowerCase() === f.toLowerCase()) || f;
                            return { field: rk, values: result.data.map(r => parseFloat(r[rk]) || 0) };
                        });
                    }
                    return out;
                }
                // Query failed — return error for REST API (no SQL fallback), retry SQL with SELECT *
                if (!result.success) {
                    const isRestApi = (window.currentDatasourceType || '').toLowerCase().replace(/\s+/g, '').includes('restapi');
                    if (isRestApi) {
                        return { labels: [], values: [], error: result.error || 'REST API request failed.' };
                    }
                    // SELECT * fallback. We deliberately drop the whereClause here:
                    // a stale cross-filter (e.g. "[Name] = 'foo'" pushed from another
                    // chart whose table HAS a Name column) can reference a column that
                    // doesn't exist on THIS table, and including it would cause the
                    // fallback to fail with the same "Invalid column name" error as
                    // the primary query — leaving the user staring at a Connection
                    // Error overlay even though the table itself is fine.
                    const fallbackQuery = this._buildLimitQuery('*', tableName, rowLimit, '');
                    const r2 = await fetch(this._chartDataUrl(), {
                        method: 'POST',
                        headers: this._chartDataHeaders(),
                        body: JSON.stringify({ query: fallbackQuery, datasourceId: dsId })
                    });
                    const result2 = await r2.json();
                    if (result2.success && result2.data && result2.data.length > 0) {
                        const keys2 = Object.keys(result2.data[0]);
                        // Prefer the user's mapping when those columns exist in the
                        // fallback result so the chart still honours the explicit
                        // labelField/valueField selections; only auto-pick when the
                        // user's chosen columns aren't present (e.g. typo, stale
                        // mapping pointing to a column that doesn't exist in this
                        // table).
                        const resolvedLabel2 = (labelField && keys2.find(k => k.toLowerCase() === labelField.toLowerCase()))
                            || keys2.find(k => typeof result2.data[0][k] === 'string') || keys2[0];
                        const resolvedValue2 = (valueField && keys2.find(k => k.toLowerCase() === valueField.toLowerCase()))
                            || keys2.find(k => typeof result2.data[0][k] === 'number') || keys2[1] || keys2[0];
                        return {
                            labels: result2.data.map(r => String(r[resolvedLabel2] ?? '')),
                            values: result2.data.map(r => parseFloat(r[resolvedValue2]) || 0),
                            rawData: result2.data
                        };
                    }
                    if (!result2.success) return { labels: [], values: [], error: (result2.error || result.error) + ' [executed: ' + query + ']' };
                }
            } catch(e) { console.warn('Datasource table fetch failed, falling back:', e); lastFetchError = e.message || 'Data fetch failed'; }
        }

        // ── DataQuery fallback: use pre-built query when no table name is available ──
        if (dsId && chartDef.dataQuery && !chartDef.dataQuery.includes('[object Object]')) {
            // Validate every [bracketed] identifier in the saved dataQuery
            // against the active table's real schema. Stale AI-generated SQL
            // (e.g. KPI's `SELECT COUNT(*) AS [Value] FROM ...` carried into a
            // bar chart, or a query whose column aliases like [Id]/[Value]
            // don't exist on the new table) would otherwise execute and fail
            // with "Invalid column name". When any referenced identifier is
            // unknown we skip dataQuery and rely on the upstream agg-path /
            // SELECT * fallback that already returned data above.
            try {
                const tn = chartDef.datasetName;
                if (tn) {
                    const schemaCols = await this._getTableColumns(dsId, tn);
                    if (schemaCols.length > 0) {
                        const known = new Set(schemaCols.map(c => String(c.name).toLowerCase()));
                        // Treat SQL keywords / function names as always-valid so we
                        // don't reject legitimate identifiers like AS, ON, AND, etc.
                        const sqlNoise = new Set(['value','as','on','and','or','from','where','group','by','order','having','asc','desc','top','distinct','select','inner','outer','left','right','join','case','when','then','else','end','null','is','not','in','like','between']);
                        const refs = [...chartDef.dataQuery.matchAll(/\[([^\]]+)\]/g)].map(m => m[1].toLowerCase());
                        const tableNameLower = String(tn).replace(/[\[\]"`]/g,'').toLowerCase();
                        const tableShort = tableNameLower.split('.').pop();
                        const bad = refs.filter(r => {
                            // Skip table names and any schema-qualifier segments.
                            if (r === tableNameLower || r === tableShort) return false;
                            if (tableNameLower.split('.').includes(r)) return false;
                            return !known.has(r);
                        });
                        if (bad.length > 0) {
                            console.warn(`[chartRenderer] dataQuery references unknown columns on [${tn}]: ${bad.join(', ')} — skipping stale dataQuery.`);
                            return { labels: [], values: [], error: lastFetchError || `Saved query references columns not on [${tn}]: ${bad.join(', ')}` };
                        }
                    }
                }
            } catch (e) { /* validation is best-effort */ }
            try {
                const r = await fetch(this._chartDataUrl(), {
                    method: 'POST',
                    headers: this._chartDataHeaders(),
                    body: JSON.stringify({ query: chartDef.dataQuery, datasourceId: dsId })
                });
                const result = await r.json();
                if (result.success && result.data && result.data.length > 0) {
                    const keys = Object.keys(result.data[0]);
                    const resolvedLabel = keys.find(k => k.toLowerCase() === labelField.toLowerCase()) || keys[0];
                    const resolvedValue = keys.find(k => k.toLowerCase() === valueField.toLowerCase()) ||
                        keys.find(k => typeof result.data[0][k] === 'number') || keys[1] || keys[0];
                    let rows = result.data;

                    // Auto-aggregate when the AI emits a non-grouped SELECT for a chart
                    // type that needs unique categories. Duplicate label rows would
                    // otherwise render as repeated x-axis ticks (e.g. "Mr., Ms., Mr.,
                    // Ms., …"). For categorical charts, group by label and SUM the
                    // numeric value so each category appears exactly once.
                    const ct = String(chartDef.chartType || '').toLowerCase();
                    const needsUniqueLabels = ['bar', 'column', 'pie', 'doughnut', 'donut', 'area', 'line'].includes(ct);
                    if (needsUniqueLabels && rows.length > 1) {
                        const seen = new Set();
                        let hasDup = false;
                        for (const r of rows) {
                            const k = String(r[resolvedLabel] ?? '');
                            if (seen.has(k)) { hasDup = true; break; }
                            seen.add(k);
                        }
                        if (hasDup) {
                            const mvf = (chartDef.mapping?.multiValueFields || []).filter(Boolean);
                            const mvKeys = mvf.map(f => keys.find(k => k.toLowerCase() === f.toLowerCase()) || f);
                            const groups = new Map();
                            rows.forEach(r => {
                                const key = String(r[resolvedLabel] ?? '');
                                if (!groups.has(key)) groups.set(key, []);
                                groups.get(key).push(r);
                            });
                            const sumNum = arr => arr.reduce((a, b) => a + (parseFloat(b) || 0), 0);
                            const aggregated = [];
                            groups.forEach((grp, key) => {
                                const obj = {};
                                obj[resolvedLabel] = key;
                                obj[resolvedValue] = sumNum(grp.map(r => r[resolvedValue]));
                                mvKeys.forEach(mk => {
                                    if (mk && mk !== resolvedValue) obj[mk] = sumNum(grp.map(r => r[mk]));
                                });
                                aggregated.push(obj);
                            });
                            aggregated.sort((a, b) => (parseFloat(b[resolvedValue]) || 0) - (parseFloat(a[resolvedValue]) || 0));
                            console.warn('[chartRenderer] Duplicate labels detected for chart "' + (chartDef.title || '') +
                                '" — auto-aggregated ' + rows.length + ' rows into ' + aggregated.length + ' categories by SUM([' + resolvedValue + ']).');
                            rows = aggregated;
                        }
                    }

                    const out = {
                        labels: rows.map(r => String(r[resolvedLabel] ?? '')),
                        values: rows.map(r => parseFloat(r[resolvedValue]) || 0),
                        rawData: rows
                    };
                    const mvf = (chartDef.mapping?.multiValueFields || []).filter(Boolean);
                    if (mvf.length > 0) {
                        out.multiValues = mvf.map(f => {
                            const rk = keys.find(k => k.toLowerCase() === f.toLowerCase()) || f;
                            return { field: rk, values: rows.map(r => parseFloat(r[rk]) || 0) };
                        });
                    }
                    return out;
                }
            } catch(e) { console.warn('Datasource query fetch failed:', e); lastFetchError = lastFetchError || e.message; }
        }

        // No data available from any real datasource path
        return { labels: [], values: [], error: lastFetchError || window._lastDatasourceError || null };
    }

    // Preflight sanitizer: guards against the internal ApexCharts crash
    //   "Cannot read properties of undefined (reading 'colors')"
    // which happens when certain option arrays are sparse, empty, or contain
    // undefined entries. Mutates `options` in place.
    _sanitizeApexOptions(options) {
        if (!options || typeof options !== 'object') return;
        const fallback = this.colorPalettes.default.slice();

        // 1. colors — must be a non-empty array of strings
        if (!Array.isArray(options.colors) || options.colors.length === 0) {
            options.colors = fallback.slice();
        } else {
            options.colors = options.colors.map(c => (typeof c === 'string' && c) ? c : fallback[0]);
        }

        // 2. series — must be a defined, non-sparse array; strip any null/undefined
        //    entries that ApexCharts would iterate and crash on.
        if (!Array.isArray(options.series)) {
            options.series = [];
        } else {
            options.series = options.series.filter(s => s !== null && s !== undefined);
        }

        // 3. labels (used by pie/donut/polarArea/radialBar) — must be array of strings
        if (options.labels !== undefined && !Array.isArray(options.labels)) {
            options.labels = [];
        }
        if (Array.isArray(options.labels)) {
            options.labels = options.labels.map(l => String(l ?? ''));
        }

        // 4. If pie-like chart has zero data points, substitute a single
        //    zero-value slice so ApexCharts doesn't crash iterating series.
        const chartType = options.chart && options.chart.type;
        const isPieLike = ['pie', 'donut', 'polarArea', 'radialBar'].includes(chartType);
        if (isPieLike) {
            const hasData = Array.isArray(options.series) && options.series.length > 0 &&
                            options.series.some(v => typeof v === 'number' && isFinite(v));
            if (!hasData) {
                options.series = [0];
                if (Array.isArray(options.labels) && options.labels.length === 0) {
                    options.labels = [''];
                }
            }
        }

        // 5. For cartesian types, ensure each series has a defined data array
        if (!isPieLike && Array.isArray(options.series)) {
            options.series = options.series.map(s => {
                if (s && typeof s === 'object') {
                    if (!Array.isArray(s.data)) s.data = [];
                    // Strip undefined/null points that can confuse theme iteration
                    s.data = s.data.filter(p => p !== undefined && p !== null);
                }
                return s;
            });
        }

        // 6. theme.monochrome.color — if set, must be a string (ApexCharts
        //    iterates theme in the crashing code path).
        if (options.theme && options.theme.monochrome && !options.theme.monochrome.color) {
            options.theme.monochrome.color = fallback[0];
        }

        // 7. plotOptions.bar.colors — if present, must be well-formed
        if (options.plotOptions && options.plotOptions.bar && options.plotOptions.bar.colors) {
            const bc = options.plotOptions.bar.colors;
            if (bc.ranges && !Array.isArray(bc.ranges)) delete bc.ranges;
            if (bc.backgroundBarColors && !Array.isArray(bc.backgroundBarColors)) {
                delete bc.backgroundBarColors;
            }
        }

        // 8. fill.colors / stroke.colors — when arrays, filter out undefined
        ['fill', 'stroke', 'markers'].forEach(key => {
            const opt = options[key];
            if (opt && Array.isArray(opt.colors)) {
                opt.colors = opt.colors.map(c => (typeof c === 'string' && c) ? c : fallback[0]);
            }
        });

        // 9. Materialize fill/stroke/plotOptions.bar.colors so ApexCharts' bar,
        //    stacked-bar, and area renderers never dereference properties on
        //    `undefined`. The generic buildApexConfig fallback emits
        //    `fill: undefined` / `stroke: undefined` for bar-family charts,
        //    which crashes internals with
        //    "Cannot read properties of undefined (reading 'colors')".
        const paletteCopy = Array.isArray(options.colors) ? options.colors.slice() : fallback.slice();
        if (!options.fill || typeof options.fill !== 'object') {
            options.fill = { colors: paletteCopy.slice() };
        } else if (!Array.isArray(options.fill.colors)) {
            options.fill.colors = paletteCopy.slice();
        }
        if (!options.stroke || typeof options.stroke !== 'object') {
            options.stroke = { colors: paletteCopy.slice() };
        } else if (!Array.isArray(options.stroke.colors)) {
            options.stroke.colors = paletteCopy.slice();
        }
        options.plotOptions = options.plotOptions || {};
        if (options.plotOptions.bar) {
            const bc = (options.plotOptions.bar.colors && typeof options.plotOptions.bar.colors === 'object')
                ? options.plotOptions.bar.colors
                : {};
            options.plotOptions.bar.colors = {
                ranges: Array.isArray(bc.ranges) ? bc.ranges : [],
                backgroundBarColors: Array.isArray(bc.backgroundBarColors) ? bc.backgroundBarColors : [],
                backgroundBarOpacity: typeof bc.backgroundBarOpacity === 'number' ? bc.backgroundBarOpacity : 1,
                backgroundBarRadius: typeof bc.backgroundBarRadius === 'number' ? bc.backgroundBarRadius : 0
            };
        }
    }

    _baseApexOptions(chartDef) {
        const style = chartDef.style || {};
        const fontFamily = style.fontFamily || "'Inter', sans-serif";
        const hasFontColor = typeof style.fontColor === 'string' && /^#[0-9a-fA-F]{6}$/.test(style.fontColor);
        const titleColor = hasFontColor ? style.fontColor : '#1E2D3D';
        const labelColor = hasFontColor ? style.fontColor : '#7A90A8';
        const fmt = (v) => this._formatNumber(v);
        const legendPos = style.legendPosition || 'top';
        const isVerticalLegend = legendPos === 'left' || legendPos === 'right';
        return {
            chart: {
                height: '100%',
                fontFamily,
                toolbar: { show: false },
                animations: { enabled: style.animated !== false, speed: 700 },
                background: 'transparent'
            },
            legend: {
                show: style.showLegend !== false,
                position: legendPos,
                horizontalAlign: 'center',
                floating: false,
                fontFamily,
                fontSize: '12px',
                fontWeight: 600,
                labels: { colors: titleColor, useSeriesColors: false },
                markers: { width: 12, height: 12, radius: 6, offsetX: -2, strokeWidth: 0 },
                itemMargin: { horizontal: 10, vertical: 4 },
                // Push the legend away from the plot area so axis labels and
                // legend swatches no longer collide along the bottom edge.
                offsetY: legendPos === 'bottom' ? 8 : (legendPos === 'top' ? -4 : 0),
                offsetX: isVerticalLegend ? 0 : 0
            },
            tooltip: {
                enabled: style.showTooltips !== false,
                theme: 'dark',
                y: { formatter: fmt }
            },
            title: {
                text: chartDef.title || '',
                style: { fontSize: `${style.titleFontSize || 14}px`, fontFamily, fontWeight: '600', color: titleColor }
            },
            grid: {
                borderColor: 'rgba(0,0,0,0.04)',
                strokeDashArray: 0,
                // Reserve enough padding so a bottom legend + rotated x-axis
                // labels don't collide along the bottom edge of the plot.
                padding: {
                    top: 0,
                    right: 12,
                    bottom: legendPos === 'bottom' ? 28 : 14,
                    left: 8
                }
            },
            dataLabels: {
                enabled: !!style.showDataLabels,
                style: { fontSize: '11px', fontFamily, fontWeight: 600 },
                formatter: fmt
            },
            xaxis: {
                labels: {
                    style: { colors: labelColor, fontFamily, fontSize: '11px' },
                    rotate: -35,
                    rotateAlways: false,
                    hideOverlappingLabels: true,
                    // Show full label text — truncating with trim:true was
                    // chopping legitimate values off the x-axis. We rely on
                    // rotation + hideOverlappingLabels to avoid visual collisions.
                    trim: false,
                    maxHeight: 140,
                    offsetY: 2
                },
                axisTicks: { show: true },
                axisBorder: { show: true, color: 'rgba(0,0,0,0.08)' }
            },
            yaxis: {
                labels: {
                    style: { colors: labelColor, fontFamily, fontSize: '11px' },
                    formatter: fmt
                }
            }
        };
    }

    buildApexConfig(chartDef, data) {
        const style = chartDef.style || {};
        const palette = style.colorPalette || 'default';
        const labels = (data.labels || []).map(s => this._formatLabel(s));
        const values = (data.values || []).map(Number);
        const colors = this.getColors(palette, Math.max(labels.length, 8));
        const ct = chartDef.chartType;

        if (ct === 'gauge')           return this.buildApexGaugeConfig(chartDef, values, style, colors);
        if (ct === 'waterfall')       return this.buildApexWaterfallConfig(chartDef, labels, values, style, colors);
        if (ct === 'funnel')          return this.buildApexFunnelConfig(chartDef, labels, values, style, colors);
        if (ct === 'mixedBarLine')    return this.buildApexMixedBarLineConfig(chartDef, labels, values, style, colors);
        if (ct === 'groupedBar')      return this.buildApexGroupedBarConfig(chartDef, labels, values, style, colors, data);
        if (ct === 'histogram')       return this.buildApexHistogramConfig(chartDef, labels, values, style, colors);
        if (ct === 'pareto')          return this.buildApexParetoConfig(chartDef, labels, values, style, colors);
        if (ct === 'bellCurve')       return this.buildApexBellCurveConfig(chartDef, labels, values, style, colors);
        if (ct === 'regressionLine')  return this.buildApexRegressionConfig(chartDef, labels, values, style, colors);
        if (ct === 'confidenceBand')  return this.buildApexConfidenceBandConfig(chartDef, labels, values, style, colors);
        if (ct === 'controlChart')    return this.buildApexControlChartConfig(chartDef, labels, values, style, colors);
        if (ct === 'errorBar')        return this.buildApexErrorBarConfig(chartDef, labels, values, style, colors);
        if (ct === 'stepLine')        return this.buildApexStepLineConfig(chartDef, labels, values, style, colors);
        if (ct === 'rangeArea')       return this.buildApexRangeAreaConfig(chartDef, labels, values, style, colors);
        if (ct === 'burnDown')        return this.buildApexBurnDownConfig(chartDef, labels, values, style, colors);
        if (ct === 'gantt')           return this.buildApexGanttConfig(chartDef, labels, values, style, colors);
        if (ct === 'bulletChart')     return this.buildApexBulletChartConfig(chartDef, labels, values, style, colors, data);
        if (ct === 'lollipop')        return this.buildApexLollipopConfig(chartDef, labels, values, style, colors);
        if (ct === 'slope')           return this.buildApexSlopeConfig(chartDef, labels, values, style, colors, data);
        if (ct === 'divergingBar')    return this.buildApexDivergingBarConfig(chartDef, labels, values, style, colors);
        if (ct === 'populationPyramid') return this.buildApexPopulationPyramidConfig(chartDef, labels, values, style, colors, data);
        if (ct === 'spanChart')       return this.buildApexSpanChartConfig(chartDef, labels, values, style, colors);
        if (ct === 'pairedBar')       return this.buildApexPairedBarConfig(chartDef, labels, values, style, colors, data);
        if (ct === 'stackedBar100')   return this.buildApexStackedBar100Config(chartDef, labels, values, style, colors, data);
        if (ct === 'stackedArea100')  return this.buildApexStackedArea100Config(chartDef, labels, values, style, colors, data);
        if (ct === 'streamGraph')     return this.buildApexStreamGraphConfig(chartDef, labels, values, style, colors, data);
        if (ct === 'velocityChart')   return this.buildApexVelocityChartConfig(chartDef, labels, values, style, colors, data);
        if (ct === 'sparkline')       return this.buildApexSparklineConfig(chartDef, labels, values, style, colors);
        if (ct === 'progressBar')     return this.buildApexProgressBarConfig(chartDef, labels, values, style, colors);
        if (ct === 'radialProgress')  return this.buildApexRadialProgressConfig(chartDef, values, style, colors);
        if (ct === 'nightingaleRose') return this.buildApexNightingaleRoseConfig(chartDef, labels, values, style, colors);
        if (ct === 'dotPlot')         return this.buildApexDotPlotConfig(chartDef, labels, values, style, colors);
        if (ct === 'timeLine')        return this.buildApexTimeLineConfig(chartDef, labels, values, style, colors);

        // ── Generic fallback ──
        const apexType = this.mapApexType(ct);
        const opts = this._baseApexOptions(chartDef);
        const isPieLike = ['pie', 'donut', 'polarArea'].includes(apexType);

        if (isPieLike) {
            return {
                ...opts,
                chart: { ...opts.chart, type: apexType },
                series: values,
                labels,
                colors,
                plotOptions: apexType === 'donut' ? { pie: { donut: { size: '65%' } } } : {}
            };
        }
        if (apexType === 'radar') {
            return {
                ...opts,
                chart: { ...opts.chart, type: 'radar' },
                series: [{ name: chartDef.mapping?.valueField || chartDef.title || 'Data', data: values }],
                xaxis: { ...opts.xaxis, categories: labels },
                colors: [colors[0]],
                fill: { opacity: 0.4 }
            };
        }
        if (apexType === 'scatter') {
            return {
                ...opts,
                chart: { ...opts.chart, type: 'scatter' },
                series: [{ name: chartDef.mapping?.valueField || 'Data', data: values.map((v, i) => [i, v]) }],
                colors: [colors[0]]
            };
        }
        if (apexType === 'bubble') {
            return {
                ...opts,
                chart: { ...opts.chart, type: 'bubble' },
                series: [{ name: chartDef.mapping?.valueField || 'Data', data: values.map((v, i) => ({ x: i, y: v, z: Math.max(5, v / 5) })) }],
                colors: [colors[0]]
            };
        }

        // Bar / line / area
        const isHorizontal = ct === 'horizontalBar';
        const isStacked    = ct === 'stackedBar';
        const isArea       = ct === 'area';
        const actualType   = isArea ? 'area' : apexType;

        let series = [{ name: chartDef.mapping?.valueField || chartDef.title || 'Data', data: values }];
        if (data.multiValues && data.multiValues.length > 0) {
            series.push(...data.multiValues.map(mv => ({ name: mv.field || 'Series', data: mv.values })));
        }

        // For single-series bar charts, distribute palette colors per category
        // so each column gets a different color from the selected theme
        // (instead of every bar painted with colors[0]). Multi-series and
        // stacked charts keep one color per series as expected.
        const isSingleSeriesBar = (actualType === 'bar') && series.length === 1 && !isStacked;
        const baseLegend = opts.legend || {};

        return {
            ...opts,
            chart: { ...opts.chart, type: actualType, stacked: isStacked || undefined },
            series,
            xaxis: { ...opts.xaxis, categories: labels },
            colors,
            legend: isSingleSeriesBar ? { ...baseLegend, show: false } : baseLegend,
            plotOptions: {
                bar: {
                    horizontal: isHorizontal,
                    borderRadius: parseInt(style.borderRadius || '4'),
                    columnWidth: '60%',
                    distributed: isSingleSeriesBar
                }
            },
            // Always emit well-formed objects (never `undefined`) — ApexCharts'
            // internal bar/stacked-bar renderer dereferences `config.fill.colors[i]`
            // and crashes when these slots are missing.
            stroke: (actualType === 'line' || actualType === 'area') ? { curve: 'smooth', width: 2 } : {},
            fill: isArea ? { type: 'gradient', gradient: { shadeIntensity: 1, opacityFrom: 0.4, opacityTo: 0.05, stops: [0, 100] } } : {}
        };
    }


    // ============================================================
    // ApexCharts config builders
    // ============================================================

    buildApexGaugeConfig(chartDef, values, style, colors) {
        const val = values[0] || 0;
        const max = Math.max(...values, 100);
        const pct = Math.round(Math.min(val / max, 1) * 100);
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'radialBar' },
            series: [pct],
            plotOptions: {
                radialBar: {
                    startAngle: -90, endAngle: 90,
                    hollow: { size: '60%' },
                    dataLabels: {
                        name: { show: true, offsetY: -10, color: '#1E2D3D', fontSize: '13px', fontWeight: '600' },
                        value: { show: true, fontSize: '24px', fontWeight: '700', color: colors[0], formatter: () => val.toFixed(1) }
                    }
                }
            },
            labels: [chartDef.title || 'Gauge'],
            colors: [colors[0]],
            legend: { show: false }
        };
    }

    buildApexWaterfallConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Start','Q1','Q2','Q3','Q4','End'];
        const vals = values.length ? values : [0, 120, -30, 80, -20, 150];
        let cumulative = 0;
        const last = lbls.length - 1;
        const floatData = lbls.map((l, i) => {
            const v = vals[i] || 0;
            let y;
            if (i === 0 || i === last) { y = [0, v]; }
            else { y = [cumulative, cumulative + v]; }
            cumulative += v;
            return { x: l, y };
        });
        const barColors = floatData.map((d, i) => {
            if (i === 0 || i === last) return colors[0];
            return (vals[i] || 0) >= 0 ? '#4CAF50' : '#E87C3E';
        });
        // Embed per-bar fill color on each datum so ApexCharts picks them up
        // for rangeBar (top-level `colors` alone does not map 1:1 to bars).
        const dataWithColors = floatData.map((d, i) => ({ ...d, fillColor: barColors[i] }));
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'rangeBar' },
            series: [{ name: chartDef.title || 'Waterfall', data: dataWithColors }],
            colors: barColors,
            plotOptions: { bar: { horizontal: false, columnWidth: '60%', borderRadius: 2 } },
            tooltip: {
                custom: ({ dataPointIndex, w }) => {
                    try {
                        const d = w.config.series[0].data[dataPointIndex];
                        if (!d || !Array.isArray(d.y)) return '';
                        const change = (d.y[1] || 0) - (d.y[0] || 0);
                        return `<div style="padding:8px;font-size:12px"><b>${d.x}</b><br>Change: ${change.toFixed(1)} (Total: ${(d.y[1] || 0).toFixed(1)})</div>`;
                    } catch (_) { return ''; }
                }
            }
        };
    }

    buildApexFunnelConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Leads','Prospects','Qualified','Proposals','Closed'];
        const vals = values.length ? [...values].sort((a,b) => b - a) : [1000, 750, 500, 250, 100];
        const pairs = lbls.map((l,i) => ({l, v: vals[i]||0})).sort((a,b) => b.v - a.v);
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'bar' },
            series: [{ name: chartDef.title || 'Funnel', data: pairs.map(p => p.v) }],
            xaxis: { ...opts.xaxis, categories: pairs.map(p => p.l) },
            colors: [colors[0]],
            plotOptions: { bar: { horizontal: true, borderRadius: 4, barHeight: '60%', distributed: true } }
        };
    }

    buildApexMixedBarLineConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May','Jun'];
        const barVals = values.length ? values : [120,150,130,200,180,210];
        const lineVals = barVals.map((v,i) => Math.round(barVals.slice(0,i+1).reduce((a,b)=>a+b,0)/(i+1)));
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'bar' },
            series: [
                { name: 'Monthly', type: 'bar', data: barVals },
                { name: 'Trend Avg', type: 'line', data: lineVals }
            ],
            xaxis: { ...opts.xaxis, categories: lbls },
            colors: [colors[0], colors[1]],
            plotOptions: { bar: { borderRadius: 4, columnWidth: '55%' } },
            stroke: { width: [0, 2], curve: 'smooth' },
            markers: { size: [0, 4] }
        };
    }

    buildApexGroupedBarConfig(chartDef, labels, values, style, colors, data) {
        const mv = (data && Array.isArray(data.multiValues)) ? data.multiValues : [];
        const opts = this._baseApexOptions(chartDef);
        let series, lbls;
        if (labels.length && (mv.length > 0 || values.length)) {
            lbls = labels;
            const primaryName = chartDef.mapping?.valueField || chartDef.title || 'Value';
            series = [{ name: primaryName, data: values }];
            mv.forEach(m => series.push({ name: m.field || 'Series', data: m.values || [] }));
        } else {
            lbls = labels.length ? labels : ['Q1','Q2','Q3','Q4'];
            const n = Math.min(lbls.length, 4);
            const seriesNames = ['Product A','Product B','Product C'];
            series = seriesNames.map((name, si) => ({
                name,
                data: Array.from({length: n}, () => Math.round(40 + Math.random()*80 + si*20))
            }));
            lbls = lbls.slice(0, n);
        }
        return {
            ...opts,
            chart: { ...opts.chart, type: 'bar' },
            series,
            xaxis: { ...opts.xaxis, categories: lbls },
            colors: colors.slice(0, Math.max(series.length, 1)),
            plotOptions: { bar: { borderRadius: 3, columnWidth: '60%' } }
        };
    }

    buildApexHistogramConfig(chartDef, labels, values, style, colors) {
        const raw = values.length >= 5 ? values : [23,25,27,28,30,31,32,33,34,35,36,37,38,40,42,45,48,50,55,60];
        const min = Math.min(...raw), max = Math.max(...raw);
        const binCount = Math.min(10, Math.ceil(Math.sqrt(raw.length))) || 1;
        // Guard: when all raw values are equal (max==min), binSize would be 0
        // and every Math.floor((v-min)/0) becomes NaN — collapse to a single
        // bin holding all observations so the chart renders sensibly.
        const range = max - min;
        const bins = Array(binCount).fill(0);
        let binSize;
        if (range === 0) {
            binSize = 1;
            bins[0] = raw.length;
        } else {
            binSize = range / binCount;
            raw.forEach(v => { const idx = Math.min(Math.floor((v - min) / binSize), binCount - 1); bins[idx]++; });
        }
        const binLabels = bins.map((_, i) => `${(min + i*binSize).toFixed(1)}-${(min + (i+1)*binSize).toFixed(1)}`);
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'bar' },
            series: [{ name: 'Frequency', data: bins }],
            xaxis: { ...opts.xaxis, categories: binLabels },
            colors: [colors[0]],
            plotOptions: { bar: { columnWidth: '98%', borderRadius: 0 } },
            dataLabels: { enabled: false }
        };
    }

    buildApexParetoConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Defect A','Defect B','Defect C','Defect D','Defect E'];
        const vals = values.length ? values : [80, 50, 30, 20, 10];
        const pairs = lbls.map((l,i) => ({l, v: vals[i]||0})).sort((a,b) => b.v - a.v);
        const total = pairs.reduce((s,p) => s + p.v, 0) || 1;
        let cum = 0;
        const cumPct = pairs.map(p => { cum += p.v; return Math.round(cum / total * 100); });
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'bar' },
            series: [
                { name: 'Frequency', type: 'bar', data: pairs.map(p => p.v) },
                { name: 'Cumulative %', type: 'line', data: cumPct }
            ],
            xaxis: { ...opts.xaxis, categories: pairs.map(p => p.l) },
            yaxis: [
                { title: { text: 'Count' }, labels: { style: { colors: '#7A90A8' } } },
                { opposite: true, min: 0, max: 100, title: { text: '% Cumulative' }, labels: { style: { colors: '#7A90A8' }, formatter: v => v + '%' } }
            ],
            colors: [colors[0], colors[2]],
            plotOptions: { bar: { borderRadius: 3, columnWidth: '55%' } },
            stroke: { width: [0, 2], curve: 'smooth' },
            markers: { size: [0, 4] }
        };
    }

    buildApexBellCurveConfig(chartDef, labels, values, style, colors) {
        const raw = values.length >= 3 ? values : [10,15,20,25,30,35,40,45,50,55,60,65,70];
        const mean = raw.reduce((s,v) => s+v,0) / raw.length;
        const variance = raw.reduce((s,v) => s + Math.pow(v-mean,2),0) / raw.length;
        const std = Math.sqrt(variance) || 1;
        const xMin = mean - 4*std, xMax = mean + 4*std;
        const pts = 60;
        const xVals = [], yVals = [];
        for (let i = 0; i <= pts; i++) {
            const x = xMin + (xMax - xMin) * i / pts;
            xVals.push(x.toFixed(2));
            yVals.push(parseFloat((Math.exp(-0.5 * Math.pow((x - mean) / std, 2)) / (std * Math.sqrt(2 * Math.PI))).toFixed(6)));
        }
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'area' },
            series: [{ name: 'Distribution', data: yVals }],
            xaxis: { ...opts.xaxis, categories: xVals, tickAmount: 6 },
            colors: [colors[0]],
            fill: { type: 'gradient', gradient: { shadeIntensity: 1, opacityFrom: 0.4, opacityTo: 0.05, stops: [0, 100] } },
            stroke: { curve: 'smooth', width: 2 },
            dataLabels: { enabled: false }
        };
    }

    buildApexRegressionConfig(chartDef, labels, values, style, colors) {
        const pts = values.length >= 4 ? values.map((v,i) => ({x: i, y: v})) :
            [{x:1,y:22},{x:2,y:28},{x:3,y:33},{x:4,y:35},{x:5,y:42},{x:6,y:48},{x:7,y:51},{x:8,y:58}];
        const n = pts.length;
        const sumX = pts.reduce((s,p)=>s+p.x,0), sumY = pts.reduce((s,p)=>s+p.y,0);
        const sumXY = pts.reduce((s,p)=>s+p.x*p.y,0), sumX2 = pts.reduce((s,p)=>s+p.x*p.x,0);
        const slope = (n*sumXY - sumX*sumY) / (n*sumX2 - sumX*sumX);
        const intercept = (sumY - slope*sumX) / n;
        const lineData = pts.map(p => ({ x: p.x, y: parseFloat((slope*p.x + intercept).toFixed(2)) }));
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'line' },
            series: [
                { name: 'Data Points', type: 'scatter', data: pts.map(p => ({ x: p.x, y: p.y })) },
                { name: 'Regression', type: 'line', data: lineData.map(p => ({ x: p.x, y: p.y })) }
            ],
            colors: [colors[0], colors[2]],
            stroke: { width: [0, 2], curve: 'straight' },
            markers: { size: [5, 0] }
        };
    }

    buildApexConfidenceBandConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug'];
        const vals = values.length ? values : [30,35,32,40,38,45,42,48];
        const ci = vals.map(v => v * 0.12);
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'line' },
            series: [
                { name: 'Upper CI', data: vals.map((v,i) => parseFloat((v+ci[i]).toFixed(2))) },
                { name: 'Mean', data: vals },
                { name: 'Lower CI', data: vals.map((v,i) => parseFloat((v-ci[i]).toFixed(2))) }
            ],
            xaxis: { ...opts.xaxis, categories: lbls },
            colors: [this.hexToRgba(colors[0], 0.4), colors[0], this.hexToRgba(colors[0], 0.4)],
            stroke: { width: [1, 2, 1], curve: 'smooth', dashArray: [4, 0, 4] },
            fill: { type: 'solid', opacity: [0.1, 1, 0.1] },
            markers: { size: [0, 4, 0] }
        };
    }

    buildApexControlChartConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : Array.from({length: 15}, (_,i) => `P${i+1}`);
        const vals = values.length ? values : [48,52,51,53,47,50,54,49,51,50,52,55,48,51,50];
        const mean = vals.reduce((s,v)=>s+v,0)/vals.length;
        const std = Math.sqrt(vals.reduce((s,v)=>s+Math.pow(v-mean,2),0)/vals.length);
        const ucl = mean + 3*std, lcl = Math.max(0, mean - 3*std);
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'line' },
            series: [
                { name: 'UCL', data: Array(vals.length).fill(parseFloat(ucl.toFixed(2))) },
                { name: 'Mean', data: Array(vals.length).fill(parseFloat(mean.toFixed(2))) },
                { name: 'LCL', data: Array(vals.length).fill(parseFloat(lcl.toFixed(2))) },
                { name: 'Data', data: vals }
            ],
            xaxis: { ...opts.xaxis, categories: lbls.slice(0, vals.length) },
            colors: ['#dc3545', '#4CAF50', '#dc3545', colors[0]],
            stroke: { width: [1.5, 1.5, 1.5, 2], curve: 'smooth', dashArray: [5, 4, 5, 0] },
            markers: { size: [0, 0, 0, 4] }
        };
    }

    buildApexErrorBarConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['A','B','C','D','E'];
        const vals = values.length ? values : [45, 62, 38, 75, 55];
        const errors = vals.map(v => parseFloat((v * 0.1).toFixed(2)));
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'bar' },
            series: [{ name: chartDef.title || 'Values', data: vals }],
            xaxis: { ...opts.xaxis, categories: lbls },
            colors: [colors[0]],
            plotOptions: { bar: { borderRadius: 4, columnWidth: '50%' } },
            annotations: {
                points: vals.map((v, i) => ({
                    x: lbls[i], y: v,
                    marker: { size: 0 },
                    label: { text: `±${errors[i]}`, style: { fontSize: '10px', color: '#8492a6', background: 'transparent', border: 'none' } }
                }))
            }
        };
    }

    buildApexStepLineConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Mon','Tue','Wed','Thu','Fri','Sat','Sun'];
        const vals = values.length ? values : [100, 120, 120, 150, 130, 130, 180];
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'line' },
            series: [{ name: chartDef.title || 'Step Line', data: vals }],
            xaxis: { ...opts.xaxis, categories: lbls },
            colors: [colors[0]],
            stroke: { curve: 'stepline', width: 2 },
            fill: { type: 'gradient', gradient: { shadeIntensity: 1, opacityFrom: 0.15, opacityTo: 0.02, stops: [0, 100] } },
            markers: { size: 5 }
        };
    }

    buildApexRangeAreaConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May','Jun','Jul'];
        const vals = values.length ? values : [30,35,33,40,38,45,42];
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'rangeArea' },
            series: [{
                name: chartDef.title || 'Range',
                data: lbls.map((l, i) => ({
                    x: l,
                    y: [parseFloat((vals[i] - vals[i]*0.2).toFixed(2)), parseFloat((vals[i] + vals[i]*0.2).toFixed(2))]
                }))
            }],
            colors: [colors[0]],
            fill: { opacity: 0.3 },
            stroke: { curve: 'smooth', width: 2 }
        };
    }

    buildApexBurnDownConfig(chartDef, labels, values, style, colors) {
        let lbls, actual, ideal;
        if (values.length >= 2) {
            // Use real data: actual = provided series; ideal = straight line from
            // first → last point across the same number of steps.
            lbls = labels.length === values.length ? labels.slice() : values.map((_,i) => `Pt ${i}`);
            actual = values.slice();
            const start = actual[0];
            const end = actual[actual.length - 1];
            const stepCount = actual.length - 1;
            ideal = actual.map((_,i) => parseFloat((start + (end - start) * (i / stepCount)).toFixed(2)));
        } else {
            const n = 10;
            const total = (values[0] || 100);
            lbls = Array.from({length: n+1}, (_,i) => `Day ${i}`);
            ideal = Array.from({length: n+1}, (_,i) => Math.round(total - total*i/n));
            actual = [total];
            for (let i = 1; i <= n; i++) actual.push(Math.max(0, Math.round(ideal[i] + (Math.random()-0.3)*total*0.08)));
        }
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'line' },
            series: [
                { name: 'Ideal', data: ideal },
                { name: 'Actual', data: actual }
            ],
            xaxis: { ...opts.xaxis, categories: lbls },
            colors: ['#adb5bd', colors[0]],
            stroke: { width: [1.5, 2], curve: 'smooth', dashArray: [6, 0] },
            fill: { type: ['solid', 'gradient'], opacity: [1, 0.15] },
            markers: { size: [0, 4] }
        };
    }

    buildApexGanttConfig(chartDef, labels, values, style, colors) {
        const tasks = labels.length ? labels.slice(0,6) : ['Planning','Design','Development','Testing','Deployment','Review'];
        const now = new Date();
        const data = tasks.map((t, i) => ({
            x: t,
            y: [
                new Date(now.getFullYear(), now.getMonth(), 1 + i*3 + (i>2?2:0)).getTime(),
                new Date(now.getFullYear(), now.getMonth(), 1 + i*3 + (i>2?2:0) + 3 + (i%3)).getTime()
            ]
        }));
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'rangeBar' },
            series: [{ name: 'Task Duration', data }],
            colors: colors.slice(0, tasks.length),
            plotOptions: { bar: { horizontal: true, borderRadius: 4 } },
            xaxis: { type: 'datetime', labels: { style: { colors: '#7A90A8' } } },
            yaxis: { labels: { style: { colors: '#7A90A8' } } }
        };
    }

    buildApexBulletChartConfig(chartDef, labels, values, style, colors, data) {
        const lbls = labels.length ? labels.slice(0,4) : ['Revenue','Cost','Growth','NPS'];
        const vals = values.length ? values.slice(0,lbls.length) : [78, 55, 62, 85];
        // Prefer a real Target field when mapped (first multiValues entry).
        // Fallback: vals * 1.15 (no longer clamped to 100, which broke any
        // dataset using values like revenue or counts > 100).
        const mv = (data && Array.isArray(data.multiValues)) ? data.multiValues : [];
        const targets = (mv.length > 0 && Array.isArray(mv[0].values))
            ? mv[0].values.slice(0, lbls.length).map(v => Number(v) || 0)
            : vals.map(v => Math.round(v * 1.15));
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'bar' },
            series: [
                { name: 'Target', data: targets },
                { name: 'Actual', data: vals }
            ],
            xaxis: { ...opts.xaxis, categories: lbls },
            colors: ['#e9ecef', colors[0]],
            plotOptions: { bar: { horizontal: true, borderRadius: 4, barHeight: '60%' } },
            dataLabels: { enabled: false }
        };
    }

    buildApexLollipopConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['A','B','C','D','E','F'];
        const vals = values.length ? values : [42, 67, 35, 78, 55, 63];
        const opts = this._baseApexOptions(chartDef);
        // Mixed chart: thin bar = stick, scatter = head dot. A pure bar chart
        // ignores the markers config so the dot was never drawn before.
        return {
            ...opts,
            chart: { ...opts.chart, type: 'bar' },
            series: [
                { name: chartDef.title || 'Value', type: 'bar', data: vals },
                { name: 'Head', type: 'scatter', data: vals }
            ],
            xaxis: { ...opts.xaxis, categories: lbls },
            colors: [colors[0], colors[0]],
            plotOptions: { bar: { columnWidth: '4%', borderRadius: 0 } },
            markers: { size: [0, 8], strokeColors: '#fff', strokeWidth: 2, hover: { size: 10 } },
            legend: { show: false }
        };
    }

    buildApexSlopeConfig(chartDef, labels, values, style, colors, data) {
        const mv = (data && Array.isArray(data.multiValues)) ? data.multiValues : [];
        let items, before, after, n1, n2;
        if (labels.length && values.length && mv.length > 0) {
            // Real data: primary value = before, first multi-value field = after.
            items = labels.slice();
            before = values.slice();
            after = (mv[0].values || []).slice();
            n1 = chartDef.mapping?.valueField || 'Before';
            n2 = mv[0].field || 'After';
        } else if (labels.length && values.length) {
            // Only one value series — render a flat slope so user still sees real data.
            items = labels.slice();
            before = values.slice();
            after = values.slice();
            n1 = chartDef.mapping?.valueField || 'Before';
            n2 = 'After';
        } else {
            items = ['Product A','Product B','Product C','Product D'];
            before = items.map(()=>Math.round(30+Math.random()*50));
            after = before.map(v => Math.round(v + (Math.random()-0.4)*20));
            n1 = 'Before'; n2 = 'After';
        }
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'line' },
            series: items.map((name, i) => ({ name, data: [before[i], after[i]] })),
            xaxis: { ...opts.xaxis, categories: [n1, n2] },
            colors: colors.slice(0, items.length),
            stroke: { width: 2, curve: 'straight' },
            markers: { size: 6 }
        };
    }

    buildApexDivergingBarConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Very Satisfied','Satisfied','Neutral','Dissatisfied','Very Dissatisfied'];
        // Respect the real sign of each value rather than splitting by index;
        // the previous behavior turned half of every dataset negative.
        const vals = values.length ? values.slice(0, lbls.length).map(v => Number(v) || 0) : [45, 30, 10, -20, -35];
        const barColors = vals.map(v => v >= 0 ? '#4CAF50' : '#E87C3E');
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'bar' },
            series: [{ name: chartDef.title || 'Diverging', data: vals.map((v,i) => ({ x: lbls[i], y: v, fillColor: barColors[i] })) }],
            colors: barColors,
            plotOptions: { bar: { horizontal: true, borderRadius: 4, distributed: true } },
            xaxis: { ...opts.xaxis, categories: lbls, labels: { formatter: v => Math.abs(Number(v) || 0) } },
            legend: { show: false }
        };
    }

    buildApexPopulationPyramidConfig(chartDef, labels, values, style, colors, data) {
        const mv = (data && Array.isArray(data.multiValues)) ? data.multiValues : [];
        const opts = this._baseApexOptions(chartDef);
        let categories, leftSeries, rightSeries, leftName, rightName;
        if (labels.length && mv.length > 0) {
            categories = labels;
            leftName = chartDef.mapping?.valueField || 'Left';
            rightName = mv[0].field || 'Right';
            leftSeries = values.map(v => -Math.abs(v));
            rightSeries = (mv[0].values || []).map(v => Math.abs(v));
        } else {
            categories = ['0-9','10-19','20-29','30-39','40-49','50-59','60-69','70+'];
            leftName = 'Male'; rightName = 'Female';
            leftSeries = [80,95,110,120,105,90,70,45].map(v => -v);
            rightSeries = [78,92,108,118,108,92,74,52];
        }
        return {
            ...opts,
            chart: { ...opts.chart, type: 'bar' },
            series: [
                { name: leftName, data: leftSeries },
                { name: rightName, data: rightSeries }
            ],
            xaxis: { ...opts.xaxis, categories, labels: { formatter: v => Math.abs(Number(v) || 0) } },
            colors: [colors[0], colors[1]],
            plotOptions: { bar: { horizontal: true, borderRadius: 2, barHeight: '80%' } },
            yaxis: { labels: { style: { colors: '#7A90A8' } } }
        };
    }

    buildApexSpanChartConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels.slice(0,5) : ['Item A','Item B','Item C','Item D','Item E'];
        const mins = values.length ? values.slice(0,lbls.length) : [10,20,15,30,25];
        const maxs = mins.map(v => v + Math.round(v * 0.5 + 10));
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'rangeBar' },
            series: [{ name: 'Range', data: lbls.map((l,i) => ({ x: l, y: [mins[i], maxs[i]] })) }],
            colors: [colors[0]],
            plotOptions: { bar: { horizontal: true, borderRadius: 4 } }
        };
    }

    buildApexPairedBarConfig(chartDef, labels, values, style, colors, data) {
        const mv = (data && Array.isArray(data.multiValues)) ? data.multiValues : [];
        const opts = this._baseApexOptions(chartDef);
        let lbls, g1, g2, n1, n2;
        if (labels.length && mv.length > 0) {
            lbls = labels;
            n1 = chartDef.mapping?.valueField || 'Series 1';
            n2 = mv[0].field || 'Series 2';
            g1 = values;
            g2 = mv[0].values || [];
        } else {
            lbls = labels.length ? labels.slice(0,5) : ['North','South','East','West','Central'];
            g1 = values.length ? values.slice(0,lbls.length) : [65,72,58,80,70];
            g2 = g1.map(v => Math.round(v * (0.7 + Math.random() * 0.6)));
            n1 = '2023'; n2 = '2024';
        }
        return {
            ...opts,
            chart: { ...opts.chart, type: 'bar' },
            series: [
                { name: n1, data: g1 },
                { name: n2, data: g2 }
            ],
            xaxis: { ...opts.xaxis, categories: lbls },
            colors: [colors[0], colors[1]],
            plotOptions: { bar: { horizontal: true, borderRadius: 3, barHeight: '40%' } }
        };
    }

    buildApexStackedBar100Config(chartDef, labels, values, style, colors, data) {
        const mv = (data && Array.isArray(data.multiValues)) ? data.multiValues : [];
        const opts = this._baseApexOptions(chartDef);
        let lbls, seriesNames, rawData;
        if (labels.length && mv.length > 0) {
            lbls = labels;
            seriesNames = [chartDef.mapping?.valueField || 'Value', ...mv.map(m => m.field || 'Series')];
            rawData = [values, ...mv.map(m => m.values || [])];
        } else {
            lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May'];
            seriesNames = ['Group A','Group B','Group C'];
            rawData = seriesNames.map(() => lbls.map(() => Math.round(20 + Math.random() * 40)));
        }
        const normalized = lbls.map((_, li) => {
            const total = seriesNames.reduce((s, _n, si) => s + (Number(rawData[si][li]) || 0), 0) || 1;
            return seriesNames.map((_, si) => Math.round((Number(rawData[si][li]) || 0) / total * 100));
        });
        return {
            ...opts,
            chart: { ...opts.chart, type: 'bar', stacked: true, stackType: '100%' },
            series: seriesNames.map((name, si) => ({ name, data: lbls.map((_,li) => normalized[li][si]) })),
            xaxis: { ...opts.xaxis, categories: lbls },
            colors: colors.slice(0, seriesNames.length),
            plotOptions: { bar: { borderRadius: 0, columnWidth: '60%' } },
            yaxis: { max: 100, labels: { formatter: v => v + '%', style: { colors: '#7A90A8' } } }
        };
    }

    buildApexStackedArea100Config(chartDef, labels, values, style, colors, data) {
        const mv = (data && Array.isArray(data.multiValues)) ? data.multiValues : [];
        const opts = this._baseApexOptions(chartDef);
        let lbls, seriesNames, rawData;
        if (labels.length && mv.length > 0) {
            lbls = labels;
            seriesNames = [chartDef.mapping?.valueField || 'Value', ...mv.map(m => m.field || 'Series')];
            rawData = [values, ...mv.map(m => m.values || [])];
        } else {
            lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May','Jun'];
            seriesNames = ['Series A','Series B','Series C'];
            rawData = seriesNames.map(() => lbls.map(() => Math.round(20 + Math.random() * 40)));
        }
        const normalized = lbls.map((_, li) => {
            const total = seriesNames.reduce((s,_n,si) => s + (Number(rawData[si][li]) || 0), 0) || 1;
            return seriesNames.map((_, si) => Math.round((Number(rawData[si][li]) || 0)/total*100));
        });
        return {
            ...opts,
            chart: { ...opts.chart, type: 'area', stacked: true },
            series: seriesNames.map((name, si) => ({ name, data: lbls.map((_,li) => normalized[li][si]) })),
            xaxis: { ...opts.xaxis, categories: lbls },
            colors: colors.slice(0, seriesNames.length),
            stroke: { curve: 'smooth', width: 2 },
            fill: { type: 'gradient', gradient: { opacityFrom: 0.6, opacityTo: 0.3 } },
            yaxis: { max: 100, labels: { formatter: v => v + '%', style: { colors: '#7A90A8' } } }
        };
    }

    buildApexStreamGraphConfig(chartDef, labels, values, style, colors, data) {
        const mv = (data && Array.isArray(data.multiValues)) ? data.multiValues : [];
        const opts = this._baseApexOptions(chartDef);
        let lbls, series;
        if (labels.length && (mv.length > 0 || values.length)) {
            lbls = labels;
            const primaryName = chartDef.mapping?.valueField || chartDef.title || 'Value';
            series = [{ name: primaryName, data: values }];
            mv.forEach(m => series.push({ name: m.field || 'Series', data: m.values || [] }));
        } else {
            lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug'];
            const seriesNames = ['Series A','Series B','Series C','Series D'];
            series = seriesNames.map(name => ({
                name,
                data: lbls.map(() => Math.round(15 + Math.random()*30))
            }));
        }
        return {
            ...opts,
            chart: { ...opts.chart, type: 'area', stacked: true },
            series,
            xaxis: { ...opts.xaxis, categories: lbls },
            colors: colors.slice(0, Math.max(series.length, 1)),
            stroke: { curve: 'smooth', width: 1 },
            fill: { type: 'gradient', gradient: { opacityFrom: 0.7, opacityTo: 0.4 } },
            dataLabels: { enabled: false }
        };
    }

    buildApexVelocityChartConfig(chartDef, labels, values, style, colors, data) {
        const mv = (data && Array.isArray(data.multiValues)) ? data.multiValues : [];
        const opts = this._baseApexOptions(chartDef);
        let sprints, committed, completed, n1, n2;
        if (labels.length && mv.length > 0) {
            sprints = labels;
            n1 = chartDef.mapping?.valueField || 'Series 1';
            n2 = mv[0].field || 'Series 2';
            committed = values;
            completed = mv[0].values || [];
        } else if (labels.length && values.length) {
            // Real labels + single value series — show it as Committed; mirror
            // it as Completed so users see actual data instead of randomized
            // "completion %" the previous version invented.
            sprints = labels.slice();
            committed = values.slice();
            completed = values.slice();
            n1 = chartDef.mapping?.valueField || 'Committed';
            n2 = 'Completed';
        } else {
            sprints = labels.length ? labels.slice(0,8) : Array.from({length:8},(_,i)=>`Sprint ${i+1}`);
            committed = values.length ? values.slice(0,sprints.length) : [40,42,38,45,43,50,48,52];
            completed = committed.map(v => Math.round(v * (0.7 + Math.random()*0.35)));
            n1 = 'Committed'; n2 = 'Completed';
        }
        return {
            ...opts,
            chart: { ...opts.chart, type: 'bar' },
            series: [
                { name: n1, data: committed },
                { name: n2, data: completed }
            ],
            xaxis: { ...opts.xaxis, categories: sprints },
            colors: [this.hexToRgba(colors[0], 0.4), colors[0]],
            plotOptions: { bar: { borderRadius: 4, columnWidth: '60%' } }
        };
    }

    buildApexSparklineConfig(chartDef, labels, values, style, colors) {
        const vals = values.length ? values : [12,18,15,22,20,25,23,30,28,35];
        return {
            chart: { type: 'line', height: '100%', sparkline: { enabled: true }, animations: { speed: 300 } },
            series: [{ data: vals }],
            colors: [colors[0]],
            stroke: { curve: 'smooth', width: 2 },
            fill: { type: 'gradient', gradient: { shadeIntensity: 1, opacityFrom: 0.4, opacityTo: 0.05, stops: [0, 100] } },
            tooltip: { theme: 'dark', fixed: { enabled: false }, x: { show: false } }
        };
    }

    buildApexProgressBarConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels.slice(0,4) : ['Goal A','Goal B','Goal C','Goal D'];
        const vals = values.length ? values.slice(0,lbls.length).map(v => Math.min(100,Math.abs(v))) : [78,55,91,63];
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'bar' },
            series: [{ name: 'Progress', data: vals }],
            xaxis: { ...opts.xaxis, categories: lbls },
            colors: colors.slice(0, lbls.length),
            plotOptions: { bar: { horizontal: true, borderRadius: 20, barHeight: '40%', distributed: true } },
            yaxis: { max: 100, labels: { formatter: v => v + '%', style: { colors: '#7A90A8' } } },
            legend: { show: false }
        };
    }

    buildApexRadialProgressConfig(chartDef, values, style, colors) {
        const val = Math.min(100, Math.abs(values[0] || 72));
        return {
            chart: { type: 'radialBar', height: '100%', fontFamily: "'Inter', sans-serif", toolbar: { show: false }, animations: { speed: 700 } },
            series: [val],
            plotOptions: {
                radialBar: {
                    hollow: { size: '75%' },
                    dataLabels: {
                        name: { show: true, fontSize: '13px', fontWeight: '600', color: '#1E2D3D', offsetY: 5 },
                        value: { show: true, fontSize: '24px', fontWeight: '700', color: colors[0], offsetY: -15, formatter: v => v + '%' }
                    }
                }
            },
            labels: [chartDef.title || 'Progress'],
            colors: [colors[0]],
            legend: { show: false }
        };
    }

    buildApexNightingaleRoseConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
        const vals = values.length ? values : lbls.map(()=>Math.round(20+Math.random()*60));
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'polarArea' },
            series: vals,
            labels: lbls,
            colors: colors.slice(0, lbls.length),
            fill: { opacity: 0.85 },
            stroke: { colors: ['#fff'], width: 1 }
        };
    }

    buildApexDotPlotConfig(chartDef, labels, values, style, colors) {
        const pts = values.length ? values.map((v,i) => ({ x: labels[i] || String(i), y: v })) :
            [{x:'A',y:45},{x:'A',y:52},{x:'B',y:30},{x:'B',y:38},{x:'C',y:60},{x:'C',y:65},{x:'D',y:25},{x:'D',y:35}];
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'scatter' },
            series: [{ name: chartDef.title || 'Data', data: pts.map(p => ({ x: p.x, y: p.y })) }],
            colors: [colors[0]],
            markers: { size: 7, hover: { size: 9 } }
        };
    }

    buildApexTimeLineConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
        const vals = values.length ? values : [65,72,68,80,75,82,88,91,85,90,95,102];
        const opts = this._baseApexOptions(chartDef);
        return {
            ...opts,
            chart: { ...opts.chart, type: 'area' },
            series: [{ name: chartDef.title || 'Timeline', data: vals }],
            xaxis: { ...opts.xaxis, categories: lbls },
            colors: [colors[0]],
            stroke: { curve: 'smooth', width: 2 },
            fill: { type: 'gradient', gradient: { shadeIntensity: 1, opacityFrom: 0.4, opacityTo: 0.05, stops: [0, 100] } },
            markers: { size: 4 }
        };
    }

    // ============================================================
    // Custom renderers (HTML/Canvas)
    // ============================================================

    renderCustomChart(chartDef, container, data) {
        const ct = chartDef.chartType;
        const h = container.parentElement ? parseInt(container.parentElement.style.height) || 280 : 280;
        const style = chartDef.style || {};
        const palette = style.colorPalette || 'default';
        const colors = this.getColors(palette, 10);

        // Explicit switch avoids dynamic property access with user-controlled input
        switch (ct) {
            case 'treemap':      this.renderTreemap(chartDef, container, data, colors, h); break;
            case 'heatmap':      this.renderHeatmap(chartDef, container, data, colors, h); break;
            case 'sankey':       this.renderSankey(chartDef, container, data, colors, h); break;
            case 'sunburst':     this.renderSunburst(chartDef, container, data, colors, h); break;
            case 'boxPlot':      this.renderBoxPlot(chartDef, container, data, colors, h); break;
            case 'violin':       this.renderViolin(chartDef, container, data, colors, h); break;
            case 'stemLeaf':     this.renderStemLeaf(chartDef, container, data, colors, h); break;
            case 'candlestick':  this.renderCandlestick(chartDef, container, data, colors, h); break;
            case 'ohlc':         this.renderOHLC(chartDef, container, data, colors, h); break;
            case 'eventTimeline': this.renderEventTimeline(chartDef, container, data, colors, h); break;
            case 'choropleth':   this.renderGeoPlaceholder(chartDef, container, data, colors, h, 'Choropleth Map'); break;
            case 'bubbleMap':    this.renderGeoPlaceholder(chartDef, container, data, colors, h, 'Bubble Map'); break;
            case 'heatMapGeo':   this.renderGeoPlaceholder(chartDef, container, data, colors, h, 'Geographic Heat Map'); break;
            case 'flowMap':      this.renderGeoPlaceholder(chartDef, container, data, colors, h, 'Flow Map'); break;
            case 'spikeMap':     this.renderGeoPlaceholder(chartDef, container, data, colors, h, 'Spike Map'); break;
            case 'networkGraph': this.renderNetworkGraph(chartDef, container, data, colors, h); break;
            case 'chordDiagram': this.renderChordDiagram(chartDef, container, data, colors, h); break;
            case 'arcDiagram':   this.renderArcDiagram(chartDef, container, data, colors, h); break;
            case 'forceDirected': this.renderForceDirected(chartDef, container, data, colors, h); break;
            case 'matrix':       this.renderMatrix(chartDef, container, data, colors, h); break;
            case 'waffleChart':  this.renderWaffleChart(chartDef, container, data, colors, h); break;
            case 'pictograph':   this.renderPictograph(chartDef, container, data, colors, h); break;
            case 'kpi':          this.renderKpiCard(chartDef, container, data, colors, h); break;
            case 'kpiCard':      this.renderKpiCard(chartDef, container, data, colors, h); break;
            case 'metricTile':   this.renderMetricTile(chartDef, container, data, colors, h); break;
            case 'card':         this.renderCard(chartDef, container, data, colors, h); break;
            case 'marimekko':    this.renderMarimekko(chartDef, container, data, colors, h); break;
            case 'dumbbell':     this.renderDumbbell(chartDef, container, data, colors, h); break;
            case 'table':        this.renderTableChart(chartDef, container, data, colors, h); break;
            case 'slicer':       this.renderSlicer(chartDef, container, data, colors, h); break;
            case 'navigation':   this.renderNavigationWidget(chartDef, container, colors, h); break;
            default:             this.renderPlaceholder(chartDef, container, colors, h); break;
        }
    }

    _makeCanvas(container, h) {
        const canvas = document.createElement('canvas');
        canvas.width = container.clientWidth || 400;
        canvas.height = h - 10;
        canvas.style.cssText = 'width:100%;height:100%;display:block;';
        container.appendChild(canvas);
        return canvas;
    }

    // Paint a uniform "insufficient data" overlay inside a custom-renderer
    // container. Used to replace the previous Math.random fake-data fallbacks
    // so users see a clear empty state instead of misleading synthetic charts.
    _insufficientDataOverlay(container, chartType) {
        const ct = this._esc(String(chartType || 'this chart'));
        container.style.cssText += 'background:#f8f9fa;display:flex;align-items:center;justify-content:center;';
        container.innerHTML =
            '<div class="chart-no-data" style="padding:20px;text-align:center;color:#6c757d;font-family:Inter,sans-serif">' +
            '<i class="bi bi-bar-chart-line" style="font-size:1.8rem;display:block;margin-bottom:6px;color:#adb5bd"></i>' +
            '<div style="font-size:12px;font-weight:600">Insufficient data</div>' +
            '<div style="font-size:11px;margin-top:2px">' + ct + ' needs more data points</div>' +
            '</div>';
    }

    renderTreemap(chartDef, container, data, colors, h) {
        const canvas = this._makeCanvas(container, h);
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;
        const labels = (data.labels||['A','B','C','D','E','F','G']).slice(0,8);
        const values = labels.map((_,i) => Math.abs((data.values||[])[i] || Math.round(10+Math.random()*90)));
        const total = values.reduce((s,v)=>s+v,0);
        ctx.clearRect(0,0,W,H);
        // Simple slice-and-dice layout
        const items = labels.map((l,i) => ({l, v: values[i]})).sort((a,b)=>b.v-a.v);
        let x = 0;
        items.forEach((item, i) => {
            const w = Math.round(item.v / total * W);
            ctx.fillStyle = colors[i % colors.length] + 'CC';
            ctx.fillRect(x+1, 1, w-2, H-2);
            ctx.strokeStyle = '#fff';
            ctx.lineWidth = 2;
            ctx.strokeRect(x+1,1,w-2,H-2);
            if (w > 30) {
                ctx.fillStyle = '#fff';
                ctx.font = `bold ${Math.min(14, w/4)}px Inter,sans-serif`;
                ctx.textAlign = 'center';
                ctx.fillText(item.l, x + w/2, H/2 - 6);
                ctx.font = `${Math.min(11, w/5)}px Inter,sans-serif`;
                ctx.fillText(item.v, x + w/2, H/2 + 12);
            }
            x += w;
        });
    }

    renderHeatmap(chartDef, container, data, colors, h) {
        if ((!data.labels || !data.labels.length) && (!data.values || !data.values.length) && (!data.rawData || !data.rawData.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const canvas = this._makeCanvas(container, h);
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;
        const rows = ['Mon','Tue','Wed','Thu','Fri'];
        const cols = ['9am','10am','11am','12pm','1pm','2pm','3pm','4pm','5pm'];
        const cellW = Math.floor((W-40) / cols.length);
        const cellH = Math.floor((H-20) / rows.length);
        const vals = rows.map(() => cols.map(() => Math.random()));
        const maxVal = 1;
        ctx.clearRect(0,0,W,H);
        ctx.fillStyle = '#666'; ctx.font = '10px Inter,sans-serif';
        cols.forEach((c,j) => { ctx.fillText(c, 40+j*cellW+cellW/2-10, 12); });
        rows.forEach((r,i) => {
            ctx.fillStyle = '#444'; ctx.fillText(r, 2, 20+i*cellH+cellH/2+4);
            cols.forEach((_,j) => {
                const v = vals[i][j];
                const alpha = 0.15 + v * 0.85;
                ctx.fillStyle = this.hexToRgba(colors[0], alpha);
                ctx.fillRect(40+j*cellW+1, 18+i*cellH+1, cellW-2, cellH-2);
            });
        });
    }

    renderSankey(chartDef, container, data, colors, h) {
        if ((!data.rawData || !data.rawData.length) && (!data.labels || !data.labels.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const canvas = this._makeCanvas(container, h);
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;
        // Simple 3-level sankey approximation
        const nodes = [
            {name:'Revenue',x:0.05,y:0.2,h:0.6},
            {name:'Product',x:0.38,y:0.05,h:0.35},
            {name:'Service',x:0.38,y:0.45,h:0.25},
            {name:'Other',x:0.38,y:0.75,h:0.15},
            {name:'EMEA',x:0.72,y:0.05,h:0.25},
            {name:'AMER',x:0.72,y:0.35,h:0.3},
            {name:'APAC',x:0.72,y:0.7,h:0.2}
        ];
        const links = [
            {src:0,dst:1,v:0.35},{src:0,dst:2,v:0.25},{src:0,dst:3,v:0.15},
            {src:1,dst:4,v:0.15},{src:1,dst:5,v:0.2},{src:2,dst:5,v:0.15},{src:2,dst:6,v:0.1},{src:3,dst:6,v:0.1}
        ];
        const nw = 16;
        ctx.clearRect(0,0,W,H);
        // Draw links
        links.forEach((link,i) => {
            const s = nodes[link.src], d = nodes[link.dst];
            const sx = s.x*W+nw, sy = s.y*H + s.h*H*0.5;
            const dx = d.x*W, dy = d.y*H + d.h*H*0.5;
            ctx.beginPath();
            ctx.moveTo(sx, sy);
            ctx.bezierCurveTo(sx+60, sy, dx-60, dy, dx, dy);
            ctx.strokeStyle = colors[i % colors.length] + '88';
            ctx.lineWidth = Math.max(2, link.v * H * 0.5);
            ctx.stroke();
        });
        // Draw nodes
        nodes.forEach((n, i) => {
            ctx.fillStyle = colors[i % colors.length] + 'CC';
            ctx.fillRect(n.x*W, n.y*H, nw, n.h*H);
            ctx.fillStyle = '#333'; ctx.font = '11px Inter,sans-serif';
            ctx.fillText(n.name, n.x*W + nw + 4, n.y*H + n.h*H/2 + 4);
        });
    }

    renderSunburst(chartDef, container, data, colors, h) {
        if ((!data.rawData || !data.rawData.length) && (!data.labels || !data.labels.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const canvas = this._makeCanvas(container, h);
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;
        const cx = W/2, cy = H/2, maxR = Math.min(W,H)/2 - 10;
        ctx.clearRect(0,0,W,H);
        // 3-ring sunburst
        const rings = [
            [{label:'Total',val:1}],
            [{label:'A',val:0.4},{label:'B',val:0.35},{label:'C',val:0.25}],
            [{label:'A1',val:0.2},{label:'A2',val:0.2},{label:'B1',val:0.15},{label:'B2',val:0.2},{label:'C1',val:0.25}]
        ];
        const ringW = maxR/rings.length;
        rings.forEach((ring, ri) => {
            let angle = -Math.PI/2;
            ring.forEach((seg, si) => {
                const sweep = seg.val * 2 * Math.PI;
                ctx.beginPath();
                ctx.moveTo(cx, cy);
                ctx.arc(cx, cy, (ri+1)*ringW, angle, angle+sweep);
                ctx.closePath();
                ctx.fillStyle = colors[(ri*3+si) % colors.length] + 'CC';
                ctx.fill();
                ctx.strokeStyle = '#fff'; ctx.lineWidth = 2; ctx.stroke();
                const midAngle = angle + sweep/2;
                const labelR = ri*ringW + ringW/2 + (ri===0?0:0);
                const lx = cx + Math.cos(midAngle) * (ri*ringW + ringW*0.6);
                const ly = cy + Math.sin(midAngle) * (ri*ringW + ringW*0.6);
                if (sweep > 0.15) {
                    ctx.fillStyle = '#fff'; ctx.font = `${ri===0?12:10}px Inter,sans-serif`;
                    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
                    ctx.fillText(seg.label, lx, ly);
                }
                angle += sweep;
            });
        });
    }

    renderBoxPlot(chartDef, container, data, colors, h) {
        if ((!data.rawData || !data.rawData.length) && (!data.labels || !data.labels.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const canvas = this._makeCanvas(container, h);
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;
        const groups = (data.labels||['Group A','Group B','Group C']).slice(0,4);
        const stats = groups.map(() => {
            const sorted = Array.from({length:20},()=>Math.round(20+Math.random()*80)).sort((a,b)=>a-b);
            return {
                min: sorted[0], q1: sorted[4], median: sorted[9],
                q3: sorted[14], max: sorted[19]
            };
        });
        const allVals = stats.flatMap(s=>[s.min,s.max]);
        const minV = Math.min(...allVals)*0.9, maxV = Math.max(...allVals)*1.05;
        const pad = 40, bw = Math.min(50, (W-2*pad)/groups.length/2);
        ctx.clearRect(0,0,W,H);
        const toY = v => pad + (1 - (v-minV)/(maxV-minV)) * (H-2*pad);
        groups.forEach((g, i) => {
            const cx = pad + (i+0.5) * (W-2*pad) / groups.length;
            const s = stats[i];
            const color = colors[i % colors.length];
            // Whiskers
            ctx.beginPath(); ctx.moveTo(cx, toY(s.max)); ctx.lineTo(cx, toY(s.min));
            ctx.strokeStyle = color; ctx.lineWidth = 1.5; ctx.stroke();
            // Box
            ctx.fillStyle = color+'44'; ctx.strokeStyle = color; ctx.lineWidth = 2;
            ctx.fillRect(cx-bw/2, toY(s.q3), bw, toY(s.q1)-toY(s.q3));
            ctx.strokeRect(cx-bw/2, toY(s.q3), bw, toY(s.q1)-toY(s.q3));
            // Median line
            ctx.beginPath(); ctx.moveTo(cx-bw/2, toY(s.median)); ctx.lineTo(cx+bw/2, toY(s.median));
            ctx.strokeStyle = color; ctx.lineWidth = 3; ctx.stroke();
            // Whisker caps
            [[s.min],[s.max]].forEach(([v]) => {
                ctx.beginPath(); ctx.moveTo(cx-bw/4, toY(v)); ctx.lineTo(cx+bw/4, toY(v));
                ctx.lineWidth = 1.5; ctx.stroke();
            });
            ctx.fillStyle = '#333'; ctx.font = '10px Inter,sans-serif'; ctx.textAlign = 'center';
            ctx.fillText(g, cx, H-5);
        });
    }

    renderViolin(chartDef, container, data, colors, h) {
        if ((!data.rawData || !data.rawData.length) && (!data.labels || !data.labels.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const canvas = this._makeCanvas(container, h);
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;
        const groups = (data.labels||['Alpha','Beta','Gamma']).slice(0,4);
        ctx.clearRect(0,0,W,H);
        const pad = 30;
        groups.forEach((g, i) => {
            const cx = pad + (i+0.5)*(W-2*pad)/groups.length;
            const color = colors[i%colors.length];
            const pts = 20;
            const mean = 40 + Math.random()*30;
            const widths = Array.from({length:pts}, (_,k) => {
                const y = k/pts;
                return Math.exp(-0.5*Math.pow((y-0.5)/0.2,2)) * 25 * (0.7+Math.random()*0.6);
            });
            ctx.beginPath();
            for (let k=0;k<pts;k++) {
                const y = pad + k*(H-2*pad)/pts;
                ctx.lineTo(cx+widths[k], y);
            }
            for (let k=pts-1;k>=0;k--) {
                const y = pad + k*(H-2*pad)/pts;
                ctx.lineTo(cx-widths[k], y);
            }
            ctx.closePath();
            ctx.fillStyle = color+'AA'; ctx.fill();
            ctx.strokeStyle = color; ctx.lineWidth = 2; ctx.stroke();
            ctx.fillStyle = '#333'; ctx.font = '10px Inter,sans-serif'; ctx.textAlign='center';
            ctx.fillText(g, cx, H-5);
        });
    }

    renderStemLeaf(chartDef, container, data, colors, h) {
        const values = (data.values||[25,32,35,41,48,52,55,58,61,67,72,78,81,85,93]).sort((a,b)=>a-b);
        container.style.cssText += 'background:#fff;padding:12px;overflow:auto;';
        const stems = {};
        values.forEach(v => {
            const stem = Math.floor(v/10);
            (stems[stem]||(stems[stem]=[])).push(v%10);
        });
        let html = `<div style="font-family:monospace;font-size:13px;line-height:1.6;color:#333">
            <div style="font-weight:600;margin-bottom:6px;font-family:Inter,sans-serif;font-size:12px">Stem &amp; Leaf Plot — ${this._esc(chartDef.title||'')}</div>`;
        Object.keys(stems).sort((a,b)=>+a-+b).forEach(stem => {
            // stem is a numeric key; leaf values are single digits — no user HTML
            const safeStem = parseInt(stem, 10);
            const safeLeaves = stems[stem].map(d => parseInt(d, 10)).join(' ');
            html += `<div><span style="display:inline-block;width:28px;text-align:right;color:${colors[0]};font-weight:600">${safeStem}</span><span style="color:#999;margin:0 6px">|</span>${safeLeaves}</div>`;
        });
        html += '</div>';
        container.innerHTML = html;
    }

    _genOHLC(n) {
        const data = [];
        let price = 100;
        const now = Date.now();
        for (let i = 0; i < n; i++) {
            const open = price;
            const change = (Math.random()-0.48)*8;
            const close = +(open + change).toFixed(2);
            const high = +(Math.max(open,close) + Math.random()*3).toFixed(2);
            const low  = +(Math.min(open,close) - Math.random()*3).toFixed(2);
            data.push({t: new Date(now - (n-i)*86400000).toLocaleDateString('en',{month:'short',day:'numeric'}), o:open, h:high, l:low, c:close});
            price = close;
        }
        return data;
    }

    renderCandlestick(chartDef, container, data, colors, h) {
        if ((!data.rawData || !data.rawData.length) && (!data.values || !data.values.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const canvas = this._makeCanvas(container, h);
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;
        const ohlc = this._genOHLC(20);
        const allPrices = ohlc.flatMap(d=>[d.h,d.l]);
        const minP = Math.min(...allPrices)*0.995, maxP = Math.max(...allPrices)*1.005;
        const pad = {l:40,r:10,t:20,b:20};
        const cw = (W-pad.l-pad.r)/ohlc.length;
        ctx.clearRect(0,0,W,H);
        const toY = v => pad.t + (1-(v-minP)/(maxP-minP))*(H-pad.t-pad.b);
        // Y axis labels
        ctx.fillStyle='#666'; ctx.font='9px Inter,sans-serif'; ctx.textAlign='right';
        [0,0.25,0.5,0.75,1].forEach(frac => {
            const v = minP + frac*(maxP-minP);
            ctx.fillText(v.toFixed(1), pad.l-3, toY(v)+4);
        });
        ohlc.forEach((bar, i) => {
            const x = pad.l + i*cw + cw/2;
            const up = bar.c >= bar.o;
            ctx.strokeStyle = up ? '#4CAF50' : '#E87C3E';
            ctx.fillStyle = up ? '#4CAF50CC' : '#E87C3ECC';
            // Wick
            ctx.beginPath(); ctx.moveTo(x, toY(bar.h)); ctx.lineTo(x, toY(bar.l));
            ctx.lineWidth=1; ctx.stroke();
            // Body
            const bw = cw*0.6;
            const y1 = toY(Math.max(bar.o,bar.c)), y2 = toY(Math.min(bar.o,bar.c));
            ctx.fillRect(x-bw/2, y1, bw, Math.max(2,y2-y1));
            ctx.strokeRect(x-bw/2, y1, bw, Math.max(2,y2-y1));
        });
        ctx.fillStyle='#333'; ctx.font='9px Inter,sans-serif'; ctx.textAlign='center';
        ohlc.filter((_,i)=>i%4===0).forEach((bar,i) => {
            ctx.fillText(bar.t, pad.l+(i*4)*cw+cw/2, H-3);
        });
    }

    renderOHLC(chartDef, container, data, colors, h) {
        if ((!data.rawData || !data.rawData.length) && (!data.values || !data.values.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const canvas = this._makeCanvas(container, h);
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;
        const ohlc = this._genOHLC(20);
        const allPrices = ohlc.flatMap(d=>[d.h,d.l]);
        const minP = Math.min(...allPrices)*0.995, maxP = Math.max(...allPrices)*1.005;
        const pad = {l:40,r:10,t:20,b:20};
        const cw = (W-pad.l-pad.r)/ohlc.length;
        ctx.clearRect(0,0,W,H);
        const toY = v => pad.t + (1-(v-minP)/(maxP-minP))*(H-pad.t-pad.b);
        ohlc.forEach((bar, i) => {
            const x = pad.l + i*cw + cw/2;
            const up = bar.c >= bar.o;
            ctx.strokeStyle = up ? '#4CAF50' : '#E87C3E';
            ctx.lineWidth = 1.5;
            // High-low line
            ctx.beginPath(); ctx.moveTo(x, toY(bar.h)); ctx.lineTo(x, toY(bar.l)); ctx.stroke();
            // Open tick (left)
            const tw = cw*0.35;
            ctx.beginPath(); ctx.moveTo(x-tw, toY(bar.o)); ctx.lineTo(x, toY(bar.o)); ctx.stroke();
            // Close tick (right)
            ctx.beginPath(); ctx.moveTo(x, toY(bar.c)); ctx.lineTo(x+tw, toY(bar.c)); ctx.stroke();
        });
    }

    renderEventTimeline(chartDef, container, data, colors, h) {
        const events = data.labels ? data.labels.slice(0,6).map((l,i) => ({label:l, val: (data.values||[])[i]||'', color:colors[i%colors.length]})) :
            ['Kickoff','Alpha Release','Beta Launch','User Testing','GA Release','Post-Launch Review'].map((l,i)=>({label:l,val:`Month ${i+1}`,color:colors[i%colors.length]}));
        container.style.cssText += 'background:#fff;padding:16px 12px;overflow:auto;';
        let html = `<div style="position:relative;padding-left:24px">
            <div style="position:absolute;left:10px;top:0;bottom:0;width:2px;background:${colors[0]}44;border-radius:1px"></div>`;
        events.forEach((ev,i) => {
            html += `<div style="position:relative;margin-bottom:18px;display:flex;align-items:flex-start;gap:10px">
                <div style="position:absolute;left:-18px;top:3px;width:12px;height:12px;border-radius:50%;background:${ev.color};border:2px solid #fff;box-shadow:0 0 0 2px ${ev.color}44"></div>
                <div>
                    <div style="font-size:12px;font-weight:600;color:#2c3e50">${this._esc(ev.label)}</div>
                    <div style="font-size:11px;color:#8492a6">${this._esc(String(ev.val))}</div>
                </div>
            </div>`;
        });
        html += '</div>';
        container.innerHTML = html;
    }

    renderGeoPlaceholder(chartDef, container, data, colors, h, typeName) {
        const lbls = data.labels && data.labels.length ? data.labels : ['USA','Germany','Japan','UK','Brazil','Canada','Australia','France','India','Mexico'];
        const vals = lbls.map((_,i) => (data.values||[])[i] || Math.round(20+Math.random()*80));
        const maxV = Math.max(...vals);
        container.style.cssText += 'background:#f8f9fa;padding:12px;overflow:auto;';
        let html = `<div style="font-family:Inter,sans-serif">
            <div style="text-align:center;color:#4A90D9;font-size:42px;margin-bottom:6px">🗺️</div>
            <div style="text-align:center;font-size:12px;font-weight:600;color:#2c3e50;margin-bottom:10px">${this._esc(typeName)} — ${this._esc(chartDef.title||'Geographic')}</div>
            <div style="font-size:10px;color:#8492a6;text-align:center;margin-bottom:10px">Geographic visualization (requires map library)</div>`;
        lbls.slice(0,6).forEach((l,i) => {
            const pct = Math.max(0, Math.min(100, Math.round(vals[i]/maxV*100)));
            const safeVal = Math.round(Number(vals[i]) || 0);
            html += `<div style="display:flex;align-items:center;gap:6px;margin-bottom:5px">
                <div style="width:60px;font-size:10px;color:#333;text-align:right">${this._esc(String(l))}</div>
                <div style="flex:1;height:12px;background:#e9ecef;border-radius:6px;overflow:hidden">
                    <div style="height:100%;width:${pct}%;background:${colors[i%colors.length]};border-radius:6px"></div>
                </div>
                <div style="width:28px;font-size:10px;color:#666">${safeVal}</div>
            </div>`;
        });
        html += '</div>';
        container.innerHTML = html;
    }

    renderNetworkGraph(chartDef, container, data, colors, h) {
        if ((!data.rawData || !data.rawData.length) && (!data.labels || !data.labels.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const canvas = this._makeCanvas(container, h);
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;
        const nodeCount = 8;
        const nodes = Array.from({length:nodeCount}, (_,i) => ({
            x: W*0.15 + Math.random()*(W*0.7),
            y: H*0.1 + Math.random()*(H*0.8),
            r: 8 + Math.random()*12,
            label: (data.labels||[])[i] || `Node ${i+1}`,
            color: colors[i%colors.length]
        }));
        const edges = [];
        for (let i=0;i<nodeCount;i++) for (let j=i+1;j<nodeCount;j++) if (Math.random()<0.35) edges.push([i,j]);
        ctx.clearRect(0,0,W,H);
        edges.forEach(([a,b]) => {
            ctx.beginPath(); ctx.moveTo(nodes[a].x, nodes[a].y); ctx.lineTo(nodes[b].x, nodes[b].y);
            ctx.strokeStyle='#adb5bd88'; ctx.lineWidth=1.5; ctx.stroke();
        });
        nodes.forEach(n => {
            ctx.beginPath(); ctx.arc(n.x, n.y, n.r, 0, Math.PI*2);
            ctx.fillStyle=n.color+'CC'; ctx.fill();
            ctx.strokeStyle='#fff'; ctx.lineWidth=2; ctx.stroke();
            ctx.fillStyle='#333'; ctx.font='9px Inter,sans-serif'; ctx.textAlign='center';
            ctx.fillText(n.label.length>8?n.label.slice(0,7)+'…':n.label, n.x, n.y+n.r+10);
        });
    }

    renderChordDiagram(chartDef, container, data, colors, h) {
        if ((!data.rawData || !data.rawData.length) && (!data.labels || !data.labels.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const canvas = this._makeCanvas(container, h);
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;
        const cx = W/2, cy = H/2, R = Math.min(W,H)/2 - 40, arcW = 18;
        const groups = (data.labels||['Alpha','Beta','Gamma','Delta','Epsilon']).slice(0,5);
        const n = groups.length;
        const matrix = Array.from({length:n}, () => Array.from({length:n}, () => Math.random()));
        ctx.clearRect(0,0,W,H);
        const angleStep = (2*Math.PI) / n;
        // Draw arcs for groups
        groups.forEach((g,i) => {
            const startA = i*angleStep - Math.PI/2;
            const endA = startA + angleStep - 0.05;
            ctx.beginPath(); ctx.arc(cx, cy, R, startA, endA);
            ctx.lineWidth = arcW; ctx.strokeStyle = colors[i%colors.length]+'CC'; ctx.stroke();
            const midA = (startA+endA)/2;
            ctx.fillStyle='#333'; ctx.font='11px Inter,sans-serif'; ctx.textAlign='center';
            ctx.fillText(g, cx+Math.cos(midA)*(R+arcW+10), cy+Math.sin(midA)*(R+arcW+10)+4);
        });
        // Draw chords
        for (let i=0;i<n;i++) for (let j=i+1;j<n;j++) {
            if (matrix[i][j] < 0.4) continue;
            const ai = i*angleStep - Math.PI/2 + angleStep/2;
            const aj = j*angleStep - Math.PI/2 + angleStep/2;
            ctx.beginPath();
            ctx.moveTo(cx+Math.cos(ai)*R, cy+Math.sin(ai)*R);
            ctx.quadraticCurveTo(cx, cy, cx+Math.cos(aj)*R, cy+Math.sin(aj)*R);
            ctx.strokeStyle=colors[i%colors.length]+'66'; ctx.lineWidth=2; ctx.stroke();
        }
    }

    renderArcDiagram(chartDef, container, data, colors, h) {
        if ((!data.rawData || !data.rawData.length) && (!data.labels || !data.labels.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const canvas = this._makeCanvas(container, h);
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;
        const nodes = (data.labels||['A','B','C','D','E','F','G']).slice(0,7);
        const n = nodes.length;
        const pad = 40, spacing = (W-2*pad) / (n-1);
        const cy = H * 0.65;
        ctx.clearRect(0,0,W,H);
        const xs = nodes.map((_,i) => pad + i*spacing);
        // Draw arcs
        for (let i=0;i<n;i++) for (let j=i+1;j<n;j++) {
            if (Math.random() < 0.5) continue;
            const x1=xs[i], x2=xs[j], mx=(x1+x2)/2, rx=(x2-x1)/2;
            const ry = rx * (0.4 + Math.random()*0.4);
            ctx.beginPath(); ctx.ellipse(mx, cy, rx, ry, 0, Math.PI, 0, true);
            ctx.strokeStyle=colors[i%colors.length]+'88'; ctx.lineWidth=1.5; ctx.stroke();
        }
        // Draw nodes
        nodes.forEach((node,i) => {
            ctx.beginPath(); ctx.arc(xs[i], cy, 7, 0, Math.PI*2);
            ctx.fillStyle=colors[i%colors.length]+'CC'; ctx.fill();
            ctx.strokeStyle='#fff'; ctx.lineWidth=2; ctx.stroke();
            ctx.fillStyle='#333'; ctx.font='10px Inter,sans-serif'; ctx.textAlign='center';
            ctx.fillText(node, xs[i], cy+20);
        });
    }

    renderForceDirected(chartDef, container, data, colors, h) {
        if ((!data.rawData || !data.rawData.length) && (!data.labels || !data.labels.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const canvas = this._makeCanvas(container, h);
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;
        const n = 10;
        // Simple spring relaxation
        let nodes = Array.from({length:n}, (_,i) => ({
            x: W/2 + (Math.random()-0.5)*W*0.5,
            y: H/2 + (Math.random()-0.5)*H*0.5,
            vx:0, vy:0,
            label: (data.labels||[])[i] || `N${i+1}`,
            color: colors[i%colors.length],
            r: 8 + Math.random()*8
        }));
        const edges = [];
        for (let i=0;i<n;i++) for (let j=i+1;j<n;j++) if (Math.random()<0.3) edges.push([i,j]);
        // Quick simulation
        for (let iter=0;iter<80;iter++) {
            nodes.forEach((a,i) => {
                let fx=0, fy=0;
                nodes.forEach((b,j) => {
                    if (i===j) return;
                    const dx=a.x-b.x, dy=a.y-b.y, dist=Math.sqrt(dx*dx+dy*dy)||1;
                    const repel = 800/(dist*dist);
                    fx += dx/dist*repel; fy += dy/dist*repel;
                });
                edges.forEach(([ei,ej]) => {
                    if (ei!==i&&ej!==i) return;
                    const other = nodes[ei===i?ej:ei];
                    const dx=a.x-other.x, dy=a.y-other.y, dist=Math.sqrt(dx*dx+dy*dy)||1;
                    const spring = (dist-80)*0.05;
                    fx -= dx/dist*spring; fy -= dy/dist*spring;
                });
                // center gravity
                fx += (W/2-a.x)*0.01; fy += (H/2-a.y)*0.01;
                a.vx=(a.vx+fx)*0.5; a.vy=(a.vy+fy)*0.5;
            });
            nodes.forEach(a => {
                a.x = Math.max(20,Math.min(W-20,a.x+a.vx));
                a.y = Math.max(20,Math.min(H-20,a.y+a.vy));
            });
        }
        ctx.clearRect(0,0,W,H);
        edges.forEach(([i,j]) => {
            ctx.beginPath(); ctx.moveTo(nodes[i].x,nodes[i].y); ctx.lineTo(nodes[j].x,nodes[j].y);
            ctx.strokeStyle='#adb5bd88'; ctx.lineWidth=1.5; ctx.stroke();
        });
        nodes.forEach(nd => {
            ctx.beginPath(); ctx.arc(nd.x, nd.y, nd.r, 0, Math.PI*2);
            ctx.fillStyle=nd.color+'CC'; ctx.fill(); ctx.strokeStyle='#fff'; ctx.lineWidth=2; ctx.stroke();
            ctx.fillStyle='#333'; ctx.font='9px Inter,sans-serif'; ctx.textAlign='center';
            ctx.fillText(nd.label, nd.x, nd.y+nd.r+10);
        });
    }

    renderMatrix(chartDef, container, data, colors, h) {
        if ((!data.rawData || !data.rawData.length) && (!data.labels || !data.labels.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const lbls = (data.labels||['Alpha','Beta','Gamma','Delta','Epsilon']).slice(0,5);
        const n = lbls.length;
        const matrix = Array.from({length:n}, (_,i) => Array.from({length:n}, (_,j) => Math.round(Math.random()*100)));
        container.style.cssText += 'background:#fff;padding:12px;overflow:auto;';
        let html = `<div style="font-size:11px;font-family:Inter,sans-serif">
            <div style="margin-bottom:8px;font-weight:600;color:#2c3e50">${this._esc(chartDef.title||'Matrix Chart')}</div>
            <table style="border-collapse:collapse;font-size:10px">
            <tr><th style="padding:4px"></th>${lbls.map(l=>`<th style="padding:4px;color:${colors[0]};text-align:center">${this._esc(l)}</th>`).join('')}</tr>`;
        matrix.forEach((row,i) => {
            const maxRow = Math.max(...row);
            html += `<tr><td style="padding:4px;font-weight:600;color:${colors[i%colors.length]};white-space:nowrap">${this._esc(lbls[i])}</td>` +
                row.map((v,j) => {
                    const safeV = Math.round(Number(v) || 0);
                    const alpha = (0.1 + safeV/100*0.9).toFixed(2);
                    return `<td style="padding:4px;text-align:center;background:${colors[(i+j)%colors.length]}${Math.round(parseFloat(alpha)*255).toString(16).padStart(2,'0')};border-radius:3px">${safeV}</td>`;
                }).join('') + '</tr>';
        });
        html += '</table></div>';
        container.innerHTML = html;
    }

    renderWaffleChart(chartDef, container, data, colors, h) {
        if ((!data.labels || !data.labels.length) || (!data.values || !data.values.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const lbls = (data.labels||['A','B','C','D']).slice(0,4);
        const vals = lbls.map((_,i) => Math.abs((data.values||[])[i]||Math.round(10+Math.random()*40)));
        const total = vals.reduce((s,v)=>s+v,0);
        const pcts = vals.map(v => Math.max(0, Math.min(100, Math.round(v/total*100))));
        const cells = 100, cellsPerRow = 10;
        let colored = [];
        pcts.forEach((p,i) => { for (let k=0;k<p;k++) colored.push(i); });
        const cellSize = Math.floor(Math.min((h-50)/cellsPerRow, ((container.clientWidth||300)-20)/cellsPerRow));
        container.style.cssText += 'background:#fff;padding:10px;overflow:auto;display:flex;flex-direction:column;align-items:center;';
        let gridHtml = `<div style="display:grid;grid-template-columns:repeat(${cellsPerRow},${cellSize}px);gap:2px;margin-bottom:10px">`;
        for (let i=0;i<cells;i++) {
            const colorIdx = colored[i] !== undefined ? colored[i] : -1;
            const bg = colorIdx >= 0 ? colors[colorIdx % colors.length] : '#e9ecef';
            gridHtml += `<div style="width:${cellSize}px;height:${cellSize}px;background:${bg};border-radius:2px"></div>`;
        }
        gridHtml += '</div>';
        let legend = `<div style="display:flex;flex-wrap:wrap;gap:8px;justify-content:center">`;
        lbls.forEach((l,i) => {
            legend += `<div style="display:flex;align-items:center;gap:4px;font-size:11px;font-family:Inter,sans-serif"><div style="width:10px;height:10px;border-radius:2px;background:${colors[i%colors.length]}"></div>${this._esc(l)} (${pcts[i]}%)</div>`;
        });
        legend += '</div>';
        container.innerHTML = gridHtml + legend;
    }

    renderPictograph(chartDef, container, data, colors, h) {
        if ((!data.labels || !data.labels.length) || (!data.values || !data.values.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const lbls = (data.labels||['Team A','Team B','Team C','Team D']).slice(0,5);
        const vals = lbls.map((_,i) => Math.round(Math.abs((data.values||[])[i]||Math.round(3+Math.random()*7))));
        const icons = ['👤','🏠','📦','💰','⭐','🎯','🏆','📊'];
        const maxVal = Math.max(...vals);
        container.style.cssText += 'background:#fff;padding:12px;overflow:auto;';
        let html = `<div style="font-family:Inter,sans-serif;font-size:12px;font-weight:600;color:#2c3e50;margin-bottom:10px">${this._esc(chartDef.title||'Pictograph')}</div>`;
        lbls.forEach((l, i) => {
            const icon = icons[i % icons.length];
            const n = vals[i];
            html += `<div style="display:flex;align-items:center;gap:6px;margin-bottom:8px">
                <div style="width:70px;font-size:10px;color:#555;text-align:right">${this._esc(l)}</div>
                <div style="flex:1;font-size:18px;line-height:1">${icon.repeat(n)}</div>
                <div style="font-size:10px;color:#8492a6;width:24px">${n}</div>
            </div>`;
        });
        container.innerHTML = html;
    }

    renderKpiCard(chartDef, container, data, colors, h) {
        const val = this._scalarFromData(data, chartDef);
        const color = colors[0] || '#4A90D9';
        const title = chartDef.title || 'KPI';
        const display = (val === null || val === undefined)
            ? '—'
            : (typeof val === 'number' ? val.toLocaleString(undefined, { maximumFractionDigits: 2 }) : String(val));
        const len = display.length;
        // Adapt value font to BOTH width and height so the title above is
        // never pushed out of view on short cards.
        const cw = container.clientWidth || container.offsetWidth || 240;
        const ch = container.clientHeight || container.offsetHeight || h || 160;
        let valFont = len > 12 ? 30 : (len > 8 ? 38 : 46);
        if (ch < 110) valFont = Math.min(valFont, 22);
        else if (ch < 140) valFont = Math.min(valFont, 28);
        else if (ch < 170) valFont = Math.min(valFont, 34);
        if (cw < 160) valFont = Math.min(valFont, 24);
        else if (cw < 220) valFont = Math.min(valFont, 32);

        // Alignment (defaults: horizontal=center, vertical=middle).
        const hAlign = chartDef.style?.kpiHAlign || 'center';
        const vAlign = chartDef.style?.kpiVAlign || 'middle';
        const hFlex = hAlign === 'left' ? 'flex-start' : (hAlign === 'right' ? 'flex-end' : 'center');
        const vFlex = vAlign === 'top' ? 'flex-start' : (vAlign === 'bottom' ? 'flex-end' : 'center');
        const textAlign = hAlign;

        // Derive a progress percentage for the bar visual.
        const vals = (data && Array.isArray(data.values)) ? data.values.filter(v => typeof v === 'number' && isFinite(v)) : [];
        let pct = 100;
        let deltaHtml = '';
        if (vals.length > 1) {
            const maxV = Math.max(...vals);
            const curV = vals[vals.length - 1];
            if (maxV > 0) pct = Math.max(2, Math.min(100, Math.round((curV / maxV) * 100)));
            const prev = vals[vals.length - 2];
            if (typeof prev === 'number' && prev !== 0) {
                const d = ((curV - prev) / Math.abs(prev)) * 100;
                const up = d >= 0;
                const arrow = up ? '▲' : '▼';
                const dColor = up ? '#16a34a' : '#dc2626';
                deltaHtml = `<span style="font-size:11px;font-weight:600;color:${dColor};margin-left:8px;white-space:nowrap">${arrow} ${Math.abs(d).toFixed(1)}%</span>`;
            }
        }

        container.style.cssText += `background:#fff;display:block;border-radius:8px;overflow:hidden;`;
        const titleFont = cw < 160 ? 10 : (cw < 220 ? 10.5 : 11.5);
        const padX = cw < 160 ? 10 : 18;
        const padY = cw < 160 ? 8 : (ch < 140 ? 10 : 16);
        const gap = ch < 120 ? 4 : (ch < 160 ? 6 : 10);
        container.innerHTML = `<div style="font-family:Inter,sans-serif;padding:${padY}px ${padX}px;width:100%;height:100%;box-sizing:border-box;display:flex;flex-direction:column;justify-content:${vFlex};align-items:stretch;gap:${gap}px;text-align:${textAlign};overflow:hidden;min-height:0">
            <div style="flex:0 0 auto;font-size:${titleFont}px;font-weight:600;text-transform:uppercase;letter-spacing:0.5px;color:#64748b;line-height:1.3;word-break:break-word;overflow-wrap:anywhere;white-space:normal">${this._esc(title)}</div>
            <div style="flex:0 1 auto;min-height:0;display:flex;align-items:baseline;justify-content:${hFlex};flex-wrap:wrap">
                <div style="font-size:${valFont}px;font-weight:700;color:#1e293b;line-height:1.15;white-space:normal;word-break:break-word;overflow-wrap:anywhere;max-width:100%">${this._esc(display)}</div>
                ${deltaHtml}
            </div>
            <div style="flex:0 0 auto;width:100%;height:8px;background:${color}1A;border-radius:999px;overflow:hidden;position:relative">
                <div style="position:absolute;left:0;top:0;bottom:0;width:${pct}%;background:linear-gradient(90deg,${color} 0%,${color}CC 100%);border-radius:999px;transition:width 0.4s ease"></div>
            </div>
        </div>`;
    }

    // Extract a single scalar from a data payload. When the chart has no
    // aggregation configured (valueFieldAgg === 'None' / aggregation disabled),
    // surface the FIRST row's value so users see real underlying data instead
    // of an auto-sum of every row. Otherwise sum numeric rows so multi-row
    // queries still render a sensible KPI.
    _scalarFromData(data, chartDef) {
        const vals = (data && data.values) || [];
        if (!vals.length) return null;
        if (vals.length === 1) return vals[0];
        const aggFn = String(
            (chartDef && chartDef.mapping && chartDef.mapping.valueFieldAgg) ||
            (chartDef && chartDef.aggregation && chartDef.aggregation.function) ||
            ''
        ).toLowerCase();
        const aggDisabled = chartDef && chartDef.aggregation && chartDef.aggregation.enabled === false;
        if (aggFn === 'none' || aggDisabled) {
            return vals[0];
        }
        const nums = vals.filter(v => typeof v === 'number' && isFinite(v));
        if (!nums.length) return vals[0];
        return nums.reduce((s, v) => s + v, 0);
    }

    renderMetricTile(chartDef, container, data, colors, h) {
        const metrics = [
            { label: (data.labels||[])[0]||'Revenue',    val: (data.values||[])[0]||98400,  icon:'💰', color:colors[0] },
            { label: (data.labels||[])[1]||'Users',      val: (data.values||[])[1]||14200,  icon:'👥', color:colors[1] },
            { label: (data.labels||[])[2]||'Conversion', val: (data.values||[])[2]||3.8,    icon:'📈', color:colors[2] },
            { label: (data.labels||[])[3]||'Avg Order',  val: (data.values||[])[3]||127,    icon:'🛒', color:colors[3] },
        ];
        container.style.cssText += 'background:#f8f9fa;display:grid;grid-template-columns:1fr 1fr;gap:8px;padding:10px;';
        container.innerHTML = metrics.map(m => `
            <div style="background:#fff;border-radius:8px;padding:12px;box-shadow:0 1px 4px rgba(0,0,0,0.07)">
                <div style="font-size:18px">${m.icon}</div>
                <div style="font-size:18px;font-weight:700;color:${m.color};line-height:1.2;margin-top:4px">${typeof m.val==='number'?m.val.toLocaleString():m.val}</div>
                <div style="font-size:10px;color:#8492a6;margin-top:2px">${this._esc(m.label)}</div>
            </div>`).join('');
    }

    renderCard(chartDef, container, data, colors, h) {
        const title = chartDef.title || 'Value';
        const val = this._scalarFromData(data, chartDef);
        const color = colors[0] || '#4A90D9';
        const display = (val === null || val === undefined)
            ? '—'
            : (typeof val === 'number' ? val.toLocaleString(undefined, { maximumFractionDigits: 2 }) : String(val));
        const len = display.length;
        const cw = container.clientWidth || container.offsetWidth || 240;
        const ch = container.clientHeight || container.offsetHeight || h || 160;
        let valFont = len > 12 ? 30 : (len > 8 ? 38 : 46);
        if (ch < 110) valFont = Math.min(valFont, 22);
        else if (ch < 140) valFont = Math.min(valFont, 28);
        else if (ch < 170) valFont = Math.min(valFont, 34);
        if (cw < 160) valFont = Math.min(valFont, 24);
        else if (cw < 220) valFont = Math.min(valFont, 32);

        const hAlign = chartDef.style?.kpiHAlign || 'center';
        const vAlign = chartDef.style?.kpiVAlign || 'middle';
        const hFlex = hAlign === 'left' ? 'flex-start' : (hAlign === 'right' ? 'flex-end' : 'center');
        const vFlex = vAlign === 'top' ? 'flex-start' : (vAlign === 'bottom' ? 'flex-end' : 'center');
        const padY = ch < 140 ? 10 : 16;
        const titleFont = cw < 160 ? 10 : (cw < 220 ? 11 : 11.5);

        container.style.cssText += `background:linear-gradient(135deg,${color}10,${color}05);display:flex;flex-direction:column;align-items:${hFlex};justify-content:${vFlex};border-radius:8px;overflow:hidden;border:1px solid ${color}22;`;
        container.innerHTML = `<div style="text-align:${hAlign};font-family:Inter,sans-serif;padding:${padY}px 14px;width:100%;box-sizing:border-box;display:flex;flex-direction:column;gap:6px;min-height:0">
            ${ch >= 130 ? `<div style="flex:0 0 auto;width:36px;height:36px;border-radius:10px;background:${color}22;display:${hAlign === 'center' ? 'inline-flex' : 'flex'};align-items:center;justify-content:center;${hAlign === 'right' ? 'margin-left:auto' : ''}">
                <div style="width:18px;height:18px;border-radius:5px;background:${color};"></div>
            </div>` : ''}
            <div style="flex:0 1 auto;min-height:0;font-size:${valFont}px;font-weight:700;color:#1e293b;line-height:1.15;white-space:normal;word-break:break-word;overflow-wrap:anywhere">${this._esc(display)}</div>
            <div style="flex:0 0 auto;font-size:${titleFont}px;font-weight:600;color:#64748b;text-transform:uppercase;letter-spacing:0.4px;line-height:1.3;word-break:break-word;overflow-wrap:anywhere;white-space:normal">${this._esc(title)}</div>
        </div>`;
    }

    renderMarimekko(chartDef, container, data, colors, h) {
        if ((!data.rawData || !data.rawData.length) && (!data.labels || !data.labels.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const canvas = this._makeCanvas(container, h);
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;
        const categories = (data.labels||['A','B','C','D']).slice(0,4);
        const series = ['S1','S2','S3'];
        const widths = categories.map(() => Math.round(15+Math.random()*35));
        const wTotal = widths.reduce((s,v)=>s+v,0);
        const segData = categories.map(() => {
            const raw = series.map(()=>Math.random());
            const total = raw.reduce((s,v)=>s+v,0);
            return raw.map(v=>v/total);
        });
        ctx.clearRect(0,0,W,H);
        const pad = {l:8,r:8,t:20,b:20};
        const drawW = W-pad.l-pad.r;
        let xOffset = pad.l;
        categories.forEach((cat, ci) => {
            const colW = Math.round(widths[ci]/wTotal*drawW);
            let yOffset = pad.t;
            const drawH = H-pad.t-pad.b;
            series.forEach((s,si) => {
                const segH = segData[ci][si]*drawH;
                ctx.fillStyle = colors[(si*4+ci)%colors.length]+'CC';
                ctx.fillRect(xOffset+1, yOffset+1, colW-2, segH-2);
                if (segH > 16 && colW > 20) {
                    ctx.fillStyle='#fff'; ctx.font='9px Inter,sans-serif'; ctx.textAlign='center';
                    ctx.fillText(s, xOffset+colW/2, yOffset+segH/2+4);
                }
                yOffset += segH;
            });
            ctx.fillStyle='#333'; ctx.font='10px Inter,sans-serif'; ctx.textAlign='center';
            ctx.fillText(`${cat} (${widths[ci]}%)`, xOffset+colW/2, H-3);
            xOffset += colW;
        });
    }

    renderDumbbell(chartDef, container, data, colors, h) {
        if ((!data.labels || !data.labels.length) || (!data.values || !data.values.length)) {
            return this._insufficientDataOverlay(container, chartDef.chartType);
        }
        const canvas = this._makeCanvas(container, h);
        const ctx = canvas.getContext('2d');
        const W = canvas.width, H = canvas.height;
        const items = (data.labels||['CEO','Manager','Analyst','Developer','Designer']).slice(0,5);
        const before = items.map((_,i)=>(data.values||[])[i]||Math.round(30+Math.random()*40));
        const after  = before.map(v=>Math.round(v+(Math.random()-0.3)*20));
        const allV = [...before,...after];
        const minV=Math.min(...allV)*0.9, maxV=Math.max(...allV)*1.05;
        const padL=80, padR=20, padT=20, padB=20;
        const rowH=(H-padT-padB)/items.length;
        ctx.clearRect(0,0,W,H);
        const toX = v => padL + (v-minV)/(maxV-minV)*(W-padL-padR);
        items.forEach((item,i) => {
            const y = padT + (i+0.5)*rowH;
            const x1=toX(before[i]), x2=toX(after[i]);
            // connecting line
            ctx.beginPath(); ctx.moveTo(x1,y); ctx.lineTo(x2,y);
            ctx.strokeStyle='#adb5bd88'; ctx.lineWidth=2; ctx.stroke();
            // before dot
            ctx.beginPath(); ctx.arc(x1,y,7,0,Math.PI*2);
            ctx.fillStyle=colors[0]+'CC'; ctx.fill();
            ctx.strokeStyle='#fff'; ctx.lineWidth=2; ctx.stroke();
            // after dot
            ctx.beginPath(); ctx.arc(x2,y,7,0,Math.PI*2);
            ctx.fillStyle=colors[1]+'CC'; ctx.fill();
            ctx.strokeStyle='#fff'; ctx.lineWidth=2; ctx.stroke();
            ctx.fillStyle='#333'; ctx.font='10px Inter,sans-serif'; ctx.textAlign='right';
            ctx.fillText(item, padL-6, y+4);
        });
        // Legend
        ctx.fillStyle=colors[0]; ctx.beginPath(); ctx.arc(padL+20,H-8,5,0,Math.PI*2); ctx.fill();
        ctx.fillStyle='#555'; ctx.font='10px Inter,sans-serif'; ctx.textAlign='left';
        ctx.fillText('Before', padL+28, H-4);
        ctx.fillStyle=colors[1]; ctx.beginPath(); ctx.arc(padL+90,H-8,5,0,Math.PI*2); ctx.fill();
        ctx.fillText('After', padL+98, H-4);
    }

    renderPlaceholder(chartDef, container, colors, h) {
        container.style.cssText += 'background:#f8f9fa;display:flex;flex-direction:column;align-items:center;justify-content:center;';
        container.innerHTML = `<div style="text-align:center;font-family:Inter,sans-serif;padding:20px">
            <div style="font-size:36px;margin-bottom:8px">📊</div>
            <div style="font-size:13px;font-weight:600;color:#2c3e50">${this._esc(chartDef.title||chartDef.chartType)}</div>
            <div style="font-size:11px;color:#8492a6;margin-top:4px">${this._esc(chartDef.chartType)}</div>
        </div>`;
    }

    mapApexType(chartType) {
        const map = {
            bar: 'bar', horizontalBar: 'bar', stackedBar: 'bar', groupedBar: 'bar',
            waterfall: 'bar', funnel: 'bar', histogram: 'bar', pareto: 'bar',
            bulletChart: 'bar', lollipop: 'bar', divergingBar: 'bar',
            spanChart: 'bar', pairedBar: 'bar', populationPyramid: 'bar',
            stackedBar100: 'bar', progressBar: 'bar', velocityChart: 'bar',
            gantt: 'bar', errorBar: 'bar',
            line: 'line', stepLine: 'line', burnDown: 'line', controlChart: 'line',
            confidenceBand: 'line', timeLine: 'line', slope: 'line', sparkline: 'line',
            area: 'area', streamGraph: 'area', stackedArea100: 'area', rangeArea: 'area',
            bellCurve: 'area', mixedBarLine: 'bar',
            pie: 'pie',
            donut: 'donut', doughnut: 'donut', gauge: 'radialBar', radialProgress: 'radialBar',
            nightingaleRose: 'polarArea', polarArea: 'polarArea',
            scatter: 'scatter', dotPlot: 'scatter', regressionLine: 'scatter',
            bubble: 'bubble',
            radar: 'radar',
        };
        return map[chartType] || 'bar';
    }

    renderTableChart(chartDef, container, data, colors, h) {
        const MAX_ROWS = 50;
        const rawData = (data.rawData || []).slice(0, MAX_ROWS);
        const mapping = chartDef.mapping || {};
        const tableFields = (mapping.tableFields || []).filter(f => f && f.fieldName);
        const primaryColor = (chartDef.style && chartDef.style.colorPalette && chartDef.style.colorPalette.startsWith('#'))
            ? chartDef.style.colorPalette : colors[0];

        container.style.cssText = 'position:absolute;inset:0;overflow:auto;padding:8px;';

        // Use all columns from rawData when available
        if (rawData.length > 0) {
            const allColumns = Object.keys(rawData[0]);
            const columns = tableFields.length
                ? tableFields
                    .map(f => ({
                        key: allColumns.find(c => c.toLowerCase() === String(f.fieldName || '').toLowerCase()) || f.fieldName,
                        label: f.label || f.fieldName,
                        width: f.width || null,
                        visible: f.visible !== false
                    }))
                    .filter(f => f.key && f.visible)
                : allColumns.map(c => ({ key: c, label: c, width: null, visible: true }));
            const headerCells = columns.map(col =>
                `<th style="color:${this._esc(primaryColor)};${col.width ? `width:${Number(col.width)}px;` : ''}">${this._esc(col.label)}</th>`
            ).join('');
            const bodyRows = rawData.map((row, idx) =>
                `<tr data-row-idx="${idx}" style="cursor:pointer;">` + columns.map(col => {
                    const val = row[col.key];
                    const isNum = typeof val === 'number';
                    return `<td${isNum ? ' class="text-end"' : ''}>${this._esc(String(val ?? ''))}</td>`;
                }).join('') + '</tr>'
            ).join('');
            container.innerHTML = `
                <table class="table table-sm table-striped table-bordered table-hover mb-0" style="font-size:12px;">
                    <thead style="position:sticky;top:0;background:#fff;">
                        <tr>${headerCells}</tr>
                    </thead>
                    <tbody>${bodyRows}</tbody>
                </table>`;
            // Cross-filter: clicking a row emits CrossFilter.apply with the
            // row's labelField value (or first column if no mapping). Re-clicking
            // the same row clears the filter (Power BI parity).
            const filterField = mapping.labelField || allColumns[0];
            this._wireTableRowClicks(chartDef, container, rawData, filterField);
            return;
        }

        // Fallback to label/value two-column view
        const labels = (data.labels || []).slice(0, MAX_ROWS);
        const values = (data.values || []).slice(0, MAX_ROWS);
        const labelField = mapping.labelField || 'Label';
        const valueField = mapping.valueField || 'Value';
        const rows = labels.map((lbl, i) =>
            `<tr data-row-idx="${i}" style="cursor:pointer;"><td>${this._esc(String(lbl))}</td><td class="text-end">${this._esc(String(values[i] ?? ''))}</td></tr>`
        ).join('');
        container.innerHTML = `
            <table class="table table-sm table-striped table-bordered table-hover mb-0" style="font-size:12px;">
                <thead style="position:sticky;top:0;background:#fff;">
                    <tr>
                        <th style="color:${this._esc(primaryColor)}">${this._esc(labelField)}</th>
                        <th class="text-end" style="color:${this._esc(primaryColor)}">${this._esc(valueField)}</th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>`;
        // Synthesize rawData rows from labels/values so click→filter still works.
        const synthRows = labels.map((lbl, i) => ({ [labelField]: lbl, [valueField]: values[i] }));
        this._wireTableRowClicks(chartDef, container, synthRows, labelField);
    }

    /** Wire row click → CrossFilter.apply on a custom table chart (Power BI parity). */
    _wireTableRowClicks(chartDef, container, rows, filterField) {
        if (!filterField || !window.CrossFilter) return;
        const tbody = container.querySelector('tbody');
        if (!tbody) return;
        const af = window.CrossFilter.activeFilter;
        const activeIsMine = af && af.sourceChartId === chartDef.id;
        const activeVal = activeIsMine ? String(af.value) : null;

        tbody.querySelectorAll('tr[data-row-idx]').forEach(tr => {
            const idx = parseInt(tr.dataset.rowIdx, 10);
            const row = rows[idx];
            if (!row) return;
            // Resolve actual key in row (case-insensitive)
            const rowKeys = Object.keys(row);
            const key = rowKeys.find(k => k.toLowerCase() === String(filterField).toLowerCase()) || filterField;
            const val = row[key];
            // Highlight the active row
            if (activeVal !== null && String(val) === activeVal) {
                tr.style.backgroundColor = 'rgba(74,144,217,0.18)';
                tr.style.fontWeight = '600';
            }
            tr.addEventListener('click', () => {
                const wasActive = activeVal !== null && String(val) === activeVal;
                if (wasActive) {
                    window.CrossFilter.clear();
                    return;
                }
                window.CrossFilter.apply(key, val, String(val), {
                    sourceChartId: chartDef.id,
                    sourceDatasetName: chartDef.datasetName,
                    sourceDatasourceId: chartDef.datasourceId || window.currentDatasourceId,
                    sourceRow: row
                });
            });
        });
    }

    renderSlicer(chartDef, container, data, colors, h) {
        const labels = data.labels || ['A','B','C','D','E'];
        const rawData = Array.isArray(data.rawData) ? data.rawData : [];
        const uniqueValues = [...new Set(labels.map(String))];
        const primaryColor = (chartDef.style && chartDef.style.colorPalette && chartDef.style.colorPalette.startsWith('#'))
            ? chartDef.style.colorPalette : colors[0];

        container.style.cssText = 'position:absolute;inset:0;overflow:auto;padding:12px;display:flex;flex-direction:column;gap:8px;';
        const mapping = chartDef.mapping || {};
        // Resolve the actual column name. When mapping.labelField is empty
        // (auto-built/AI slicers, legacy reports) fall back to the first key
        // of rawData — that's the real column name returned by the SQL —
        // so cross-filter emits e.g. "Segment"="Midmarket" instead of the
        // placeholder "Values"="Midmarket".
        let labelField = mapping.labelField;
        if (!labelField && rawData.length > 0) {
            labelField = Object.keys(rawData[0])[0] || '';
        }
        if (!labelField) labelField = 'Values';

        const titleEl = document.createElement('div');
        titleEl.style.cssText = 'font-size:11px;font-weight:600;color:#6c757d;text-transform:uppercase;letter-spacing:.5px;margin-bottom:4px;';
        titleEl.textContent = labelField;

        const wrap = document.createElement('div');
        wrap.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;';

        const setActive = (btn, active) => {
            btn.dataset.active = active ? '1' : '0';
            btn.style.backgroundColor = active ? primaryColor : `${primaryColor}22`;
            btn.style.color = active ? '#fff' : primaryColor;
        };

        // Restore active state from current CrossFilter, if it was set by this slicer
        const af = window.CrossFilter && window.CrossFilter.activeFilter;
        const currentVal = (af && af.sourceChartId === chartDef.id) ? String(af.value) : null;

        const buttons = [];
        uniqueValues.forEach(v => {
            const btn = document.createElement('button');
            btn.className = 'slicer-chip badge';
            btn.style.cssText = `background-color:${primaryColor}22;color:${primaryColor};border:1px solid ${primaryColor}66;border-radius:16px;padding:4px 12px;font-size:12px;cursor:pointer;text-align:left;font-weight:500;transition:all .15s;margin:2px;`;
            btn.textContent = String(v);
            setActive(btn, currentVal !== null && currentVal === String(v));
            btn.addEventListener('click', () => {
                const wasActive = btn.dataset.active === '1';
                // Single-select: deactivate all chips first
                buttons.forEach(b => setActive(b, false));
                if (!window.CrossFilter) return;
                if (wasActive) {
                    // Re-clicking active chip clears the filter
                    window.CrossFilter.clear();
                    return;
                }
                setActive(btn, true);
                // Find the source row for this value so other charts can use
                // foreign-key relationships when their table differs.
                let sourceRow = null;
                if (rawData.length) {
                    const keys = Object.keys(rawData[0]);
                    const lk = keys.find(k => k.toLowerCase() === String(labelField).toLowerCase()) || labelField;
                    sourceRow = rawData.find(r => String(r[lk]) === String(btn.textContent)) || null;
                }
                window.CrossFilter.apply(labelField, btn.textContent, btn.textContent, {
                    sourceChartId: chartDef.id,
                    sourceDatasetName: chartDef.datasetName,
                    sourceDatasourceId: chartDef.datasourceId || window.currentDatasourceId || null,
                    sourceRow
                });
            });
            buttons.push(btn);
            wrap.appendChild(btn);
        });

        container.appendChild(titleEl);
        container.appendChild(wrap);
    }

    renderNavigationWidget(chartDef, container, colors) {
        const nav = chartDef.navigation || {};
        const primaryColor = (chartDef.style && chartDef.style.colorPalette && chartDef.style.colorPalette.startsWith('#'))
            ? chartDef.style.colorPalette : colors[0];
        const borderEnabled = nav.borderEnabled !== false;
        const target = nav.target || 'current';
        const href = target === 'url' ? (nav.customUrl || '#') : '#';
        const borderColor = nav.borderColor || primaryColor;
        const borderRadius = Number(nav.borderRadius ?? 8);
        const backgroundColor = nav.backgroundColor || '#ffffff';
        const textColor = nav.textColor || primaryColor;
        const fontSize = Number(nav.fontSize ?? 13);
        const label = nav.label || chartDef.title || 'Open Link';

        container.style.cssText = 'position:absolute;inset:0;display:flex;align-items:center;justify-content:center;padding:8px;';
        const linkEl = document.createElement('a');
        linkEl.href = href;
        if (target === 'url') { linkEl.target = '_blank'; linkEl.rel = 'noopener noreferrer'; }
        linkEl.style.cssText = `display:inline-flex;align-items:center;justify-content:center;gap:6px;text-decoration:none;font-size:${fontSize}px;font-weight:600;color:${textColor};background:${backgroundColor};border:${borderEnabled ? '1px solid ' + borderColor : '1px solid transparent'};border-radius:${borderRadius}px;padding:8px 14px;min-width:120px;cursor:pointer;`;
        linkEl.innerHTML = '<i class="bi bi-link-45deg"></i>' + this._esc(label);

        // Handle page navigation click
        if (target === 'page') {
            linkEl.addEventListener('click', (e) => {
                e.preventDefault();
                const pageIndex = Number(nav.targetPageIndex ?? 0);
                if (window.canvasManager) {
                    window.canvasManager.switchPage(pageIndex);
                }
            });
        }

        container.innerHTML = '';
        container.appendChild(linkEl);
    }
}

window.chartRenderer = new ChartRenderer();
