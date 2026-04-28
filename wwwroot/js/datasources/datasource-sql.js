/**
 * SQL Server datasource — front-end behavior.
 *
 * Owns: connection-string + DB user/password fields, payload building, and
 * the fallback system-prompt template. Registered with `WfDatasources` so
 * the workspace wizard never branches on type strings inline.
 */
(function () {
    'use strict';
    if (!window.WfDatasources) return;

    function $(root, id) { return (root || document).querySelector('#' + id); }

    window.WfDatasources.register({
        key: 'sql',
        label: 'SQL Server',

        // Default catch-all: anything that is NOT Power BI / REST API / File URL
        // is treated as a connection-string-style relational datasource. This
        // keeps backwards-compat with the legacy "if not pbi/rest/file then sql"
        // branching in the wizard.
        matches: function (type) {
            var t = (type || '').toLowerCase();
            if (!t) return false;
            if (/power\s*bi/.test(t)) return false;
            if (/rest\s*api/.test(t)) return false;
            if (/file\s*url/.test(t)) return false;
            return true;
        },

        showFields: function (root, isActive) {
            // SQL Server uses the "default" connection-string + cred row that
            // are always present in the wizard markup; non-SQL types hide them.
            var connStr = $(root, 'wfDsConnStr');
            if (connStr) {
                var wrapper = connStr.closest('.wf-setup-field');
                if (wrapper) wrapper.style.display = isActive ? '' : 'none';
            }
            var credRow = (root || document).querySelector('.wfe-cred-row');
            if (credRow) credRow.style.display = isActive ? '' : 'none';
        },

        buildPayload: function (payload, root) {
            payload.connectionString = ($(root, 'wfDsConnStr') || {}).value || '';
            payload.dbUser     = (($(root, 'wfDsUser')     || {}).value || '').trim() || null;
            payload.dbPassword = (($(root, 'wfDsPassword') || {}).value || '').trim() || null;
        },

        fallbackSystemPrompt: function (wsName, dsName, dsType, tables) {
            return 'You are a helpful data assistant for the "' + (wsName || 'workspace') +
                '" workspace. You have access to ' + (dsName || 'the datasource') +
                ' (' + (dsType || 'database') + '). Available tables: ' +
                ((tables || []).join(', ')) +
                '. Help users query data, generate SQL, analyze results, and create visualizations.';
        }
    });
})();
