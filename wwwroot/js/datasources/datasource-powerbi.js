/**
 * Power BI datasource — front-end behavior.
 *
 * Owns: XMLA endpoint + catalog + tenant/client/secret fields, payload
 * building, and the fallback system-prompt template.
 */
(function () {
    'use strict';
    if (!window.WfDatasources) return;

    function $(root, id) { return (root || document).querySelector('#' + id); }
    function val(root, id) { return (($(root, id) || {}).value || '').trim(); }

    window.WfDatasources.register({
        key: 'powerbi',
        label: 'Power BI',

        matches: function (type) { return /power\s*bi/i.test(type || ''); },

        showFields: function (root, isActive) {
            var pbi = $(root, 'wfDsPbiFields');
            if (pbi) pbi.style.display = isActive ? '' : 'none';
        },

        buildPayload: function (payload, root) {
            payload.xmlaEndpoint              = val(root, 'wfDsXmlaEndpoint');
            payload.connectionString          = val(root, 'wfDsCatalog');
            payload.microsoftAccountTenantId  = val(root, 'wfDsTenantId');
            payload.dbUser                    = val(root, 'wfDsClientId');
            payload.dbPassword                = val(root, 'wfDsClientSecret');
        },

        fallbackSystemPrompt: function (wsName, dsName, dsType, tables) {
            return 'You are a helpful data assistant for the "' + (wsName || 'workspace') +
                '" workspace. You have access to ' + (dsName || 'the Power BI dataset') +
                ' (' + (dsType || 'Power BI') + '). Available tables: ' +
                ((tables || []).join(', ')) +
                '. Help users analyze the semantic model, write DAX queries, and create visualizations.';
        }
    });
})();
