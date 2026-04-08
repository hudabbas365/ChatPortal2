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
        const container = document.getElementById('chart-canvas-drop');
        if (!container || typeof window.jspdf === 'undefined') return;

        const { jsPDF } = window.jspdf;
        const canvasName = document.getElementById('canvas-name-display')?.textContent || 'Report';
        const now = new Date().toLocaleDateString('en-US', { year:'numeric', month:'long', day:'numeric' });
        const charts = window.canvasManager ? window.canvasManager.charts : [];

        // A4 page settings (mm)
        const pageW = 210, pageH = 297;
        const marginX = 14, marginY = 16;
        const contentW = pageW - marginX * 2;

        const pdf = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'a4' });

        // ---- Cover / Header Page ----
        pdf.setFillColor(74, 144, 217);
        pdf.rect(0, 0, pageW, 50, 'F');

        pdf.setTextColor(255, 255, 255);
        pdf.setFont('helvetica', 'bold');
        pdf.setFontSize(24);
        pdf.text('ManageCharts', marginX, 22);

        pdf.setFontSize(14);
        pdf.setFont('helvetica', 'normal');
        pdf.text(canvasName, marginX, 34);

        pdf.setFontSize(9);
        pdf.text(`Generated: ${now}  ·  ${charts.length} chart${charts.length !== 1 ? 's' : ''}`, marginX, 44);

        // Reset text color
        pdf.setTextColor(44, 62, 80);

        // ---- Chart listing (table of contents) ----
        let curY = 62;
        pdf.setFont('helvetica', 'bold');
        pdf.setFontSize(11);
        pdf.text('Chart Index', marginX, curY);

        pdf.setDrawColor(74, 144, 217);
        pdf.setLineWidth(0.4);
        pdf.line(marginX, curY + 2, pageW - marginX, curY + 2);
        curY += 8;

        pdf.setFont('helvetica', 'normal');
        pdf.setFontSize(9);
        charts.forEach((c, i) => {
            if (curY > pageH - 20) { pdf.addPage(); curY = marginY; }
            pdf.setFillColor(i % 2 === 0 ? 248 : 255, i % 2 === 0 ? 249 : 255, i % 2 === 0 ? 250 : 255);
            pdf.rect(marginX, curY - 4, contentW, 7, 'F');
            pdf.setFont('helvetica', 'bold');
            pdf.text(`${i + 1}.`, marginX + 1, curY);
            pdf.setFont('helvetica', 'normal');
            pdf.text(c.title || 'Untitled', marginX + 8, curY);
            pdf.setTextColor(130, 140, 160);
            pdf.text(c.chartType, pageW - marginX - pdf.getTextWidth(c.chartType), curY);
            pdf.setTextColor(44, 62, 80);
            curY += 7;
        });

        // ---- Charts: screenshot each card individually ----
        const cards = document.querySelectorAll('.chart-card');
        if (cards.length > 0) {
            pdf.addPage();
            pdf.setFillColor(74, 144, 217);
            pdf.rect(0, 0, pageW, 12, 'F');
            pdf.setTextColor(255, 255, 255);
            pdf.setFont('helvetica', 'bold');
            pdf.setFontSize(10);
            pdf.text('Charts', marginX, 8);
            pdf.setTextColor(44, 62, 80);

            curY = 20;
            const chartPerRow = 2;
            const chartCellW = (contentW - 6) / chartPerRow;
            let colIdx = 0;

            for (let i = 0; i < cards.length; i++) {
                const card = cards[i];
                const chartId = card.dataset.chartId;
                const chartDef = charts.find(c => c.id === chartId);

                try {
                    const imgCanvas = await html2canvas(card, {
                        backgroundColor: '#ffffff',
                        scale: 1.5,
                        useCORS: true,
                        logging: false
                    });

                    const imgData = imgCanvas.toDataURL('image/png');
                    const aspect = imgCanvas.height / imgCanvas.width;
                    const imgW = chartCellW;
                    const imgH = Math.min(imgW * aspect, 80);

                    const xPos = marginX + colIdx * (chartCellW + 6);

                    // Check page overflow
                    if (curY + imgH + 8 > pageH - marginY) {
                        pdf.addPage();
                        pdf.setFillColor(74, 144, 217);
                        pdf.rect(0, 0, pageW, 10, 'F');
                        curY = 16;
                        colIdx = 0;
                    }

                    // Chart title
                    pdf.setFont('helvetica', 'bold');
                    pdf.setFontSize(8);
                    pdf.setTextColor(44, 62, 80);
                    pdf.text(chartDef ? (chartDef.title || 'Chart') : 'Chart', xPos, curY);

                    // Chart image
                    pdf.addImage(imgData, 'PNG', xPos, curY + 2, imgW, imgH);

                    // Light border
                    pdf.setDrawColor(220, 227, 234);
                    pdf.setLineWidth(0.3);
                    pdf.rect(xPos, curY + 2, imgW, imgH);

                    colIdx++;
                    if (colIdx >= chartPerRow) {
                        colIdx = 0;
                        curY += imgH + 12;
                    }
                } catch (e) {
                    console.warn('Could not capture chart', e);
                }
            }
        }

        // ---- Footer on each page ----
        const totalPages = pdf.getNumberOfPages();
        for (let p = 1; p <= totalPages; p++) {
            pdf.setPage(p);
            pdf.setFont('helvetica', 'normal');
            pdf.setFontSize(7);
            pdf.setTextColor(130, 140, 160);
            pdf.text(`ManageCharts · ${canvasName} · Page ${p} of ${totalPages}`, marginX, pageH - 6);
            pdf.text(now, pageW - marginX - pdf.getTextWidth(now), pageH - 6);
            pdf.setDrawColor(220, 227, 234);
            pdf.setLineWidth(0.3);
            pdf.line(marginX, pageH - 10, pageW - marginX, pageH - 10);
        }

        pdf.save(`${canvasName.replace(/[^a-z0-9\-_]/gi,'_')}_report.pdf`);
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
