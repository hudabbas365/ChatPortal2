/**
 * Datasource registry — fan-out point for per-type front-end logic.
 *
 * Each datasource family (SQL Server, Power BI, REST API, File URL) registers
 * a small descriptor here. The workspace wizard / settings panels then call
 * `WfDatasources.forType(name)` instead of branching on type strings inline.
 *
 * Mirrors the server-side `IDatasourceTypeService` pattern.
 *
 * Descriptor shape:
 *   {
 *     key:        string,                                        // canonical key, e.g. 'sql'
 *     label:      string,                                        // human label shown in pickers
 *     matches:    function(typeString): boolean,                 // type-string sniffer
 *     showFields: function(rootEl, isActive): void,              // toggle this type's input block
 *     buildPayload:        function(payload, rootEl): void,      // mutates outbound test/create payload
 *     fallbackSystemPrompt: function(wsName, dsName, dsType, tables): string
 *   }
 */
(function () {
    'use strict';

    var registry = [];

    window.WfDatasources = {
        register: function (descriptor) {
            if (descriptor && typeof descriptor.matches === 'function') registry.push(descriptor);
        },

        all: function () { return registry.slice(); },

        forType: function (typeString) {
            for (var i = 0; i < registry.length; i++) {
                if (registry[i].matches(typeString)) return registry[i];
            }
            return null;
        },

        /** Hides every per-type field block, then asks the matching descriptor (if any) to show its own. */
        toggleFields: function (rootEl, typeString) {
            var match = this.forType(typeString);
            registry.forEach(function (d) { d.showFields(rootEl, d === match); });
        },

        /** Walks every registered descriptor and lets the matching one populate the payload. */
        buildPayload: function (payload, rootEl, typeString) {
            var match = this.forType(typeString);
            if (match) match.buildPayload(payload, rootEl);
        },

        fallbackSystemPrompt: function (typeString, wsName, dsName, dsType, tables) {
            var match = this.forType(typeString);
            if (match) return match.fallbackSystemPrompt(wsName, dsName, dsType, tables);
            return '';
        }
    };
})();
