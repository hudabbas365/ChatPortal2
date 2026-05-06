// Export manager - handles PDF, PNG, JSON exports
class ExportManager {
    async exportPng() {
        const canvas = document.getElementById('chart-canvas-drop');
        if (!canvas) return;
        const c = await html2canvas(canvas, { backgroundColor: '#f4f6f9', scale: 2, useCORS: true });
        const link = document.createElement('a');
        link.download = 'report.png';
        link.href = c.toDataURL('image/png');
        link.click();
    }

    async exportPdf() {
        // One report-page per PDF-page, scale-to-fit so a tall canvas never
        // overlaps the next sheet. Iterates window.canvasManager.pages,
        // switching the active page so each one is fully rendered before
        // capture, then restores the original active page when finished.
        if (typeof window.jspdf === 'undefined') { alert('PDF library not loaded.'); return; }
        const cm = window.canvasManager;
        if (!cm) return;
        const container = document.getElementById('chart-canvas-drop');
        if (!container) return;

        const pages = (cm.pages && cm.pages.length) ? cm.pages : [{ name: 'Page 1', charts: cm.charts || [] }];
        const originalIndex = cm.activePageIndex || 0;
        const canvasName = document.getElementById('canvas-name-display')?.textContent || 'Report';

        const overlay = this._showExportOverlay('Generating PDF…');
        try {
            const { jsPDF } = window.jspdf;
            let pdf = null;

            for (let i = 0; i < pages.length; i++) {
                this._updateExportOverlay(overlay, `Generating PDF… (page ${i + 1} of ${pages.length})`);
                if (cm.activePageIndex !== i) {
                    cm.switchPage(i);
                    // Charts re-render via requestAnimationFrame; give them
                    // time to fetch data and paint.
                    await new Promise(r => setTimeout(r, 700));
                }

                const canvas = await html2canvas(container, {
                    scale: 2, useCORS: true, backgroundColor: '#ffffff', logging: false,
                    windowWidth: container.scrollWidth,
                    windowHeight: container.scrollHeight
                });
                const imgData = canvas.toDataURL('image/png');
                const imgW = canvas.width, imgH = canvas.height;
                const orientation = imgW >= imgH ? 'landscape' : 'portrait';

                if (!pdf) pdf = new jsPDF({ orientation, unit: 'mm', format: 'a4' });
                else pdf.addPage('a4', orientation);

                const pageW = pdf.internal.pageSize.getWidth();
                const pageH = pdf.internal.pageSize.getHeight();
                const margin = 8;
                const availW = pageW - margin * 2;
                const availH = pageH - margin * 2;
                const ratio = Math.min(availW / imgW, availH / imgH);
                const drawW = imgW * ratio;
                const drawH = imgH * ratio;
                pdf.addImage(imgData, 'PNG', (pageW - drawW) / 2, (pageH - drawH) / 2, drawW, drawH);
            }

            pdf.save(`${canvasName.replace(/[^a-z0-9\-_]/gi,'_')}_report.pdf`);
        } catch (e) {
            console.error('PDF export failed:', e);
            alert('Failed to export PDF.');
        } finally {
            // Restore the originally active page.
            if (cm.activePageIndex !== originalIndex) {
                cm.switchPage(originalIndex);
            }
            this._hideExportOverlay(overlay);
        }
    }

    // PowerPoint export — one report page per slide, 16:9 wide layout.
    async exportPpt() {
        if (typeof PptxGenJS === 'undefined') {
            alert('PowerPoint export library failed to load.');
            return;
        }
        const cm = window.canvasManager;
        if (!cm) return;
        const container = document.getElementById('chart-canvas-drop');
        if (!container) return;

        const pages = (cm.pages && cm.pages.length) ? cm.pages : [{ name: 'Page 1', charts: cm.charts || [] }];
        const originalIndex = cm.activePageIndex || 0;
        const canvasName = document.getElementById('canvas-name-display')?.textContent || 'Report';

        const overlay = this._showExportOverlay('Generating PowerPoint…');
        try {
            const pptx = new PptxGenJS();
            pptx.layout = 'LAYOUT_WIDE';
            const slideW = 13.333, slideH = 7.5;

            for (let i = 0; i < pages.length; i++) {
                this._updateExportOverlay(overlay, `Generating PowerPoint… (slide ${i + 1} of ${pages.length})`);
                if (cm.activePageIndex !== i) {
                    cm.switchPage(i);
                    await new Promise(r => setTimeout(r, 700));
                }

                const canvas = await html2canvas(container, {
                    scale: 2, useCORS: true, backgroundColor: '#ffffff', logging: false,
                    windowWidth: container.scrollWidth,
                    windowHeight: container.scrollHeight
                });
                const imgData = canvas.toDataURL('image/png');
                const imgW = canvas.width, imgH = canvas.height;
                const ratio = Math.min(slideW / imgW, slideH / imgH);
                const drawW = imgW * ratio;
                const drawH = imgH * ratio;
                const slide = pptx.addSlide();
                slide.background = { color: 'FFFFFF' };
                slide.addImage({
                    data: imgData,
                    x: (slideW - drawW) / 2,
                    y: (slideH - drawH) / 2,
                    w: drawW, h: drawH
                });
            }

            await pptx.writeFile({ fileName: `${canvasName.replace(/[^a-z0-9\-_]/gi,'_')}_report.pptx` });
        } catch (e) {
            console.error('PPT export failed:', e);
            alert('Failed to export PowerPoint.');
        } finally {
            if (cm.activePageIndex !== originalIndex) {
                cm.switchPage(originalIndex);
            }
            this._hideExportOverlay(overlay);
        }
    }

    // Per-chart CSV export. Uses data already cached by chartRenderer when
    // available; otherwise re-fetches via chartRenderer.fetchData.
    async exportChartCsv(chartDef) {
        try {
            let data = null;
            if (window.chartRenderer && chartRenderer._lastData && chartDef && chartDef.id != null) {
                data = chartRenderer._lastData[chartDef.id];
            }
            if (!data && window.chartRenderer && typeof chartRenderer.fetchData === 'function') {
                data = await chartRenderer.fetchData(chartDef);
            }
            if (!data) { alert('No data available for this chart yet.'); return; }

            const esc = (v) => {
                if (v === null || v === undefined) return '';
                const s = String(v);
                if (/[",\r\n]/.test(s)) return '"' + s.replace(/"/g, '""') + '"';
                return s;
            };
            const buildCsv = (rows, headers) => {
                const out = [headers.map(esc).join(',')];
                rows.forEach(r => out.push(headers.map(h => esc(r[h])).join(',')));
                return out.join('\r\n');
            };

            let csv;
            if (Array.isArray(data.rawData) && data.rawData.length > 0) {
                const headers = Object.keys(data.rawData[0]);
                csv = buildCsv(data.rawData, headers);
            } else if (Array.isArray(data.labels) && data.labels.length > 0) {
                const headers = ['Label', 'Value'];
                const rows = data.labels.map((l, i) => ({
                    Label: l,
                    Value: (data.values && data.values[i] !== undefined) ? data.values[i] : ''
                }));
                if (Array.isArray(data.multiValues)) {
                    data.multiValues.forEach(mv => {
                        headers.push(mv.field);
                        rows.forEach((r, i) => {
                            r[mv.field] = (mv.values && mv.values[i] !== undefined) ? mv.values[i] : '';
                        });
                    });
                }
                csv = buildCsv(rows, headers);
            } else {
                alert('No data available for this chart yet.');
                return;
            }

            const baseName = (chartDef && (chartDef.title || chartDef.chartType)) || 'chart';
            const safe = String(baseName).replace(/[^a-zA-Z0-9_\- ]/g, '').trim() || 'chart';
            const blob = new Blob(['\ufeff' + csv], { type: 'text/csv;charset=utf-8;' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url; a.download = safe + '.csv';
            document.body.appendChild(a); a.click();
            setTimeout(() => { document.body.removeChild(a); URL.revokeObjectURL(url); }, 0);
        } catch (e) {
            console.error('Chart CSV export failed:', e);
            alert('Failed to export CSV.');
        }
    }

    // ── Lightweight overlay used by PDF / PPT exports ──
    _showExportOverlay(label) {
        let overlay = document.getElementById('em-export-overlay');
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.id = 'em-export-overlay';
            overlay.style.cssText = 'position:fixed;inset:0;background:rgba(15,23,42,0.45);z-index:99999;display:flex;align-items:center;justify-content:center;';
            overlay.innerHTML = '<div style="background:#fff;border-radius:10px;padding:22px 28px;display:flex;align-items:center;gap:14px;min-width:260px;box-shadow:0 12px 40px rgba(0,0,0,0.25);"><div class="spinner-border text-primary" role="status" style="width:1.6rem;height:1.6rem;"></div><span id="em-export-label" style="font-size:0.92rem;color:#334155;font-weight:500;"></span></div>';
            document.body.appendChild(overlay);
        }
        const labelEl = overlay.querySelector('#em-export-label');
        if (labelEl) labelEl.textContent = label || 'Working…';
        overlay.style.display = 'flex';
        return overlay;
    }
    _updateExportOverlay(overlay, label) {
        if (!overlay) return;
        const labelEl = overlay.querySelector('#em-export-label');
        if (labelEl) labelEl.textContent = label;
    }
    _hideExportOverlay(overlay) {
        if (overlay) overlay.style.display = 'none';
    }

    exportJson() {
        const charts = window.canvasManager ? window.canvasManager.charts : [];
        const blob = new Blob([JSON.stringify(charts, null, 2)], { type: 'application/json' });
        const link = document.createElement('a');
        link.download = 'report-config.json';
        link.href = URL.createObjectURL(blob);
        link.click();
    }

    async importJson(file) {
        if (!file) return;
        const text = await file.text();
        try {
            const charts = JSON.parse(text);
            if (!Array.isArray(charts)) throw new Error('Invalid format');
            if (window.canvasManager) {
                window.canvasManager.charts = charts;
                window.canvasManager.renderAll();
            }
        } catch(e) {
            console.error('Import failed:', e);
            alert('Could not import file. Please ensure it is a valid report JSON export.');
        }
    }

    async exportXml() {
        const resp = await fetch('/api/report/export/xml');
        if (!resp.ok) { alert('Export failed'); return; }
        const blob = await resp.blob();
        const link = document.createElement('a');
        const canvasName = document.getElementById('canvas-name-display')?.textContent || 'report';
        link.download = canvasName.replace(/[^a-z0-9\-_]/gi, '_') + '.xml';
        link.href = URL.createObjectURL(blob);
        link.click();
    }

    async importXml(file) {
        if (!file) return;
        const formData = new FormData();
        formData.append('file', file);
        const resp = await fetch('/api/report/import/xml', { method: 'POST', body: formData });
        if (!resp.ok) { alert('Import failed. Ensure the file is a valid ManageCharts XML export.'); return; }
        const data = await resp.json();
        if (window.canvasManager) {
            canvasManager.pages = data.pages || [{ name: 'Page 1', charts: data.charts || [] }];
            canvasManager.activePageIndex = data.activePageIndex || 0;
            // Renumber pages sequentially after import
            canvasManager.pages.forEach((p, i) => { p.name = `Page ${i + 1}`; });
            canvasManager.charts = canvasManager.pages[canvasManager.activePageIndex].charts || [];
            canvasManager.renderAll();
            canvasManager.renderPageTabs();
        }
    }
}

window.exportManager = new ExportManager();
