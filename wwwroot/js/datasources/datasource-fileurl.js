/**
 * File URL datasource — front-end behavior.
 *
 * Owns: file URL + format-override fields, payload building, and the fallback
 * system-prompt template. Public/anonymous CSV / XLSX share links are fetched
 * and parsed server-side by `IFileDatasourceService`.
 */
(function () {
    'use strict';
    if (!window.WfDatasources) return;

    function $(root, id) { return (root || document).querySelector('#' + id); }
    function val(root, id) { return (($(root, id) || {}).value || '').trim(); }

    window.WfDatasources.register({
        key: 'fileurl',
        label: 'File URL',

        matches: function (type) { return /file\s*url/i.test(type || ''); },

        showFields: function (root, isActive) {
            var fields = $(root, 'wfDsFileUrlFields');
            if (fields) fields.style.display = isActive ? '' : 'none';
        },

        buildPayload: function (payload, root) {
            payload.apiUrl    = val(root, 'wfDsFileUrl');
            payload.apiMethod = ($(root, 'wfDsFileFormat') || {}).value || 'Auto';
        },

        fallbackSystemPrompt: function (wsName, dsName, dsType /*, tables */) {
            return 'You are a helpful data assistant for the "' + (wsName || 'workspace') +
                '" workspace. You are connected to ' + (dsName || 'a CSV/Excel file') +
                ' (' + (dsType || 'File URL') +
                '). The file is fetched and parsed automatically. Help users analyze the data, ' +
                'suggest charts and visualizations. Always set query to "FILE_URL".';
        },

        detailFormHtml: function (ds, ctx) {
            var esc = (ctx && ctx.esc) || function (s) { return s == null ? '' : String(s); };
            var m = (ds.apiMethod || '').toLowerCase();
            var fileFormatLabel = m === 'csv' ? 'CSV'
                                : m === 'xlsx' ? 'Excel (XLSX)'
                                : (m === 'auto' || !m) ? 'Auto-detect'
                                : ds.apiMethod;
            return '' +
                '<div class="mb-3">' +
                    '<label class="form-label fw-bold" style="font-size:0.8rem">File URL</label>' +
                    '<input type="text" class="form-control form-control-sm" readonly value="' + esc(ds.apiUrl || '(not set)') + '" style="font-family:monospace;font-size:0.78rem" />' +
                '</div>' +
                '<div class="mb-3">' +
                    '<label class="form-label fw-bold" style="font-size:0.8rem">File Format</label>' +
                    '<input type="text" class="form-control form-control-sm" readonly value="' + esc(fileFormatLabel) + '" />' +
                '</div>';
        }
    });
})();
