/* Phase 35 — Field Drag-and-Drop (B7 + B16)
 * Drag a field from the Data Fields panel onto the canvas to auto-create a chart.
 * Chart type is chosen from a heuristic based on the field's data type (B16).
 * Shift-drop cycles to an alternative chart type.
 *
 * Relies on:
 *   - #data-fields-list containing .data-field-row[data-field] (created by propertiesPanel.renderDataFields)
 *   - #chart-canvas-drop as the canvas drop zone
 *   - window.propertiesPanel._schemaTables / _schemaColumns for type lookup
 *   - window.canvasManager.addChart(partial)
 */
(function (global) {
    'use strict';

    function isNumeric(dt) {
        const t = (dt || '').toLowerCase();
        return t.includes('int') || t.includes('decimal') || t.includes('float') ||
               t.includes('numeric') || t.includes('money') || t.includes('double') || t.includes('real');
    }
    function isDate(dt) {
        const t = (dt || '').toLowerCase();
        return t.includes('date') || t.includes('time');
    }
    function isBool(dt) {
        const t = (dt || '').toLowerCase();
        return t.includes('bit') || t.includes('bool');
    }

    function lookupField(fieldName) {
        const pp = global.propertiesPanel;
        if (!pp) return null;
        // Try grouped tables first
        if (pp._schemaTables && pp._schemaTables.length) {
            for (const tbl of pp._schemaTables) {
                const col = (tbl.columns || []).find(c => c.name === fieldName);
                if (col) return { column: col, table: tbl };
            }
        }
        if (pp._schemaColumns && pp._schemaColumns.length) {
            const col = pp._schemaColumns.find(c => c.name === fieldName);
            if (col) return { column: col, table: null };
        }
        return null;
    }

    // B16 — choose chart type + field mapping from the dropped column's data type.
    function buildChartFromField(fieldName, useAlternate) {
        const info = lookupField(fieldName);
        const column = info?.column || { name: fieldName, dataType: '' };
        const table = info?.table;
        const dt = column.dataType || '';
        const datasetName = table?.name ||
            (document.getElementById('prop-dataset')?.value) ||
            (global._realTableNames && global._realTableNames[0]) || '';

        // Find companion columns in the same table for label/value pairings.
        const sameTableCols = table ? (table.columns || []) : [];
        const numericCol = sameTableCols.find(c => isNumeric(c.dataType) && !c.isPrimaryKey);
        const textCol   = sameTableCols.find(c => !isNumeric(c.dataType) && !isDate(c.dataType) && !c.isPrimaryKey);
        const dateCol   = sameTableCols.find(c => isDate(c.dataType));

        let chartType, mapping, title, aggregation;

        if (isNumeric(dt)) {
            // Numeric: KPI card by default (no pairing needed); alternate = bar with text companion.
            if (useAlternate && textCol) {
                chartType = 'bar';
                mapping = { labelField: textCol.name, valueField: fieldName };
                aggregation = { enabled: true, function: 'SUM' };
                title = fieldName + ' by ' + textCol.name;
            } else {
                chartType = 'kpiCard';
                mapping = { valueField: fieldName };
                aggregation = { enabled: true, function: 'SUM' };
                title = fieldName;
            }
        } else if (isDate(dt)) {
            chartType = 'line';
            mapping = { labelField: fieldName, valueField: numericCol?.name || fieldName };
            aggregation = numericCol
                ? { enabled: true, function: 'SUM' }
                : { enabled: true, function: 'COUNT' };
            title = (numericCol?.name || 'Count') + ' over ' + fieldName;
        } else if (isBool(dt)) {
            chartType = useAlternate ? 'donut' : 'pie';
            mapping = { labelField: fieldName, valueField: numericCol?.name || fieldName };
            aggregation = numericCol
                ? { enabled: true, function: 'SUM' }
                : { enabled: true, function: 'COUNT' };
            title = fieldName;
        } else {
            // Text / categorical
            if (useAlternate) {
                chartType = 'horizontalBar';
            } else if (dateCol && numericCol) {
                chartType = 'bar';
            } else {
                chartType = 'bar';
            }
            mapping = { labelField: fieldName, valueField: numericCol?.name || fieldName };
            aggregation = numericCol
                ? { enabled: true, function: 'SUM' }
                : { enabled: true, function: 'COUNT' };
            title = (numericCol?.name || 'Count') + ' by ' + fieldName;
        }

        return { chartType, mapping, title, datasetName, aggregation };
    }

    function wireDragStartEnrichment() {
        // Enrich dataTransfer so we don't have to re-look-up on drop.
        document.addEventListener('dragstart', (e) => {
            const row = e.target.closest && e.target.closest('.data-field-row');
            if (!row || !row.dataset.field) return;
            try {
                const info = lookupField(row.dataset.field);
                if (info?.column?.dataType) e.dataTransfer.setData('fieldType', info.column.dataType);
                if (info?.table?.name)      e.dataTransfer.setData('fieldTable', info.table.name);
            } catch {}
        }, true);
    }

    function wireCanvasDrop() {
        const dropZone = document.getElementById('chart-canvas-drop');
        if (!dropZone || dropZone._fieldDropWired) return;
        dropZone._fieldDropWired = true;

        dropZone.addEventListener('dragover', (e) => {
            // Tag the zone so we can style "field drop" distinctly if needed.
            if (e.dataTransfer && Array.from(e.dataTransfer.types || []).includes('fieldname')) {
                dropZone.classList.add('field-drag-over');
            }
        });
        dropZone.addEventListener('dragleave', () => dropZone.classList.remove('field-drag-over'));

        dropZone.addEventListener('drop', async (e) => {
            dropZone.classList.remove('field-drag-over');
            // Don't interfere with chart-library drops (those set `chartType`).
            const chartType = e.dataTransfer.getData('chartType');
            if (chartType) return;
            const fieldName = e.dataTransfer.getData('fieldName');
            if (!fieldName) return;
            e.preventDefault();

            const useAlternate = !!e.shiftKey;
            const spec = buildChartFromField(fieldName, useAlternate);
            if (!spec) return;

            const rect = dropZone.getBoundingClientRect();
            const x = Math.max(0, Math.round(e.clientX - rect.left - 150));
            const y = Math.max(0, Math.round(e.clientY - rect.top - 20));

            if (!global.canvasManager) return;
            await canvasManager.addChart({
                chartType: spec.chartType,
                title: spec.title,
                datasetName: spec.datasetName,
                mapping: spec.mapping,
                aggregation: spec.aggregation,
                posX: x,
                posY: y
            });
        });
    }

    function init() {
        wireDragStartEnrichment();
        wireCanvasDrop();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();

    global.fieldDropHandler = { buildChartFromField };
})(window);
