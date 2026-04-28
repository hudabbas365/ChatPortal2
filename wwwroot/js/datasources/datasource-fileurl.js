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
        }
    });
})();
