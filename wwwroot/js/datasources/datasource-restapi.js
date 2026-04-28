/**
 * REST API datasource — front-end behavior.
 *
 * Owns: HTTP method + API URL + API key fields, payload building, and the
 * fallback system-prompt template.
 */
(function () {
    'use strict';
    if (!window.WfDatasources) return;

    function $(root, id) { return (root || document).querySelector('#' + id); }
    function val(root, id) { return (($(root, id) || {}).value || '').trim(); }

    window.WfDatasources.register({
        key: 'restapi',
        label: 'REST API',

        matches: function (type) { return /rest\s*api/i.test(type || ''); },

        showFields: function (root, isActive) {
            var fields = $(root, 'wfDsRestApiFields');
            if (fields) fields.style.display = isActive ? '' : 'none';
        },

        buildPayload: function (payload, root) {
            payload.apiUrl    = val(root, 'wfDsApiUrl');
            payload.apiKey    = val(root, 'wfDsApiKey');
            payload.apiMethod = ($(root, 'wfDsApiMethod') || {}).value || 'GET';
        },

        fallbackSystemPrompt: function (wsName, dsName, dsType /*, tables */) {
            return 'You are a helpful data assistant for the "' + (wsName || 'workspace') +
                '" workspace. You are connected to ' + (dsName || 'a REST API') +
                ' (' + (dsType || 'REST API') +
                '). Data is fetched automatically from the API. Help users analyze the API data, ' +
                'suggest charts and visualizations. Always set query to "REST_API".';
        }
    });
})();
