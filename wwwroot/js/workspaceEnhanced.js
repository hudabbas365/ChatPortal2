// WorkspaceEnhanced — Extra features layered on top of WorkspaceFlow
// 1) Delete workspace / artifacts   2) Dashboard top-nav tab
// 3) Datasource credential fields   4) AI system-prompt generation
// 5) Table / view selector          6) Edit / delete workspace icons
(function () {
    'use strict';

    const WFE = {
        _WF: null,

        /* ── Bootstrap ─────────────────────────────────────── */
        init() {
            const WF = window.workspaceFlow;
            if (!WF) return;
            this._WF = WF;
            this._patchMethods();
            this._injectDashboardTab();
            this._enhanceAllWorkspaceItems();
        },

        /* ── Monkey-patch WorkspaceFlow methods ────────────── */
        _patchMethods() {
            const WF = this._WF;
            const self = this;

            const origAddWs       = WF._addWorkspaceToList.bind(WF);
            const origRenderHome  = WF._renderHome.bind(WF);
            const origSelectWs    = WF._selectWorkspace.bind(WF);

            // Feature 6 — workspace list items get edit / delete icons
            WF._addWorkspaceToList = function (ws) {
                origAddWs(ws);
            };

            // Features 1, 2, 6 — home panel enhancements
            WF._renderHome = function (data) {
                origRenderHome(data);
                self._injectArtifactDeleteBtns(data);
                self._updateDashboardTab(data);
                self._injectHomeHeaderActions(data);
            };

            // Feature 6 — enhance workspace items after selection
            WF._selectWorkspace = async function (guid) {
                await origSelectWs(guid);
            };
        },

        /* ═══════════════════════════════════════════════════
           Feature 6 — Edit / delete icons beside workspace name
           ═══════════════════════════════════════════════════ */
        _enhanceAllWorkspaceItems() {
            document.querySelectorAll('#workspaceList .panel-list-item[data-workspace-id]').forEach(item => {
                var guid = item.dataset.workspaceId;
                if (guid && guid !== '0') this._enhanceWorkspaceItem(guid);
            });
        },

        _enhanceWorkspaceItem(guid) {
            if (!guid || guid === '0') return;
            var WR = window.workspaceRoles;
            if (!WR || !WR.canEdit()) return;
            var item = document.querySelector('#workspaceList .panel-list-item[data-workspace-id="' + guid + '"]');
            if (!item || item.querySelector('.wfe-ws-actions')) return;

            var isAdmin = WR.canAdmin();
            var actions = document.createElement('span');
            actions.className = 'wfe-ws-actions';
            actions.innerHTML =
                '<button class="wfe-ws-btn wfe-ws-edit" title="Rename"><i class="bi bi-pencil"></i></button>' +
                (isAdmin ? '<button class="wfe-ws-btn wfe-ws-del" title="Delete"><i class="bi bi-trash3"></i></button>' : '');
            item.appendChild(actions);

            actions.querySelector('.wfe-ws-edit').addEventListener('click', function (e) {
                e.stopPropagation();
                WFE._renameWorkspace(guid, item);
            });
            var delBtn = actions.querySelector('.wfe-ws-del');
            if (delBtn) {
                delBtn.addEventListener('click', function (e) {
                    e.stopPropagation();
                    WFE._deleteWorkspace(guid, item);
                });
            }
        },

        _renameWorkspace(guid, item) {
            var textNode = null;
            for (var i = 0; i < item.childNodes.length; i++) {
                var n = item.childNodes[i];
                if (n.nodeType === Node.TEXT_NODE && n.textContent.trim()) { textNode = n; break; }
            }
            if (!textNode) return;

            var currentName = textNode.textContent.trim();
            var input = document.createElement('input');
            input.type = 'text';
            input.className = 'wfe-rename-input';
            input.value = currentName;
            textNode.replaceWith(input);
            input.focus();
            input.select();

            var done = false;
            var self = this;
            var finish = async function (save) {
                if (done) return;
                done = true;
                var newName = save ? (input.value.trim() || currentName) : currentName;
                input.replaceWith(document.createTextNode(newName));
                if (save && newName !== currentName) {
                    try {
                        var resp = await fetch('/api/workspaces/' + guid, {
                            method: 'PUT',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ name: newName })
                        });
                        if (resp.status === 409) {
                            var err = await resp.json().catch(function () { return null; });
                            alert((err && err.error) || 'A workspace with this name already exists.');
                            // Revert the text
                            for (var j = 0; j < item.childNodes.length; j++) {
                                var nd = item.childNodes[j];
                                if (nd.nodeType === Node.TEXT_NODE && nd.textContent.trim()) {
                                    nd.textContent = currentName;
                                    break;
                                }
                            }
                            return;
                        }
                        if (self._WF._selectedWsId === guid) {
                            var t = document.getElementById('chatWorkspaceTitle');
                            var s = document.getElementById('chatSubnavWorkspaceName');
                            if (t) t.textContent = newName;
                            if (s) s.textContent = newName;
                        }
                    } catch { /* ignore */ }
                }
            };

            input.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') finish(true);
                if (e.key === 'Escape') finish(false);
            });
            input.addEventListener('blur', function () { finish(true); });
        },

        async _deleteWorkspace(guid, item) {
            var ok = await (window.cpConfirm ? window.cpConfirm({
                title: 'Delete workspace',
                message: 'Delete this workspace and all its artifacts?',
                subMessage: 'Datasources, agents, dashboards and reports inside this workspace will be permanently removed. This cannot be undone.',
                confirmText: 'Delete workspace',
                variant: 'danger',
                icon: 'bi-folder-x'
            }) : Promise.resolve(confirm('Delete this workspace and all its artifacts? This cannot be undone.')));
            if (!ok) return;
            try {
                var r = await fetch('/api/workspaces/' + guid, { method: 'DELETE' });
                if (!r.ok) throw new Error();
                this._purgeChatHistory(guid);
                if (item) item.remove();
                if (this._WF._selectedWsId === guid) {
                    this._WF._selectedWsId = null;
                    this._WF._wsData = null;
                    this._WF._showLanding();
                    this._hideDashboardTab();
                }
            } catch {
                alert('Failed to delete workspace.');
            }
        },

        // Remove cached chat history from localStorage so deleted artifacts
        // don't leave stale conversations behind.
        // Keys are written by chat.js as `cp_chats_{wsId}` or
        // `cp_chats_{wsId}_{agentId}`.
        _purgeChatHistory(wsGuid, agentGuid) {
            try {
                if (!wsGuid) return;
                if (agentGuid) {
                    localStorage.removeItem('cp_chats_' + wsGuid + '_' + agentGuid);
                    return;
                }
                // Whole-workspace purge: scan and remove every chat bucket
                // belonging to this workspace (with or without agent suffix).
                var prefix = 'cp_chats_' + wsGuid;
                var toRemove = [];
                for (var i = 0; i < localStorage.length; i++) {
                    var k = localStorage.key(i);
                    if (k && (k === prefix || k.indexOf(prefix + '_') === 0)) toRemove.push(k);
                }
                toRemove.forEach(function (k) { localStorage.removeItem(k); });
            } catch { /* best-effort */ }
        },

        /* ═══════════════════════════════════════════════════
           Feature 1 — Delete AI Insights (cascade: datasource + agents + reports)
           ═══════════════════════════════════════════════════ */
        _injectArtifactDeleteBtns(data) {
            var WR = window.workspaceRoles;
            if (!WR || !WR.canAdmin()) return;
            var self = this;
            // Single cascade delete button on each lineage row (datasource group)
            document.querySelectorAll('.wf-flow-diagram').forEach(function (row) {
                var dsNode = row.querySelector('.wf-flow-node.wf-flow-datasource[data-ds-id]');
                if (!dsNode || row.querySelector('.wfe-insights-del')) return;
                var dsGuid = dsNode.dataset.dsId;
                var btn = document.createElement('button');
                btn.className = 'wfe-insights-del';
                btn.title = 'Delete all AI Insights (datasource, agents, reports)';
                btn.innerHTML = '<i class="bi bi-trash3 me-1"></i>Delete';
                row.style.position = 'relative';
                row.appendChild(btn);
                btn.addEventListener('click', async function (e) {
                    e.stopPropagation();
                    var ok = await (window.cpConfirm ? window.cpConfirm({
                        title: 'Delete AI Insights group',
                        message: 'Delete this entire AI Insights group?',
                        subMessage: 'The datasource, all bound agents, dashboards and reports will be permanently removed. This cannot be undone.',
                        confirmText: 'Delete group',
                        variant: 'danger',
                        icon: 'bi-trash3-fill'
                    }) : Promise.resolve(confirm('Delete this AI Insights group? This cannot be undone.')));
                    if (!ok) return;
                    try {
                        btn.disabled = true;
                        btn.innerHTML = '<i class="bi bi-hourglass-split me-1"></i>Deleting...';
                        var r = await fetch('/api/workspaces/' + encodeURIComponent(data.guid) + '/insights/' + encodeURIComponent(dsGuid), { method: 'DELETE' });
                        // Treat 404 as "already gone" — the datasource may have
                        // been cleaned up by an earlier wizard-cancel/orphan sweep.
                        // Still refresh the UI so the stale lineage row disappears.
                        if (!r.ok && r.status !== 404) throw new Error();
                        // Cascade also wipes any cached chat history for agents
                        // that were bound to this workspace's datasource.
                        self._purgeChatHistory(data.guid);
                        // Optimistic UI: remove the lineage row immediately so the user
                        // sees the deletion reflect without waiting on a full re-render.
                        if (row && row.parentNode) row.remove();
                        // Then refresh workspace view to reconcile the rest of the UI
                        // (status badges, counts, empty-state setup wizard, etc.)
                        if (data.guid) await self._WF._selectWorkspace(data.guid);
                    } catch {
                        alert('Failed to delete AI Insights.');
                        btn.disabled = false;
                        btn.innerHTML = '<i class="bi bi-trash3 me-1"></i>Delete';
                    }
                });
            });
            // Keep individual delete for reports not in a lineage row
            document.querySelectorAll('[data-action="report-view"]').forEach(function (el) {
                if (el.closest('.wf-lineage-row')) return; // handled by cascade
                self._appendDeleteBtn(el, 'report', el.dataset.reportGuid, data);
            });
        },

        _appendDeleteBtn(el, type, guid, wsData) {
            var WR = window.workspaceRoles;
            if (!WR || !WR.canAdmin()) return;
            if (el.querySelector('.wfe-artifact-del')) return;
            var self = this;
            var btn = document.createElement('button');
            btn.className = 'wfe-artifact-del';
            btn.title = 'Delete ' + type;
            btn.innerHTML = '<i class="bi bi-trash3"></i>';
            el.style.position = 'relative';
            el.appendChild(btn);

            btn.addEventListener('click', async function (e) {
                e.stopPropagation();
                var labelMap = { agent: 'AI agent', report: 'report', datasource: 'datasource' };
                var label = labelMap[type] || type;
                var ok = await (window.cpConfirm ? window.cpConfirm({
                    title: 'Delete ' + label,
                    message: 'Delete this ' + label + '?',
                    subMessage: 'This action cannot be undone.',
                    confirmText: 'Delete',
                    variant: 'danger',
                    icon: 'bi-trash3-fill'
                }) : Promise.resolve(confirm('Delete this ' + type + '? This cannot be undone.')));
                if (!ok) return;
                try {
                    var url = type === 'agent' ? '/api/agents/' + guid
                        : type === 'report' ? '/api/reports/' + guid
                        : '/api/datasources/' + guid;
                    var r = await fetch(url, { method: 'DELETE' });
                    if (!r.ok) throw new Error();
                    // Clear cached chat history so deleted agents/datasources
                    // don't leave stale conversations behind in localStorage.
                    if (wsData && wsData.guid) {
                        if (type === 'agent') self._purgeChatHistory(wsData.guid, guid);
                        else if (type === 'datasource') self._purgeChatHistory(wsData.guid);
                    }
                    // Optimistic UI: remove the host element so the user sees
                    // the change immediately, then reconcile via re-fetch.
                    if (el && el.parentNode) el.remove();
                    if (wsData.guid) await self._WF._selectWorkspace(wsData.guid);
                } catch {
                    alert('Failed to delete ' + type + '.');
                }
            });
        },

        /* ═══════════════════════════════════════════════════
           Feature 2 — Dashboard tab in subnav (removed)
           ═══════════════════════════════════════════════════ */
        _injectDashboardTab() {
            // Dashboard tab removed from chat subnav
        },

        _updateDashboardTab(data) {
        },

        _hideDashboardTab() {
        },

        /* ═══════════════════════════════════════════════════
           Feature 6 part 2 — Edit / delete in home header
           ═══════════════════════════════════════════════════ */
        _injectHomeHeaderActions(data) {
            var WR = window.workspaceRoles;
            if (!WR || !WR.canEdit()) return;
            var meta = document.querySelector('.wf-home-header .wf-home-meta');
            if (!meta || meta.querySelector('.wfe-header-actions')) return;
            var self = this;
            var isAdmin = WR.canAdmin();

            var wrap = document.createElement('div');
            wrap.className = 'wfe-header-actions';
            wrap.innerHTML =
                '<button class="wfe-hdr-btn" title="Rename workspace"><i class="bi bi-pencil"></i></button>' +
                (isAdmin ? '<button class="wfe-hdr-btn wfe-hdr-del" title="Delete workspace"><i class="bi bi-trash3"></i></button>' : '');
            meta.appendChild(wrap);

            // Rename via contentEditable
            wrap.querySelector('.wfe-hdr-btn:first-child').addEventListener('click', function () {
                var titleEl = meta.querySelector('.wf-home-title');
                if (!titleEl) return;
                var current = titleEl.textContent;
                titleEl.contentEditable = 'true';
                titleEl.focus();
                var range = document.createRange();
                range.selectNodeContents(titleEl);
                var sel = window.getSelection();
                sel.removeAllRanges();
                sel.addRange(range);

                var saved = false;
                var save = async function () {
                    if (saved) return;
                    saved = true;
                    titleEl.contentEditable = 'false';
                    var newName = titleEl.textContent.trim() || current;
                    titleEl.textContent = newName;
                    if (newName !== current) {
                        try {
                            await fetch('/api/workspaces/' + data.guid, {
                                method: 'PUT',
                                headers: { 'Content-Type': 'application/json' },
                                body: JSON.stringify({ name: newName })
                            });
                            var item = document.querySelector('#workspaceList .panel-list-item[data-workspace-id="' + data.guid + '"]');
                            if (item) {
                                for (var i = 0; i < item.childNodes.length; i++) {
                                    var n = item.childNodes[i];
                                    if (n.nodeType === Node.TEXT_NODE && n.textContent.trim()) {
                                        n.textContent = newName;
                                        break;
                                    }
                                }
                            }
                            var t = document.getElementById('chatWorkspaceTitle');
                            var sn = document.getElementById('chatSubnavWorkspaceName');
                            if (t) t.textContent = newName;
                            if (sn) sn.textContent = newName;
                        } catch { /* ignore */ }
                    }
                };

                titleEl.addEventListener('keydown', function handler(e) {
                    if (e.key === 'Enter') { e.preventDefault(); save(); titleEl.removeEventListener('keydown', handler); }
                    if (e.key === 'Escape') { titleEl.textContent = current; titleEl.contentEditable = 'false'; saved = true; titleEl.removeEventListener('keydown', handler); }
                });
                titleEl.addEventListener('blur', save, { once: true });
            });

            // Delete workspace from home header
            var delBtn = wrap.querySelector('.wfe-hdr-del');
            if (delBtn) {
                delBtn.addEventListener('click', function () {
                    var item = document.querySelector('#workspaceList .panel-list-item[data-workspace-id="' + data.guid + '"]');
                    self._deleteWorkspace(data.guid, item);
                });
            }
        },

        /* ═══════════════════════════════════════════════════
           Feature 4 — AI system-prompt generation
           ═══════════════════════════════════════════════════ */
        _injectAIPromptBtn(wsData) {
            var promptField = document.getElementById('wfAgentPrompt');
            if (!promptField) return;
            var parent = promptField.parentElement;
            if (parent.querySelector('.wfe-ai-btn-row')) return;

            // Supply a sensible default prompt
            if (!promptField.value) {
                promptField.value = 'You are a helpful data assistant. Analyze data, generate SQL queries, and provide insights.';
            }

            var row = document.createElement('div');
            row.className = 'wfe-ai-btn-row';
            row.innerHTML = '<button class="btn btn-sm btn-outline-info wfe-ai-gen-btn" type="button"><i class="bi bi-stars me-1"></i>Generate with AI</button>';
            parent.insertBefore(row, promptField);

            row.querySelector('.wfe-ai-gen-btn').addEventListener('click', async function (e) {
                var btn = e.currentTarget;
                btn.disabled = true;
                btn.innerHTML = '<i class="bi bi-hourglass-split me-1"></i>Generating...';
                try {
                    var agentName = document.getElementById('wfAgentName')?.value.trim() || 'Data Assistant';
                    var r = await fetch('/api/agents/generate-prompt', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ agentName: agentName, workspaceName: wsData.name || '' })
                    });
                    if (!r.ok) throw new Error();
                    var result = await r.json();
                    promptField.value = result.prompt;
                } catch {
                    var name = document.getElementById('wfAgentName')?.value.trim() || 'a data assistant';
                    promptField.value = 'You are ' + name + ' for the "' + (wsData.name || 'workspace') + '" workspace. Help users query data, generate SQL, analyze results, and create visualizations. Always explain your reasoning clearly.';
                }
                btn.disabled = false;
                btn.innerHTML = '<i class="bi bi-stars me-1"></i>Generate with AI';
            });
        },

        /* ═══════════════════════════════════════════════════
           Feature 3 — Optional datasource credential fields
           ═══════════════════════════════════════════════════ */
        _injectCredentialFields() {
            var connField = document.getElementById('wfDsConnStr');
            if (!connField) return;
            var fieldParent = connField.closest('.wf-setup-field');
            if (!fieldParent || document.getElementById('wfDsUser')) return;

            var html =
                '<div class="wfe-cred-row">' +
                    '<div class="wf-setup-field">' +
                        '<label>Database User <span class="wfe-opt">(optional)</span></label>' +
                        '<input type="text" id="wfDsUser" placeholder="e.g. sa, admin, root" />' +
                    '</div>' +
                    '<div class="wf-setup-field">' +
                        '<label>Database Password <span class="wfe-opt">(optional)</span></label>' +
                        '<input type="password" id="wfDsPassword" placeholder="••••••••" />' +
                    '</div>' +
                '</div>';
            fieldParent.insertAdjacentHTML('afterend', html);
        },

        /* ═══════════════════════════════════════════════════
           Feature 5 — Table / view selector after connection
           ═══════════════════════════════════════════════════ */
        async _loadTableSelector(dsId) {
            try {
                var r = await fetch('/api/datasources/' + dsId + '/tables');
                if (!r.ok) return;
                var tables = await r.json();
                var anchor = document.getElementById('wfDsFieldsPreview');
                if (!anchor) return;

                var existing = document.getElementById('wfeTableSelector');
                if (existing) existing.remove();

                var html = '<div id="wfeTableSelector" class="wfe-table-sel">';
                html += '<label class="wfe-table-label"><i class="bi bi-table me-1"></i>Select Tables & Views</label>';
                html += '<div class="wfe-table-list">';
                tables.forEach(function (t) {
                    var ico = t.type === 'View' ? 'bi-eye' : 'bi-table';
                    html += '<label class="wfe-table-item">';
                    html += '<input type="checkbox" value="' + t.name + '" checked />';
                    html += '<i class="bi ' + ico + '"></i>';
                    html += '<span class="wfe-tbl-name">' + t.name + '</span>';
                    html += '<span class="wfe-tbl-type">' + t.type + '</span>';
                    if (t.rowCount > 0) html += '<span class="wfe-tbl-rows">' + t.rowCount.toLocaleString() + ' rows</span>';
                    html += '</label>';
                });
                html += '</div>';
                html += '<div class="wfe-table-actions">';
                html += '<button class="btn btn-xs btn-outline-secondary" id="wfeSelAll">Select All</button>';
                html += '<button class="btn btn-xs btn-outline-secondary" id="wfeDeselAll">Deselect All</button>';
                html += '</div></div>';

                anchor.insertAdjacentHTML('afterend', html);

                document.getElementById('wfeSelAll')?.addEventListener('click', function () {
                    document.querySelectorAll('#wfeTableSelector input[type="checkbox"]').forEach(function (cb) { cb.checked = true; });
                });
                document.getElementById('wfeDeselAll')?.addEventListener('click', function () {
                    document.querySelectorAll('#wfeTableSelector input[type="checkbox"]').forEach(function (cb) { cb.checked = false; });
                });
            } catch { /* ignore */ }
        },

        _getSelectedTables() {
            return Array.from(document.querySelectorAll('#wfeTableSelector input[type="checkbox"]:checked'))
                .map(function (cb) { return cb.value; });
        }
    };

    window.workspaceEnhanced = WFE;
})();
