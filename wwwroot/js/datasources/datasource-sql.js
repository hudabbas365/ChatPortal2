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

        // Explicit allow-list mirroring the server-side `QueryExecutionService.SqlTypes`
        // HashSet ("SQL Server", "SqlServer", "MSSQL"). Narrowed from the previous
        // catch-all so that adding a 5th datasource type doesn't accidentally fall
        // through to SQL handling.
        matches: function (type) {
            return /^\s*(sql\s*server|sqlserver|mssql)\s*$/i.test(type || '');
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
        },

        detailFormHtml: function (ds, ctx) {
            var esc = (ctx && ctx.esc) || function (s) { return s == null ? '' : String(s); };
            var connDisplay = ds.connectionString && ds.connectionString.trim().length
                ? ds.connectionString
                : '(not configured — open the workspace flow editor to set the connection string)';
            var userBlock = ds.dbUser ? (
                '<div class="mb-3">' +
                    '<label class="form-label fw-bold" style="font-size:0.8rem">Database User</label>' +
                    '<input type="text" class="form-control form-control-sm" readonly value="' + esc(ds.dbUser) + '" />' +
                '</div>'
            ) : '';
            return '' +
                '<div class="mb-3">' +
                    '<label class="form-label fw-bold" style="font-size:0.8rem">Connection String</label>' +
                    '<textarea class="form-control form-control-sm" readonly rows="3" style="resize:none;font-family:monospace;font-size:0.78rem">' + esc(connDisplay) + '</textarea>' +
                '</div>' +
                userBlock;
        }
    });
})();
