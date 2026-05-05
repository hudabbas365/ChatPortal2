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
        },

        detailFormHtml: function (ds, ctx) {
            var esc = (ctx && ctx.esc) || function (s) { return s == null ? '' : String(s); };
            var pbiCatalogDisplay = ds.pbiConnection && ds.pbiConnection.trim().length
                ? ds.pbiConnection
                : '(not configured)';
            var tenantStatus = ds.microsoftAccountTenantId ? '(configured)' : '(not set)';
            var tenantPlaceholder = ds.microsoftAccountTenantId ? '••••••' : 'tenant-guid';
            return '' +
                '<div class="alert alert-info py-2 small mb-3">' +
                    '<i class="bi bi-info-circle me-1"></i>Leave any field blank to keep its current value. Secrets are never shown.' +
                '</div>' +
                '<div class="mb-3">' +
                    '<label class="form-label fw-bold" style="font-size:0.8rem">XMLA Endpoint <span class="text-muted fw-normal">(current: ' + esc(ds.xmlaEndpoint || '(not set)') + ')</span></label>' +
                    '<input type="text" class="form-control form-control-sm" id="dsPbiXmla" placeholder="powerbi://api.powerbi.com/v1.0/myorg/<workspace>" value="" style="font-family:monospace;font-size:0.78rem" />' +
                '</div>' +
                '<div class="mb-3">' +
                    '<label class="form-label fw-bold" style="font-size:0.8rem">Semantic Model / Catalog <span class="text-muted fw-normal">(current: ' + esc(pbiCatalogDisplay) + ')</span></label>' +
                    '<input type="text" class="form-control form-control-sm" id="dsPbiCatalog" placeholder="Dataset / semantic model name" value="" />' +
                '</div>' +
                '<div class="mb-3">' +
                    '<label class="form-label fw-bold" style="font-size:0.8rem">Tenant ID <span class="text-muted fw-normal">' + tenantStatus + '</span></label>' +
                    '<input type="text" class="form-control form-control-sm" id="dsPbiTenant" placeholder="' + tenantPlaceholder + '" value="" autocomplete="off" />' +
                '</div>' +
                '<div class="mb-3">' +
                    '<label class="form-label fw-bold" style="font-size:0.8rem">Client ID</label>' +
                    '<input type="text" class="form-control form-control-sm" id="dsPbiClientId" placeholder="••••••" value="" autocomplete="off" />' +
                '</div>' +
                '<div class="mb-3">' +
                    '<label class="form-label fw-bold" style="font-size:0.8rem">Client Secret</label>' +
                    '<input type="password" class="form-control form-control-sm" id="dsPbiClientSecret" placeholder="••••••" value="" autocomplete="new-password" />' +
                '</div>' +
                '<div id="dsPbiSaveStatus" class="small" style="min-height:1rem"></div>';
        },

        detailFooterHtml: function (/* ds, ctx */) {
            return '<button type="button" class="btn btn-sm btn-primary" id="dsPbiSaveBtn"><i class="bi bi-save me-1"></i>Save changes</button>';
        }
    });
})();
