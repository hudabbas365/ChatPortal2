// Chart rendering engine using Chart.js
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
        };
        this._customRenderTypes = new Set([
            'treemap','heatmap','sankey','sunburst',
            'boxPlot','violin','stemLeaf',
            'candlestick','ohlc','eventTimeline',
            'choropleth','bubbleMap','heatMapGeo','flowMap','spikeMap',
            'networkGraph','chordDiagram','arcDiagram','forceDirected','matrix',
            'waffleChart','pictograph',
            'kpiCard','metricTile',
            'marimekko','dumbbell',
            'table','slicer'
        ]);
    }

    // Safe HTML escaping — works even if the global escapeHtml is not loaded yet
    _esc(str) {
        if (typeof escapeHtml === 'function') return escapeHtml(str);
        const d = document.createElement('div');
        d.appendChild(document.createTextNode(String(str ?? '')));
        return d.innerHTML;
    }

    getColors(palette, count) {
        let colors;
        if (palette && palette.startsWith('#')) {
            // Hex color provided — generate shades/variations from the primary color
            colors = this._generateColorShades(palette, 8);
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
            this.instances[chartId].destroy();
            delete this.instances[chartId];
        }
    }

    async render(chartDef, canvasEl) {
        this.destroy(chartDef.id);
        const wrap = canvasEl.parentElement;
        let data;
        try { data = await this.fetchData(chartDef); }
        catch(e) { data = { labels: ['A','B','C','D','E'], values: [30,50,40,70,55] }; }

        if (this._customRenderTypes.has(chartDef.chartType)) {
            canvasEl.style.display = 'none';
            const existing = wrap.querySelector('.custom-chart-render');
            if (existing) existing.remove();
            const div = document.createElement('div');
            div.className = 'custom-chart-render';
            div.style.cssText = 'position:absolute;inset:0;overflow:hidden;';
            wrap.appendChild(div);
            this.renderCustomChart(chartDef, div, data);
            return;
        }

        canvasEl.style.display = '';
        const existing = wrap.querySelector('.custom-chart-render');
        if (existing) existing.remove();

        const ctx = canvasEl.getContext('2d');
        const config = this.buildConfig(chartDef, data);
        try {
            this.instances[chartDef.id] = new Chart(ctx, config);
        } catch(e) {
            console.warn('Chart render error:', e);
        }
    }

    async fetchData(chartDef) {
        if (chartDef.customJsonData) {
            try { return JSON.parse(chartDef.customJsonData); } catch(e) {}
        }
        const mapping = chartDef.mapping || {};
        const agg = chartDef.aggregation || {};
        const labelField = mapping.labelField || 'month';
        const valueField = mapping.valueField || 'revenue';

        if (agg.enabled && labelField && valueField) {
            const resp = await fetch(`/api/data/${chartDef.datasetName}/aggregated?labelField=${labelField}&valueField=${valueField}&aggregation=${agg.function || 'SUM'}`);
            return resp.json();
        } else {
            const resp = await fetch(`/api/data/${chartDef.datasetName}`);
            const raw = await resp.json();
            const labels = raw.map(r => r[labelField]);
            const values = raw.map(r => parseFloat(r[valueField]) || 0);
            return { labels, values };
        }
    }

    _baseOptions(chartDef, extraScales) {
        const style = chartDef.style || {};
        return {
            responsive: true,
            maintainAspectRatio: false,
            animation: { duration: style.animated !== false ? 700 : 0 },
            plugins: {
                legend: {
                    display: style.showLegend !== false,
                    position: style.legendPosition || 'top',
                    labels: { font: { family: style.fontFamily || 'Inter, sans-serif', size: 11 } }
                },
                tooltip: { enabled: style.showTooltips !== false },
                title: {
                    display: !!chartDef.title,
                    text: chartDef.title || '',
                    font: { size: style.titleFontSize || 14, family: style.fontFamily || 'Inter, sans-serif', weight: '600' },
                    color: '#2c3e50'
                }
            },
            scales: extraScales || {
                x: { grid: { color: 'rgba(0,0,0,0.05)' }, ticks: { font: { size: 10 } } },
                y: { grid: { color: 'rgba(0,0,0,0.05)' }, ticks: { font: { size: 10 } }, beginAtZero: true }
            }
        };
    }

    buildConfig(chartDef, data) {
        const style = chartDef.style || {};
        const palette = style.colorPalette || 'default';
        const labels = (data.labels || []).map(String);
        const values = (data.values || []).map(Number);
        const colors = this.getColors(palette, Math.max(labels.length, 8));
        const ct = chartDef.chartType;

        // ---- Special dispatch ----
        if (ct === 'gauge')           return this.buildGaugeConfig(chartDef, values, style);
        if (ct === 'waterfall')       return this.buildWaterfallConfig(chartDef, labels, values, style, colors);
        if (ct === 'funnel')          return this.buildFunnelConfig(chartDef, labels, values, style, colors);
        if (ct === 'mixedBarLine')    return this.buildMixedBarLineConfig(chartDef, labels, values, style, colors);
        if (ct === 'groupedBar')      return this.buildGroupedBarConfig(chartDef, labels, values, style, colors);
        if (ct === 'histogram')       return this.buildHistogramConfig(chartDef, labels, values, style, colors);
        if (ct === 'pareto')          return this.buildParetoConfig(chartDef, labels, values, style, colors);
        if (ct === 'bellCurve')       return this.buildBellCurveConfig(chartDef, labels, values, style, colors);
        if (ct === 'regressionLine')  return this.buildRegressionConfig(chartDef, labels, values, style, colors);
        if (ct === 'confidenceBand')  return this.buildConfidenceBandConfig(chartDef, labels, values, style, colors);
        if (ct === 'controlChart')    return this.buildControlChartConfig(chartDef, labels, values, style, colors);
        if (ct === 'errorBar')        return this.buildErrorBarConfig(chartDef, labels, values, style, colors);
        if (ct === 'stepLine')        return this.buildStepLineConfig(chartDef, labels, values, style, colors);
        if (ct === 'rangeArea')       return this.buildRangeAreaConfig(chartDef, labels, values, style, colors);
        if (ct === 'burnDown')        return this.buildBurnDownConfig(chartDef, labels, values, style, colors);
        if (ct === 'gantt')           return this.buildGanttConfig(chartDef, labels, values, style, colors);
        if (ct === 'bulletChart')     return this.buildBulletChartConfig(chartDef, labels, values, style, colors);
        if (ct === 'lollipop')        return this.buildLollipopConfig(chartDef, labels, values, style, colors);
        if (ct === 'slope')           return this.buildSlopeConfig(chartDef, labels, values, style, colors);
        if (ct === 'divergingBar')    return this.buildDivergingBarConfig(chartDef, labels, values, style, colors);
        if (ct === 'populationPyramid') return this.buildPopulationPyramidConfig(chartDef, labels, values, style, colors);
        if (ct === 'spanChart')       return this.buildSpanChartConfig(chartDef, labels, values, style, colors);
        if (ct === 'pairedBar')       return this.buildPairedBarConfig(chartDef, labels, values, style, colors);
        if (ct === 'stackedBar100')   return this.buildStackedBar100Config(chartDef, labels, values, style, colors);
        if (ct === 'stackedArea100')  return this.buildStackedArea100Config(chartDef, labels, values, style, colors);
        if (ct === 'streamGraph')     return this.buildStreamGraphConfig(chartDef, labels, values, style, colors);
        if (ct === 'velocityChart')   return this.buildVelocityChartConfig(chartDef, labels, values, style, colors);
        if (ct === 'sparkline')       return this.buildSparklineConfig(chartDef, labels, values, style, colors);
        if (ct === 'progressBar')     return this.buildProgressBarConfig(chartDef, labels, values, style, colors);
        if (ct === 'radialProgress')  return this.buildRadialProgressConfig(chartDef, values, style);
        if (ct === 'nightingaleRose') return this.buildNightingaleRoseConfig(chartDef, labels, values, style, colors);
        if (ct === 'dotPlot')         return this.buildDotPlotConfig(chartDef, labels, values, style, colors);
        if (ct === 'timeLine')        return this.buildTimeLineConfig(chartDef, labels, values, style, colors);

        // ---- Generic fallback ----
        const type = this.mapType(ct);
        const options = this._baseOptions(chartDef);
        let datasets = [{
            label: chartDef.title || 'Data',
            data: values,
            backgroundColor: ['pie','doughnut','polarArea'].includes(type) ? colors : colors[0] + 'CC',
            borderColor: ['pie','doughnut','polarArea'].includes(type) ? colors : colors[0],
            borderWidth: 2,
            fill: style.fillArea || false,
            tension: 0.4,
            borderRadius: parseInt(style.borderRadius || '4'),
            pointRadius: 4,
            pointHoverRadius: 6
        }];
        if (type === 'bar' && ct === 'horizontalBar') options.indexAxis = 'y';
        if (type === 'bar' && ct === 'stackedBar') {
            options.scales.x.stacked = true;
            options.scales.y.stacked = true;
        }
        return { type, data: { labels, datasets }, options };
    }

    // ============================================================
    // Chart.js config builders
    // ============================================================

    buildGaugeConfig(chartDef, values, style) {
        const val = values[0] || 0;
        const max = Math.max(...values, 100);
        const pct = Math.min(val / max, 1);
        return {
            type: 'doughnut',
            data: {
                labels: ['Value', 'Remaining'],
                datasets: [{ data: [pct*100, (1-pct)*100], backgroundColor: ['#4A90D9','#E9ECEF'], borderWidth: 0 }]
            },
            options: {
                responsive: true, maintainAspectRatio: false,
                circumference: 180, rotation: -90,
                plugins: {
                    legend: { display: false },
                    title: { display: true, text: `${chartDef.title || 'Gauge'}: ${val.toFixed(1)}`, font: { size: 14, weight: '600' } }
                }
            }
        };
    }

    buildWaterfallConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Start','Q1','Q2','Q3','Q4','End'];
        const vals = values.length ? values : [0, 120, -30, 80, -20, 150];
        const floatData = [];
        let cumulative = 0;
        const bgColors = [];
        for (let i = 0; i < vals.length; i++) {
            const v = vals[i];
            if (i === 0 || i === vals.length - 1) {
                floatData.push([0, v]);
                bgColors.push('#4A90D9');
            } else {
                const base = cumulative;
                floatData.push([base, base + v]);
                bgColors.push(v >= 0 ? '#4CAF50CC' : '#E87C3ECC');
            }
            cumulative += v;
        }
        const opts = this._baseOptions(chartDef);
        opts.plugins.tooltip = {
            callbacks: {
                label: ctx => {
                    const d = ctx.raw;
                    return `Change: ${(d[1]-d[0]).toFixed(1)} (Total: ${d[1].toFixed(1)})`;
                }
            }
        };
        return {
            type: 'bar',
            data: { labels: lbls, datasets: [{ label: chartDef.title || 'Waterfall', data: floatData, backgroundColor: bgColors, borderWidth: 1, borderColor: bgColors }] },
            options: opts
        };
    }

    buildFunnelConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Leads','Prospects','Qualified','Proposals','Closed'];
        const vals = values.length ? [...values].sort((a,b) => b - a) : [1000, 750, 500, 250, 100];
        const sorted = lbls.map((l,i) => ({l, v: vals[i]||0})).sort((a,b) => b.v - a.v);
        const opts = this._baseOptions(chartDef);
        opts.indexAxis = 'y';
        return {
            type: 'bar',
            data: {
                labels: sorted.map(d => d.l),
                datasets: [{ label: chartDef.title || 'Funnel', data: sorted.map(d => d.v), backgroundColor: colors.map(c => c+'CC'), borderRadius: 4 }]
            },
            options: opts
        };
    }

    buildMixedBarLineConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May','Jun'];
        const barVals = values.length ? values : [120,150,130,200,180,210];
        const lineVals = barVals.map((v,i) => Math.round(barVals.slice(0,i+1).reduce((a,b)=>a+b,0)/(i+1)));
        const opts = this._baseOptions(chartDef);
        return {
            type: 'bar',
            data: {
                labels: lbls,
                datasets: [
                    { type: 'bar', label: 'Monthly', data: barVals, backgroundColor: colors[0]+'CC', borderRadius: 4 },
                    { type: 'line', label: 'Trend Avg', data: lineVals, borderColor: colors[1], backgroundColor: 'transparent', borderWidth: 2, pointRadius: 4, tension: 0.3 }
                ]
            },
            options: opts
        };
    }

    buildGroupedBarConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Q1','Q2','Q3','Q4'];
        const n = Math.min(lbls.length, 4);
        const series = ['Product A','Product B','Product C'];
        const datasets = series.map((s, si) => ({
            label: s,
            data: Array.from({length: n}, (_,i) => Math.round(40 + Math.random()*80 + si*20)),
            backgroundColor: colors[si]+'CC',
            borderRadius: 3
        }));
        const opts = this._baseOptions(chartDef);
        return { type: 'bar', data: { labels: lbls.slice(0, n), datasets }, options: opts };
    }

    buildHistogramConfig(chartDef, labels, values, style, colors) {
        const data = values.length >= 5 ? values : [23,25,27,28,30,31,32,33,34,35,36,37,38,40,42,45,48,50,55,60];
        const min = Math.min(...data), max = Math.max(...data);
        const binCount = Math.min(10, Math.ceil(Math.sqrt(data.length)));
        const binSize = (max - min) / binCount;
        const bins = Array(binCount).fill(0);
        data.forEach(v => {
            const idx = Math.min(Math.floor((v - min) / binSize), binCount - 1);
            bins[idx]++;
        });
        const binLabels = bins.map((_, i) => `${(min + i * binSize).toFixed(1)}-${(min + (i+1) * binSize).toFixed(1)}`);
        const opts = this._baseOptions(chartDef);
        return {
            type: 'bar',
            data: { labels: binLabels, datasets: [{ label: 'Frequency', data: bins, backgroundColor: colors[0]+'CC', borderColor: colors[0], borderWidth: 1, categoryPercentage: 1.0, barPercentage: 1.0 }] },
            options: opts
        };
    }

    buildParetoConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Defect A','Defect B','Defect C','Defect D','Defect E'];
        const vals = values.length ? values : [80, 50, 30, 20, 10];
        const pairs = lbls.map((l,i) => ({l, v: vals[i]||0})).sort((a,b) => b.v - a.v);
        const total = pairs.reduce((s,p) => s + p.v, 0);
        let cum = 0;
        const cumPct = pairs.map(p => { cum += p.v; return Math.round(cum / total * 100); });
        const opts = this._baseOptions(chartDef);
        opts.scales = {
            x: { grid: { color: 'rgba(0,0,0,0.05)' } },
            y: { position: 'left', beginAtZero: true, title: { display: true, text: 'Count' } },
            y2: { position: 'right', min: 0, max: 100, grid: { drawOnChartArea: false }, title: { display: true, text: '% Cumulative' } }
        };
        return {
            type: 'bar',
            data: {
                labels: pairs.map(p => p.l),
                datasets: [
                    { type: 'bar', label: 'Frequency', data: pairs.map(p => p.v), backgroundColor: colors[0]+'CC', yAxisID: 'y', borderRadius: 3 },
                    { type: 'line', label: 'Cumulative %', data: cumPct, borderColor: colors[2], backgroundColor: 'transparent', borderWidth: 2, pointRadius: 4, yAxisID: 'y2', tension: 0 }
                ]
            },
            options: opts
        };
    }

    buildBellCurveConfig(chartDef, labels, values, style, colors) {
        const data = values.length >= 3 ? values : [10,15,20,25,30,35,40,45,50,55,60,65,70];
        const mean = data.reduce((s,v) => s+v, 0) / data.length;
        const variance = data.reduce((s,v) => s + Math.pow(v-mean, 2), 0) / data.length;
        const std = Math.sqrt(variance) || 1;
        const xMin = mean - 4*std, xMax = mean + 4*std;
        const pts = 60;
        const xVals = [], yVals = [];
        for (let i = 0; i <= pts; i++) {
            const x = xMin + (xMax - xMin) * i / pts;
            xVals.push(x.toFixed(2));
            yVals.push(Math.exp(-0.5 * Math.pow((x - mean) / std, 2)) / (std * Math.sqrt(2 * Math.PI)));
        }
        const opts = this._baseOptions(chartDef);
        return {
            type: 'line',
            data: {
                labels: xVals,
                datasets: [{ label: 'Distribution', data: yVals, borderColor: colors[0], backgroundColor: this.hexToRgba(colors[0], 0.2), fill: true, tension: 0.4, pointRadius: 0 }]
            },
            options: opts
        };
    }

    buildRegressionConfig(chartDef, labels, values, style, colors) {
        const pts = values.length >= 4 ? values.map((v,i) => ({x: i, y: v})) :
            [{x:1,y:22},{x:2,y:28},{x:3,y:33},{x:4,y:35},{x:5,y:42},{x:6,y:48},{x:7,y:51},{x:8,y:58}];
        const n = pts.length;
        const sumX = pts.reduce((s,p)=>s+p.x,0), sumY = pts.reduce((s,p)=>s+p.y,0);
        const sumXY = pts.reduce((s,p)=>s+p.x*p.y,0), sumX2 = pts.reduce((s,p)=>s+p.x*p.x,0);
        const slope = (n*sumXY - sumX*sumY) / (n*sumX2 - sumX*sumX);
        const intercept = (sumY - slope*sumX) / n;
        const lineData = pts.map(p => ({x: p.x, y: slope*p.x + intercept}));
        const opts = this._baseOptions(chartDef, {
            x: { type: 'linear', grid: { color: 'rgba(0,0,0,0.05)' } },
            y: { grid: { color: 'rgba(0,0,0,0.05)' }, beginAtZero: false }
        });
        return {
            type: 'scatter',
            data: {
                datasets: [
                    { label: 'Data Points', data: pts, backgroundColor: colors[0]+'BB', pointRadius: 6 },
                    { type: 'line', label: 'Regression', data: lineData, borderColor: colors[2], backgroundColor: 'transparent', borderWidth: 2, pointRadius: 0, tension: 0 }
                ]
            },
            options: opts
        };
    }

    buildConfidenceBandConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug'];
        const vals = values.length ? values : [30,35,32,40,38,45,42,48];
        const ci = vals.map(v => v * 0.12);
        const opts = this._baseOptions(chartDef);
        return {
            type: 'line',
            data: {
                labels: lbls,
                datasets: [
                    { label: 'Upper CI', data: vals.map((v,i)=>v+ci[i]), borderColor: 'transparent', backgroundColor: this.hexToRgba(colors[0], 0.15), fill: '+1', pointRadius: 0, tension: 0.4 },
                    { label: 'Mean', data: vals, borderColor: colors[0], backgroundColor: 'transparent', borderWidth: 2, pointRadius: 4, tension: 0.4 },
                    { label: 'Lower CI', data: vals.map((v,i)=>v-ci[i]), borderColor: 'transparent', backgroundColor: this.hexToRgba(colors[0], 0.15), fill: '-1', pointRadius: 0, tension: 0.4 }
                ]
            },
            options: opts
        };
    }

    buildControlChartConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : Array.from({length: 15}, (_,i) => `P${i+1}`);
        const vals = values.length ? values : [48,52,51,53,47,50,54,49,51,50,52,55,48,51,50];
        const mean = vals.reduce((s,v)=>s+v,0)/vals.length;
        const std = Math.sqrt(vals.reduce((s,v)=>s+Math.pow(v-mean,2),0)/vals.length);
        const ucl = mean + 3*std, lcl = Math.max(0, mean - 3*std);
        const opts = this._baseOptions(chartDef);
        return {
            type: 'line',
            data: {
                labels: lbls.slice(0, vals.length),
                datasets: [
                    { label: 'UCL', data: Array(vals.length).fill(ucl), borderColor: '#dc3545', borderDash: [5,5], borderWidth: 1.5, pointRadius: 0, fill: false },
                    { label: 'Mean', data: Array(vals.length).fill(mean), borderColor: '#4CAF50', borderDash: [4,4], borderWidth: 1.5, pointRadius: 0, fill: false },
                    { label: 'LCL', data: Array(vals.length).fill(lcl), borderColor: '#dc3545', borderDash: [5,5], borderWidth: 1.5, pointRadius: 0, fill: false },
                    { label: 'Data', data: vals, borderColor: colors[0], backgroundColor: colors[0]+'44', fill: false, borderWidth: 2, pointRadius: 4, tension: 0.1 }
                ]
            },
            options: opts
        };
    }

    buildErrorBarConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['A','B','C','D','E'];
        const vals = values.length ? values : [45, 62, 38, 75, 55];
        const errors = vals.map(v => v * 0.1);
        const floatData = vals.map((v,i) => [v - errors[i], v + errors[i]]);
        const opts = this._baseOptions(chartDef);
        return {
            type: 'bar',
            data: {
                labels: lbls,
                datasets: [
                    { label: chartDef.title || 'Values', data: vals, backgroundColor: colors[0]+'CC', borderRadius: 4 },
                    { label: 'Error Range', data: floatData, backgroundColor: colors[1]+'88', borderColor: colors[1], borderWidth: 1, borderSkipped: false }
                ]
            },
            options: opts
        };
    }

    buildStepLineConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Mon','Tue','Wed','Thu','Fri','Sat','Sun'];
        const vals = values.length ? values : [100, 120, 120, 150, 130, 130, 180];
        const opts = this._baseOptions(chartDef);
        return {
            type: 'line',
            data: {
                labels: lbls,
                datasets: [{ label: chartDef.title || 'Step Line', data: vals, borderColor: colors[0], backgroundColor: this.hexToRgba(colors[0], 0.15), fill: true, stepped: true, pointRadius: 5, borderWidth: 2 }]
            },
            options: opts
        };
    }

    buildRangeAreaConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May','Jun','Jul'];
        const vals = values.length ? values : [30,35,33,40,38,45,42];
        const upper = vals.map(v => v + v*0.2);
        const lower = vals.map(v => v - v*0.2);
        const opts = this._baseOptions(chartDef);
        return {
            type: 'line',
            data: {
                labels: lbls,
                datasets: [
                    { label: 'Max', data: upper, borderColor: this.hexToRgba(colors[0], 0.5), backgroundColor: this.hexToRgba(colors[0], 0.15), fill: '+1', pointRadius: 0, tension: 0.4 },
                    { label: 'Value', data: vals, borderColor: colors[0], backgroundColor: 'transparent', borderWidth: 2, pointRadius: 4, tension: 0.4 },
                    { label: 'Min', data: lower, borderColor: this.hexToRgba(colors[0], 0.5), backgroundColor: this.hexToRgba(colors[0], 0.15), fill: '-1', pointRadius: 0, tension: 0.4 }
                ]
            },
            options: opts
        };
    }

    buildBurnDownConfig(chartDef, labels, values, style, colors) {
        const n = 10;
        const total = (values[0] || 100);
        const lbls = Array.from({length: n+1}, (_,i) => `Day ${i}`);
        const ideal = Array.from({length: n+1}, (_,i) => Math.round(total - total*i/n));
        const actual = [total];
        for (let i = 1; i <= n; i++) actual.push(Math.round(ideal[i] + (Math.random()-0.3)*total*0.08));
        const opts = this._baseOptions(chartDef);
        return {
            type: 'line',
            data: {
                labels: lbls,
                datasets: [
                    { label: 'Ideal', data: ideal, borderColor: '#adb5bd', borderDash: [6,4], borderWidth: 1.5, pointRadius: 0, fill: false, tension: 0 },
                    { label: 'Actual', data: actual, borderColor: colors[0], backgroundColor: this.hexToRgba(colors[0], 0.1), fill: true, borderWidth: 2, pointRadius: 4, tension: 0.2 }
                ]
            },
            options: opts
        };
    }

    buildGanttConfig(chartDef, labels, values, style, colors) {
        const tasks = labels.length ? labels.slice(0,6) : ['Planning','Design','Development','Testing','Deployment','Review'];
        const data = tasks.map((t, i) => {
            const start = i * 3 + (i > 2 ? 2 : 0);
            const dur = 3 + (i % 3);
            return [start, start + dur];
        });
        const opts = this._baseOptions(chartDef);
        opts.indexAxis = 'y';
        opts.scales = {
            x: { min: 0, max: 20, title: { display: true, text: 'Days' }, grid: { color: 'rgba(0,0,0,0.05)' } },
            y: { grid: { color: 'rgba(0,0,0,0.05)' } }
        };
        return {
            type: 'bar',
            data: {
                labels: tasks,
                datasets: [{ label: 'Task Duration', data: data, backgroundColor: colors.map(c=>c+'CC'), borderRadius: 4, borderSkipped: false }]
            },
            options: opts
        };
    }

    buildBulletChartConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels.slice(0,4) : ['Revenue','Cost','Growth','NPS'];
        const vals = values.length ? values.slice(0,lbls.length) : [78, 55, 62, 85];
        const targets = vals.map(v => Math.min(100, v + 15));
        const opts = this._baseOptions(chartDef);
        opts.indexAxis = 'y';
        opts.plugins.annotation = {};
        return {
            type: 'bar',
            data: {
                labels: lbls,
                datasets: [
                    { label: 'Target', data: targets, backgroundColor: '#e9ecef', borderRadius: 4, barPercentage: 0.6, borderSkipped: false },
                    { label: 'Actual', data: vals, backgroundColor: colors[0]+'CC', borderRadius: 4, barPercentage: 0.35, borderSkipped: false }
                ]
            },
            options: opts
        };
    }

    buildLollipopConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['A','B','C','D','E','F'];
        const vals = values.length ? values : [42, 67, 35, 78, 55, 63];
        const opts = this._baseOptions(chartDef);
        return {
            type: 'bar',
            data: {
                labels: lbls,
                datasets: [
                    { label: chartDef.title || 'Value', data: vals, backgroundColor: 'transparent', borderColor: colors[0]+'88', borderWidth: 2, borderRadius: 0, barPercentage: 0.05 },
                    { type: 'scatter', label: 'Point', data: vals.map((v,i) => ({x: lbls[i], y: v})), backgroundColor: colors[0], pointRadius: 8, pointHoverRadius: 10 }
                ]
            },
            options: opts
        };
    }

    buildSlopeConfig(chartDef, labels, values, style, colors) {
        const items = labels.length >= 3 ? labels.slice(0,5) : ['Product A','Product B','Product C','Product D'];
        const before = values.length >= items.length ? values.slice(0,items.length) : items.map(()=>Math.round(30+Math.random()*50));
        const after = before.map(v => Math.round(v + (Math.random()-0.4)*20));
        const opts = this._baseOptions(chartDef);
        opts.scales = {
            x: { ticks: { font: { size: 12, weight: '600' } } },
            y: { beginAtZero: false, grid: { color: 'rgba(0,0,0,0.05)' } }
        };
        return {
            type: 'line',
            data: {
                labels: ['Before', 'After'],
                datasets: items.map((item, i) => ({
                    label: item,
                    data: [before[i], after[i]],
                    borderColor: colors[i % colors.length],
                    backgroundColor: colors[i % colors.length]+'33',
                    borderWidth: 2, pointRadius: 6, tension: 0
                }))
            },
            options: opts
        };
    }

    buildDivergingBarConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Very Satisfied','Satisfied','Neutral','Dissatisfied','Very Dissatisfied'];
        const vals = values.length ? values.slice(0, lbls.length).map((v,i) => i < Math.ceil(lbls.length/2) ? Math.abs(v) : -Math.abs(v)) :
            [45, 30, 10, -20, -35];
        const bgColors = vals.map(v => (v >= 0 ? '#4CAF50CC' : '#E87C3ECC'));
        const opts = this._baseOptions(chartDef);
        opts.indexAxis = 'y';
        opts.scales = {
            x: { grid: { color: 'rgba(0,0,0,0.05)' }, ticks: { callback: v => Math.abs(v) } },
            y: { grid: { color: 'rgba(0,0,0,0.05)' } }
        };
        return {
            type: 'bar',
            data: { labels: lbls, datasets: [{ label: chartDef.title || 'Diverging', data: vals, backgroundColor: bgColors, borderRadius: 4 }] },
            options: opts
        };
    }

    buildPopulationPyramidConfig(chartDef, labels, values, style, colors) {
        const ageGroups = ['0-9','10-19','20-29','30-39','40-49','50-59','60-69','70+'];
        const males   = [80,95,110,120,105,90,70,45];
        const females = [78,92,108,118,108,92,74,52];
        const opts = this._baseOptions(chartDef);
        opts.indexAxis = 'y';
        opts.scales = {
            x: { stacked: false, ticks: { callback: v => Math.abs(v) }, grid: { color: 'rgba(0,0,0,0.05)' } },
            y: { stacked: false }
        };
        return {
            type: 'bar',
            data: {
                labels: ageGroups,
                datasets: [
                    { label: 'Male', data: males.map(v => -v), backgroundColor: colors[0]+'CC', borderRadius: 2, barPercentage: 0.8 },
                    { label: 'Female', data: females, backgroundColor: colors[1]+'CC', borderRadius: 2, barPercentage: 0.8 }
                ]
            },
            options: opts
        };
    }

    buildSpanChartConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels.slice(0,5) : ['Item A','Item B','Item C','Item D','Item E'];
        const mins = values.length ? values.slice(0,lbls.length) : [10,20,15,30,25];
        const maxs = mins.map(v => v + Math.round(v * 0.5 + 10));
        const opts = this._baseOptions(chartDef);
        opts.indexAxis = 'y';
        return {
            type: 'bar',
            data: {
                labels: lbls,
                datasets: [{ label: 'Range', data: mins.map((m,i)=>[m,maxs[i]]), backgroundColor: colors.map(c=>c+'BB'), borderRadius: 4, borderSkipped: false }]
            },
            options: opts
        };
    }

    buildPairedBarConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels.slice(0,5) : ['North','South','East','West','Central'];
        const g1 = values.length ? values.slice(0,lbls.length) : [65,72,58,80,70];
        const g2 = g1.map(v => Math.round(v * (0.7 + Math.random() * 0.6)));
        const opts = this._baseOptions(chartDef);
        opts.indexAxis = 'y';
        return {
            type: 'bar',
            data: {
                labels: lbls,
                datasets: [
                    { label: '2023', data: g1, backgroundColor: colors[0]+'CC', borderRadius: 3, barPercentage: 0.4 },
                    { label: '2024', data: g2, backgroundColor: colors[1]+'CC', borderRadius: 3, barPercentage: 0.4 }
                ]
            },
            options: opts
        };
    }

    buildStackedBar100Config(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May'];
        const series = ['A','B','C'];
        const rawData = series.map((_, si) => lbls.map(() => Math.round(20 + Math.random() * 40)));
        const normalized = lbls.map((_, li) => {
            const total = series.reduce((s, _, si) => s + rawData[si][li], 0);
            return series.map((_, si) => Math.round(rawData[si][li] / total * 100));
        });
        const opts = this._baseOptions(chartDef);
        opts.scales = {
            x: { stacked: true, max: 100 },
            y: { stacked: true, max: 100, ticks: { callback: v => v + '%' } }
        };
        return {
            type: 'bar',
            data: {
                labels: lbls,
                datasets: series.map((s, si) => ({
                    label: `Group ${s}`, data: lbls.map((_,li) => normalized[li][si]), backgroundColor: colors[si]+'CC', borderRadius: 0
                }))
            },
            options: opts
        };
    }

    buildStackedArea100Config(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May','Jun'];
        const series = ['A','B','C'];
        const rawData = series.map(() => lbls.map(() => Math.round(20 + Math.random() * 40)));
        const normalized = lbls.map((_,li) => {
            const total = series.reduce((s,_,si) => s + rawData[si][li], 0);
            return series.map((_,si) => Math.round(rawData[si][li]/total*100));
        });
        const opts = this._baseOptions(chartDef);
        opts.scales = { x: { stacked: true }, y: { stacked: true, max: 100, ticks: { callback: v => v+'%' } } };
        return {
            type: 'line',
            data: {
                labels: lbls,
                datasets: series.map((s,si) => ({
                    label: `Series ${s}`, data: lbls.map((_,li) => normalized[li][si]),
                    backgroundColor: colors[si]+'CC', borderColor: colors[si], fill: true, tension: 0.4, pointRadius: 2
                }))
            },
            options: opts
        };
    }

    buildStreamGraphConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug'];
        const series = ['Series A','Series B','Series C','Series D'];
        const opts = this._baseOptions(chartDef);
        opts.scales = { x: { stacked: true, grid: { display: false } }, y: { stacked: true, grid: { color: 'rgba(0,0,0,0.05)' } } };
        return {
            type: 'line',
            data: {
                labels: lbls,
                datasets: series.map((s,i) => ({
                    label: s,
                    data: lbls.map(() => Math.round(15 + Math.random()*30)),
                    backgroundColor: colors[i]+'AA', borderColor: colors[i]+'66', fill: true, tension: 0.5, pointRadius: 0
                }))
            },
            options: opts
        };
    }

    buildVelocityChartConfig(chartDef, labels, values, style, colors) {
        const sprints = labels.length ? labels.slice(0,8) : Array.from({length:8}, (_,i)=>`Sprint ${i+1}`);
        const committed = values.length ? values.slice(0,sprints.length) : [40,42,38,45,43,50,48,52];
        const completed = committed.map(v => Math.round(v * (0.7 + Math.random()*0.35)));
        const opts = this._baseOptions(chartDef);
        return {
            type: 'bar',
            data: {
                labels: sprints,
                datasets: [
                    { label: 'Committed', data: committed, backgroundColor: colors[0]+'44', borderColor: colors[0], borderWidth: 1.5, borderRadius: 4 },
                    { label: 'Completed', data: completed, backgroundColor: colors[0]+'CC', borderRadius: 4 }
                ]
            },
            options: opts
        };
    }

    buildSparklineConfig(chartDef, labels, values, style, colors) {
        const vals = values.length ? values : [12,18,15,22,20,25,23,30,28,35];
        const opts = {
            responsive: true, maintainAspectRatio: false,
            animation: { duration: 300 },
            plugins: { legend: { display: false }, tooltip: { enabled: false }, title: { display: false } },
            scales: { x: { display: false }, y: { display: false } },
            elements: { point: { radius: 0 } }
        };
        return {
            type: 'line',
            data: {
                labels: vals.map((_,i)=>i),
                datasets: [{ data: vals, borderColor: colors[0], backgroundColor: this.hexToRgba(colors[0], 0.15), fill: true, borderWidth: 2, tension: 0.4 }]
            },
            options: opts
        };
    }

    buildProgressBarConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels.slice(0,4) : ['Goal A','Goal B','Goal C','Goal D'];
        const vals = values.length ? values.slice(0,lbls.length).map(v => Math.min(100,Math.abs(v))) : [78,55,91,63];
        const opts = this._baseOptions(chartDef);
        opts.indexAxis = 'y';
        opts.scales = {
            x: { min: 0, max: 100, ticks: { callback: v => v + '%' }, grid: { color: 'rgba(0,0,0,0.05)' } },
            y: {}
        };
        return {
            type: 'bar',
            data: {
                labels: lbls,
                datasets: [
                    { label: 'Track', data: Array(lbls.length).fill(100), backgroundColor: '#e9ecef', borderRadius: 20, barPercentage: 0.4 },
                    { label: 'Progress', data: vals, backgroundColor: colors.map(c=>c+'CC'), borderRadius: 20, barPercentage: 0.4 }
                ]
            },
            options: opts
        };
    }

    buildRadialProgressConfig(chartDef, values, style) {
        const val = Math.min(100, Math.abs(values[0] || 72));
        return {
            type: 'doughnut',
            data: {
                labels: ['Progress', 'Remaining'],
                datasets: [{ data: [val, 100-val], backgroundColor: ['#4A90D9','#E9ECEF'], borderWidth: 0 }]
            },
            options: {
                responsive: true, maintainAspectRatio: false,
                rotation: -90, circumference: 360,
                plugins: {
                    legend: { display: false },
                    title: { display: true, text: `${chartDef.title || 'Progress'}: ${val}%`, font: { size: 14, weight: '600' } }
                },
                cutout: '75%'
            }
        };
    }

    buildNightingaleRoseConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
        const vals = values.length ? values : lbls.map(()=>Math.round(20+Math.random()*60));
        const opts = this._baseOptions(chartDef);
        return {
            type: 'polarArea',
            data: { labels: lbls, datasets: [{ label: chartDef.title||'Value', data: vals, backgroundColor: colors.map(c=>c+'CC') }] },
            options: opts
        };
    }

    buildDotPlotConfig(chartDef, labels, values, style, colors) {
        const pts = values.length ? values.map((v,i) => ({x: labels[i]||i, y: v})) :
            [{x:'A',y:45},{x:'A',y:52},{x:'B',y:30},{x:'B',y:38},{x:'C',y:60},{x:'C',y:65},{x:'D',y:25},{x:'D',y:35}];
        const opts = this._baseOptions(chartDef);
        return {
            type: 'scatter',
            data: { datasets: [{ label: chartDef.title||'Data', data: pts, backgroundColor: colors[0]+'BB', pointRadius: 7, pointHoverRadius: 9 }] },
            options: opts
        };
    }

    buildTimeLineConfig(chartDef, labels, values, style, colors) {
        const lbls = labels.length ? labels : ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
        const vals = values.length ? values : [65,72,68,80,75,82,88,91,85,90,95,102];
        const opts = this._baseOptions(chartDef);
        return {
            type: 'line',
            data: {
                labels: lbls,
                datasets: [{ label: chartDef.title||'Timeline', data: vals, borderColor: colors[0], backgroundColor: this.hexToRgba(colors[0], 0.12), fill: true, borderWidth: 2, pointRadius: 4, tension: 0.4 }]
            },
            options: opts
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
            case 'kpiCard':      this.renderKpiCard(chartDef, container, data, colors, h); break;
            case 'metricTile':   this.renderMetricTile(chartDef, container, data, colors, h); break;
            case 'marimekko':    this.renderMarimekko(chartDef, container, data, colors, h); break;
            case 'dumbbell':     this.renderDumbbell(chartDef, container, data, colors, h); break;
            case 'table':        this.renderTableChart(chartDef, container, data, colors, h); break;
            case 'slicer':       this.renderSlicer(chartDef, container, data, colors, h); break;
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
        const val = (data.values||[])[0] || 12345;
        const prev = val * (0.85 + Math.random() * 0.3);
        const change = ((val - prev) / prev * 100).toFixed(1);
        const up = change >= 0;
        container.style.cssText += 'background:#fff;display:flex;flex-direction:column;align-items:center;justify-content:center;';
        container.innerHTML = `<div style="text-align:center;font-family:Inter,sans-serif;padding:16px">
            <div style="font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:0.5px;color:#8492a6;margin-bottom:8px">${this._esc(chartDef.title||'KPI')}</div>
            <div style="font-size:42px;font-weight:700;color:${colors[0]};line-height:1">${typeof val==='number'?val.toLocaleString():val}</div>
            <div style="margin-top:8px;font-size:14px;color:${up?'#4CAF50':'#E87C3E'};font-weight:600">
                ${up?'▲':'▼'} ${Math.abs(change)}% vs prior period
            </div>
            <div style="font-size:10px;color:#adb5bd;margin-top:4px">${(data.labels||[])[0]||'Current Period'}</div>
        </div>`;
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

    renderMarimekko(chartDef, container, data, colors, h) {
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

    mapType(chartType) {
        const map = {
            bar: 'bar', horizontalBar: 'bar', stackedBar: 'bar', groupedBar: 'bar',
            waterfall: 'bar', funnel: 'bar', treemap: 'bar', heatmap: 'bar',
            sankey: 'bar', histogram: 'bar', boxPlot: 'bar', pareto: 'bar',
            bulletChart: 'bar', marimekko: 'bar', lollipop: 'bar', divergingBar: 'bar',
            spanChart: 'bar', pairedBar: 'bar', populationPyramid: 'bar',
            stackedBar100: 'bar', waffleChart: 'bar', pictograph: 'bar',
            progressBar: 'bar', kpiCard: 'bar', metricTile: 'bar', velocityChart: 'bar',
            gantt: 'bar', errorBar: 'bar',
            line: 'line', area: 'line', stepLine: 'line', streamGraph: 'line',
            burnDown: 'line', bellCurve: 'line', controlChart: 'line',
            confidenceBand: 'line', timeLine: 'line', rangeArea: 'line',
            slope: 'line', sparkline: 'line', stackedArea100: 'line',
            pie: 'pie', sunburst: 'pie', chordDiagram: 'pie',
            donut: 'doughnut', gauge: 'doughnut', radialProgress: 'doughnut',
            nightingaleRose: 'polarArea', polarArea: 'polarArea',
            scatter: 'scatter', dotPlot: 'scatter', networkGraph: 'scatter',
            arcDiagram: 'scatter', forceDirected: 'scatter', regressionLine: 'scatter',
            bubble: 'bubble', bubbleMap: 'bubble',
            radar: 'radar',
        };
        return map[chartType] || 'bar';
    }

    renderTableChart(chartDef, container, data, colors, h) {
        const MAX_ROWS = 50;
        const labels = (data.labels || ['A','B','C','D','E']).slice(0, MAX_ROWS);
        const values = (data.values || []).slice(0, MAX_ROWS);
        const mapping = chartDef.mapping || {};
        const labelField = mapping.labelField || 'Label';
        const valueField = mapping.valueField || 'Value';
        const primaryColor = (chartDef.style && chartDef.style.colorPalette && chartDef.style.colorPalette.startsWith('#'))
            ? chartDef.style.colorPalette : colors[0];

        container.style.cssText = 'position:absolute;inset:0;overflow:auto;padding:8px;';
        const rows = labels.map((lbl, i) =>
            `<tr><td>${this._esc(String(lbl))}</td><td class="text-end">${this._esc(String(values[i] ?? ''))}</td></tr>`
        ).join('');
        container.innerHTML = `
            <table class="table table-sm table-striped mb-0" style="font-size:12px;">
                <thead style="position:sticky;top:0;background:#fff;">
                    <tr>
                        <th style="color:${this._esc(primaryColor)}">${this._esc(labelField)}</th>
                        <th class="text-end" style="color:${this._esc(primaryColor)}">${this._esc(valueField)}</th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>`;
    }

    renderSlicer(chartDef, container, data, colors, h) {
        const labels = data.labels || ['A','B','C','D','E'];
        const uniqueValues = [...new Set(labels.map(String))];
        const primaryColor = (chartDef.style && chartDef.style.colorPalette && chartDef.style.colorPalette.startsWith('#'))
            ? chartDef.style.colorPalette : colors[0];

        container.style.cssText = 'position:absolute;inset:0;overflow:auto;padding:12px;display:flex;flex-direction:column;gap:8px;';
        const mapping = chartDef.mapping || {};
        const labelField = mapping.labelField || 'Values';

        const titleEl = document.createElement('div');
        titleEl.style.cssText = 'font-size:11px;font-weight:600;color:#6c757d;text-transform:uppercase;letter-spacing:.5px;margin-bottom:4px;';
        titleEl.textContent = labelField;

        const wrap = document.createElement('div');
        wrap.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;';

        uniqueValues.forEach(v => {
            const btn = document.createElement('button');
            btn.className = 'slicer-chip badge';
            btn.style.cssText = `background-color:${primaryColor}22;color:${primaryColor};border:1px solid ${primaryColor}66;border-radius:16px;padding:4px 12px;font-size:12px;cursor:pointer;text-align:left;font-weight:500;transition:all .15s;margin:2px;`;
            btn.textContent = String(v);
            btn.addEventListener('click', () => {
                const active = btn.dataset.active === '1';
                btn.dataset.active = active ? '0' : '1';
                btn.style.backgroundColor = active ? `${primaryColor}22` : primaryColor;
                btn.style.color = active ? primaryColor : '#fff';
            });
            wrap.appendChild(btn);
        });

        container.appendChild(titleEl);
        container.appendChild(wrap);
    }
}

window.chartRenderer = new ChartRenderer();
