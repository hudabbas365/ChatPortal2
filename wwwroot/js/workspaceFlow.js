// WorkspaceFlow — Inline workspace creation & artifact panel manager
// No modals — everything renders inline in left panel + main area
(function () {
    'use strict';

    const WF = {
        _selectedWsId: null,
        _wsData: null,
        _viewMode: 'card',
        _setupStep: 0,

        // ── Initialise ──────────────────────────────────────
        init() {
            this._wireLeftPanel();
            this._wireViewToggle();

            // Auto-select workspace from URL hash (?workspace='GUID) or query param (?workspace=GUID)
            // Also restore agent chat context (?agent=GUID) so a page refresh keeps the user
            // on the same chat instead of bouncing back to the workspace home/landing.
            var wsGuid = null;
            var agentGuid = null;
            var hash = window.location.hash;
            if (hash) {
                var m = hash.match(/[#&]ws=([^&]+)/);
                if (m) wsGuid = decodeURIComponent(m[1]);
                var ma = hash.match(/[#&]agent=([^&]+)/);
                if (ma) agentGuid = decodeURIComponent(ma[1]);
            }
            var params = new URLSearchParams(window.location.search);
            if (!wsGuid) wsGuid = params.get('workspace');
            if (!agentGuid) agentGuid = params.get('agent');

            // Fallback to last-visited chat persisted in localStorage when URL has nothing.
            if (!wsGuid && !agentGuid) {
                try {
                    var last = JSON.parse(localStorage.getItem('cp_last_chat') || 'null');
                    if (last && last.wsGuid) {
                        wsGuid = last.wsGuid;
                        agentGuid = last.agentGuid || null;
                    }
                } catch { /* ignore */ }
            }

            var self = this;
            if (wsGuid) {
                var p = this._selectWorkspace(wsGuid);
                // Capture the selection token AFTER _selectWorkspace's sync
                // prologue ran (it increments the token). If anything bumps
                // the token before the fetch resolves — e.g. the user clicks
                // any workspace, even the same one — we must NOT auto-open
                // the remembered chat, because the newer selection is going
                // to render the home/lineage view and we'd flash chat first.
                var initToken = self._wsSelectionToken;
                if (agentGuid && p && typeof p.then === 'function') {
                    p.then(function () {
                        if (self._wsSelectionToken === initToken &&
                            self._selectedWsId === wsGuid) {
                            self._startChat(wsGuid, agentGuid);
                        }
                    });
                } else if (agentGuid) {
                    this._startChat(wsGuid, agentGuid);
                }
            } else {
                this._showLanding();
            }

            // Handle hash changes while on the page
            window.addEventListener('hashchange', function () {
                var h = window.location.hash;
                var match = h.match(/[#&]ws=([^&]+)/);
                if (match) {
                    self._selectWorkspace(decodeURIComponent(match[1]));
                }
            });
        },

        // ── Left panel: workspace list click + create ───────
        _wireLeftPanel() {
            const list = document.getElementById('workspaceList');
            const addBtn = document.getElementById('addWorkspaceBtn');
            const inlineWrap = document.getElementById('wfInlineCreate');

            if (list) {
                list.addEventListener('click', (e) => {
                    const item = e.target.closest('.panel-list-item');
                    if (!item) return;
                    const wsId = item.dataset.workspaceId;
                    if (wsId && wsId !== '0') this._selectWorkspace(wsId);
                });
            }

            if (addBtn) {
                addBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    if (this._checkTrialLimit()) return;
                    if (inlineWrap) {
                        inlineWrap.classList.add('active');
                        const inp = document.getElementById('wfNewWsName');
                        if (inp) { inp.value = ''; inp.focus(); }
                    }
                });
            }

            const confirmBtn = document.getElementById('wfCreateConfirm');
            const cancelBtn = document.getElementById('wfCreateCancel');
            const nameInput = document.getElementById('wfNewWsName');

            if (confirmBtn) confirmBtn.addEventListener('click', () => this._createWorkspace());
            if (cancelBtn) cancelBtn.addEventListener('click', () => this._hideInlineCreate());
            if (nameInput) {
                nameInput.addEventListener('keydown', (e) => {
                    if (e.key === 'Enter') this._createWorkspace();
                    if (e.key === 'Escape') this._hideInlineCreate();
                });
            }
        },

        _hideInlineCreate() {
            const wrap = document.getElementById('wfInlineCreate');
            if (wrap) wrap.classList.remove('active');
            const alert = document.getElementById('wfInlineAlert');
            if (alert) alert.style.display = 'none';
        },

        _checkTrialLimit() {
            const plan = JSON.parse(localStorage.getItem('cp_plan') || 'null');
            if (!plan) return false;
            const tierVal = plan.tier || plan.plan || '';
            const isTrial = (typeof tierVal === 'string' ? tierVal : String(tierVal)).toLowerCase() === 'trial';
            if (!isTrial) return false;
            const list = document.getElementById('workspaceList');
            const count = list ? list.querySelectorAll('.panel-list-item[data-workspace-id]').length : 0;
            const realCount = Array.from(list ? list.querySelectorAll('.panel-list-item') : [])
                .filter(el => el.dataset.workspaceId && el.dataset.workspaceId !== '0').length;
            if (realCount >= 1) {
                const alert = document.getElementById('wfInlineAlert');
                if (alert) {
                    alert.textContent = 'Trial plan allows 1 workspace. Upgrade for more.';
                    alert.style.display = 'block';
                    const wrap = document.getElementById('wfInlineCreate');
                    if (wrap) wrap.classList.add('active');
                }
                return true;
            }
            return false;
        },

        // ── Create workspace (inline) ───────────────────────
        async _createWorkspace() {
            const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
            const inp = document.getElementById('wfNewWsName');
            const alert = document.getElementById('wfInlineAlert');
            const name = (inp ? inp.value.trim() : '') || 'New Workspace';
            try {
                const r = await fetch('/api/workspaces', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        name,
                        organizationId: user?.organizationId || 0,
                        userId: user?.id || ''
                    })
                });
                if (r.status === 409) {
                    const err = await r.json().catch(() => null);
                    if (alert) {
                        alert.textContent = (err && err.error) || 'A workspace with this name already exists.';
                        alert.style.display = 'block';
                    }
                    return;
                }
                if (!r.ok) {
                    const err = await r.json().catch(() => ({}));
                    if (alert) {
                        alert.textContent = err.error || 'Failed to create workspace.';
                        alert.style.display = 'block';
                    }
                    return;
                }
                const ws = await r.json();
                // Update local user org if server resolved it
                if (ws.organizationId && user) {
                    user.organizationId = ws.organizationId;
                    localStorage.setItem('cp_user', JSON.stringify(user));
                }
                this._addWorkspaceToList(ws);
                this._hideInlineCreate();
                this._selectWorkspace(ws.guid);
            } catch (err) {
                if (alert) {
                    alert.textContent = 'Failed to create workspace.';
                    alert.style.display = 'block';
                }
            }
        },

        _addWorkspaceToList(ws) {
            const list = document.getElementById('workspaceList');
            if (!list) return;
            // Remove "Default Workspace" placeholder
            const placeholder = list.querySelector('[data-workspace-id="0"]');
            if (placeholder) placeholder.remove();
            const item = document.createElement('div');
            item.className = 'panel-list-item';
            item.dataset.workspaceId = ws.guid;
            const iconHtml = ws.logoUrl
                ? `<img src="${this._esc(ws.logoUrl)}" alt="" style="width:18px;height:18px;border-radius:3px;object-fit:cover;margin-right:8px;">`
                : `<i class="bi bi-folder me-2"></i>`;
            item.innerHTML = `${iconHtml}${this._esc(ws.name)}<span class="wf-ws-status unconfigured"></span>`;
            item.title = ws.description || '';
            list.appendChild(item);
        },

        // ── Select workspace → load artifacts ───────────────
        async _selectWorkspace(guid) {
            // Immediately hide chat panel to prevent agent name flash
            const chatWs = document.getElementById('chatWorkspace');
            if (chatWs) chatWs.style.display = 'none';

            // Drop any stale ?agent= from the URL — caller (e.g. _startChat) will
            // re-add it if a chat is opened. Keeps refresh-on-home on home.
            try {
                var u = new URL(window.location.href);
                if (u.searchParams.has('agent')) {
                    u.searchParams.delete('agent');
                    window.history.replaceState({ wsGuid: guid }, '', u.toString());
                }
            } catch { /* ignore */ }

            // Reset wizard state to prevent leaking data between workspaces
            const isSameWorkspace = this._selectedWsId === guid;
            this._resetWizardState(isSameWorkspace);

            // Highlight in left panel
            document.querySelectorAll('#workspaceList .panel-list-item').forEach(el => el.classList.remove('active'));
            const item = document.querySelector(`#workspaceList .panel-list-item[data-workspace-id="${guid}"]`);
            if (item) item.classList.add('active');

            this._selectedWsId = guid;
            // Track the in-flight selection so a slower earlier fetch can't
            // overwrite the UI after the user has already moved on to another
            // workspace (e.g. rapid clicks, or chat-restore racing a click).
            const selectionToken = (this._wsSelectionToken = (this._wsSelectionToken || 0) + 1);
            const topTitle = document.getElementById('chatWorkspaceTitle');
            const subName = document.getElementById('chatSubnavWorkspaceName');

            try {
                const r = await fetch(`/api/workspaces/${guid}`);
                if (!r.ok) throw new Error();
                const data = await r.json();
                if (selectionToken !== this._wsSelectionToken) return; // stale
                this._wsData = data;
                if (topTitle) topTitle.textContent = data.name || 'Workspace';
                if (subName) subName.textContent = data.name || 'Workspace';
                this._updateWorkspaceStatus(guid, data);
                // Load role BEFORE rendering so delete buttons appear for admins
                if (window.workspaceRoles) await window.workspaceRoles.loadMyRole(guid);
                if (selectionToken !== this._wsSelectionToken) return; // stale
                this._renderHome(data);
                if (window.workspaceRoles) window.workspaceRoles._applyRoleUI();
            } catch {
                if (selectionToken !== this._wsSelectionToken) return; // stale
                if (topTitle) topTitle.textContent = 'Workspace';
                this._showLanding();
            }
        },

        // ── Reset wizard state between workspace switches ───
        _resetWizardState(keepAgentContext) {
            // If a wizard run was in-progress and never reached the final step,
            // delete any orphan server-side artifacts that were created during
            // earlier phases (e.g. the datasource created at "Test Connection")
            // so cancelling/abandoning the wizard doesn't leave dangling rows.
            if (this._setupStep > 0 && !this._wizardCompleted &&
                (this._createdDsId || this._createdAgentGuid)) {
                this._cleanupOrphanWizardArtifacts();
            }
            this._wizardCompleted = false;
            this._setupStep = 0;
            this._createdDsId = null;
            this._createdDsGuid = null;
            this._createdDsName = null;
            this._createdDsType = null;
            this._createdAgentGuid = null;
            this._selectedTables = null;
            this._generatedPrompt = null;
            this._pendingAgent = null;
            this._selectedDsType = '';
            // Clear agent/datasource context only when switching to a different workspace
            if (!keepAgentContext) {
                window.currentAgentGuid = null;
                window.currentDatasourceId = null;
                window.currentDatasourceName = null;
                window.currentDatasourceType = null;
            }
        },

        // Fire-and-forget DELETE of any partially-created wizard artifacts.
        _cleanupOrphanWizardArtifacts() {
            try {
                if (this._createdAgentGuid) {
                    fetch('/api/agents/' + encodeURIComponent(this._createdAgentGuid),
                          { method: 'DELETE' }).catch(() => {});
                }
                if (this._createdDsId) {
                    fetch('/api/datasources/' + encodeURIComponent(this._createdDsId),
                          { method: 'DELETE' }).catch(() => {});
                }
            } catch { /* best-effort */ }
        },

        // Cancel the in-progress setup wizard, deleting any partial artifacts
        // and returning to the workspace home view.
        async _cancelWizard(wsData) {
            var ok = await (window.cpConfirm ? window.cpConfirm({
                title: 'Cancel setup',
                message: 'Cancel the AI Insights setup?',
                subMessage: 'Any partially configured datasource or agent will be discarded.',
                confirmText: 'Cancel setup',
                cancelText: 'Keep editing',
                variant: 'warning',
                icon: 'bi-x-octagon-fill'
            }) : Promise.resolve(confirm('Cancel setup? Any partially configured datasource will be discarded.')));
            if (!ok) return;
            this._cleanupOrphanWizardArtifacts();
            this._setupStep = 0;
            this._createdDsId = null;
            this._createdDsGuid = null;
            this._createdDsName = null;
            this._createdDsType = null;
            this._createdAgentGuid = null;
            this._selectedTables = null;
            this._generatedPrompt = null;
            this._wizardCompleted = false;
            // Re-render the workspace home (will show empty-state or existing artifacts).
            if (wsData && wsData.guid) {
                await this._selectWorkspace(wsData.guid);
            }
        },

        // ── Update workspace item status badge in left panel ──
        _updateWorkspaceStatus(guid, data) {
            var item = document.querySelector('#workspaceList .panel-list-item[data-workspace-id="' + guid + '"]');
            if (!item) return;
            var badge = item.querySelector('.wf-ws-status');
            if (!badge) {
                badge = document.createElement('span');
                badge.className = 'wf-ws-status';
                item.appendChild(badge);
            }
            var ds = data.datasources || [];
            var agents = data.agents || [];
            badge.classList.remove('configured', 'unconfigured', 'disconnected');
            if (ds.length > 0 && agents.length > 0) {
                badge.classList.add('configured');
                badge.title = 'Connected';
            } else if (ds.length > 0 || agents.length > 0) {
                badge.classList.add('disconnected');
                badge.title = 'Partially configured';
            } else {
                badge.classList.add('unconfigured');
                badge.title = 'Needs setup';
            }
        },

        // ── Render artifacts home panel ─────────────────────
        _renderHome(data) {
            const panel = document.getElementById('workspaceHomePanel');
            const chatWs = document.getElementById('chatWorkspace');
            if (panel) panel.classList.remove('hidden');
            if (chatWs) chatWs.style.display = 'none';

            const agents = data.agents || [];
            const datasources = data.datasources || [];
            const reports = data.reports || [];
            const hasArtifacts = datasources.length > 0 || agents.length > 0;

            // If workspace has no artifacts yet → show setup wizard
            if (!hasArtifacts) {
                this._renderSetupWizard(panel, data);
                return;
            }

            const viewMode = this._homeViewMode || 'lineage';
            const viewContent = viewMode === 'list'
                ? this._renderListView(agents, datasources, data)
                : this._renderLineageView(agents, datasources, data);

            // Build reports section
            let reportsHtml = '';
            if (reports.length > 0) {
                reportsHtml = `<div class="wf-section-title" style="margin-top:18px">Reports <span style="font-size:0.7rem;font-weight:400;color:var(--cp-text-muted)">${reports.length} saved</span></div><div class="wf-artifacts-grid">`;
                reports.forEach(rpt => {
                    reportsHtml += `
                    <div class="wf-artifact-card" data-action="report-view" data-report-guid="${rpt.guid}" data-ws-id="${data.guid}">
                        <span class="wf-artifact-status ${rpt.status === 'Published' ? 'ok' : 'warn'}"></span>
                        <div class="wf-artifact-card-head">
                            <div class="wf-artifact-icon report" style="background:rgba(25,135,84,0.12);color:#198754"><i class="bi bi-file-earmark-bar-graph"></i></div>
                            <span class="wf-artifact-name">${this._esc(rpt.name)}</span>
                        </div>
                        <div class="wf-artifact-body">${this._esc(rpt.status || 'Draft')} &bull; ${new Date(rpt.createdAt).toLocaleDateString()}</div>
                    </div>`;
                });
                reportsHtml += '</div>';
            }

            panel.innerHTML = `
                <div class="wf-home-header">
                    <div class="wf-home-icon"><i class="bi bi-folder-fill"></i></div>
                    <div class="wf-home-meta">
                        <h2 class="wf-home-title">${this._esc(data.name)}</h2>
                        <p class="wf-home-desc">${this._esc(data.description || 'Your workspace artifacts')}</p>
                    </div>
                    <button class="btn btn-sm cp-btn-gradient" id="newArtifactInsightsBtn" style="flex-shrink:0">
                        <i class="bi bi-plus-lg me-1"></i>New AI Insights
                    </button>
                </div>
                <div class="wf-section-title">
                    Navigation
                    <span style="font-size:0.7rem;font-weight:400;color:var(--cp-text-muted)">${datasources.length + agents.length + reports.length + 1} items</span>
                    <div class="wf-view-switch">
                        <button class="wf-view-switch-btn ${viewMode === 'lineage' ? 'active' : ''}" data-home-view="lineage"><i class="bi bi-diagram-3 me-1"></i>Lineage</button>
                        <button class="wf-view-switch-btn ${viewMode === 'list' ? 'active' : ''}" data-home-view="list"><i class="bi bi-list-ul me-1"></i>List</button>
                    </div>
                </div>
                <div class="wf-lineage-container" id="wfLineageContainer">
                    ${viewContent}
                </div>
                ${reportsHtml}
            `;
            this._wireHomeViewSwitch(data);
            this._wireArtifactActions();
            this._wireReportActions();
            this._wireNewArtifactDropdown(data);
            this._showNewArtifactMenu(agents.length > 0);
            if (window.workspaceRoles) window.workspaceRoles._applyRoleUI();
        },

        _renderCardsView(agents, datasources, wsData) {
            const reports = wsData.reports || [];
            let html = '<div class="wf-artifacts-grid">';

            // Agent cards
            agents.forEach(a => {
                const ds = a.datasourceId ? datasources.find(d => d.id === a.datasourceId) : null;
                const bound = !!ds;
                html += `
                <div class="wf-artifact-card" data-action="agent-chat" data-agent-id="${a.guid}" data-ws-id="${wsData.guid}">
                    <span class="wf-artifact-status ${bound ? 'ok' : 'warn'}"></span>
                    <div class="wf-artifact-card-head">
                        <div class="wf-artifact-icon agent"><i class="bi bi-robot"></i></div>
                        <span class="wf-artifact-name">${this._esc(a.name)}</span>
                    </div>
                    <div class="wf-artifact-body">${this._esc(a.systemPrompt || 'AI Agent')}</div>
                    <div class="wf-binding-row">
                        ${bound
                        ? `<span class="wf-binding-badge connected"><i class="bi bi-link-45deg"></i>${this._esc(ds.name)}</span>`
                        : `<span class="wf-binding-badge unbound"><i class="bi bi-exclamation-circle"></i>No datasource</span>`}
                    </div>
                </div>`;
            });

            // Datasource cards
            datasources.forEach(d => {
                const boundAgents = agents.filter(a => a.datasourceId === d.id);
                html += `
                <div class="wf-artifact-card" data-action="ds-detail" data-ds-id="${d.guid}">
                    <span class="wf-artifact-status ok"></span>
                    <div class="wf-artifact-card-head">
                        <div class="wf-artifact-icon datasource"><i class="bi bi-database"></i></div>
                        <span class="wf-artifact-name">${this._esc(d.name)}</span>
                    </div>
                    <div class="wf-artifact-body">${this._esc(d.type || 'Datasource')} ${d.connectionString ? '• Connected' : ''}</div>
                    <div class="wf-binding-row">
                        ${boundAgents.length
                        ? boundAgents.map(a => `<span class="wf-binding-badge connected"><i class="bi bi-robot"></i>${this._esc(a.name)}</span>`).join('')
                        : `<span class="wf-binding-badge unbound"><i class="bi bi-dash-circle"></i>Not bound</span>`}
                    </div>
                </div>`;
            });

            // Dashboard card (always present)
            html += `
            <div class="wf-artifact-card" data-action="dashboard" data-ws-id="${wsData.guid}">
                <span class="wf-artifact-status ok"></span>
                <div class="wf-artifact-card-head">
                    <div class="wf-artifact-icon dashboard"><i class="bi bi-bar-chart-fill"></i></div>
                    <span class="wf-artifact-name">Dashboard</span>
                </div>
                <div class="wf-artifact-body">Visual report designer for this workspace</div>
                <div class="wf-binding-row">
                    <span class="wf-binding-badge connected"><i class="bi bi-easel"></i>Open designer</span>
                </div>
            </div>`;

            // Report cards
            reports.forEach(rpt => {
                html += `
                <div class="wf-artifact-card" data-action="report-view" data-report-guid="${rpt.guid}" data-ws-id="${wsData.guid}">
                    <span class="wf-artifact-status ${rpt.status === 'Published' ? 'ok' : 'warn'}"></span>
                    <div class="wf-artifact-card-head">
                        <div class="wf-artifact-icon report" style="background:rgba(25,135,84,0.12);color:#198754"><i class="bi bi-file-earmark-bar-graph"></i></div>
                        <span class="wf-artifact-name">${this._esc(rpt.name)}</span>
                    </div>
                    <div class="wf-artifact-body">${this._esc(rpt.status || 'Draft')} &bull; ${new Date(rpt.createdAt).toLocaleDateString()}</div>
                </div>`;
            });

            html += '</div>';
            return html;
        },

        _renderListView(agents, datasources, wsData) {
            const reports = wsData.reports || [];
            let html = '<div class="wf-artifacts-list">';

            agents.forEach(a => {
                const ds = a.datasourceId ? datasources.find(d => d.id === a.datasourceId) : null;
                html += `
                <div class="wf-artifact-list-item" data-action="agent-chat" data-agent-id="${a.guid}" data-ws-id="${wsData.guid}">
                    <div class="wf-artifact-icon agent"><i class="bi bi-robot"></i></div>
                    <div class="wf-artifact-list-info">
                        <div class="wf-artifact-list-name">${this._esc(a.name)}</div>
                        <div class="wf-artifact-list-meta">${ds ? 'Bound to ' + this._esc(ds.name) : 'No datasource bound'}</div>
                    </div>
                    <div class="wf-binding-row">
                        ${ds
                        ? `<span class="wf-binding-badge connected"><i class="bi bi-link-45deg"></i></span>`
                        : `<span class="wf-binding-badge unbound"><i class="bi bi-exclamation-circle"></i></span>`}
                    </div>
                </div>`;
            });

            datasources.forEach(d => {
                html += `
                <div class="wf-artifact-list-item" data-action="ds-detail" data-ds-id="${d.guid}">
                    <div class="wf-artifact-icon datasource"><i class="bi bi-database"></i></div>
                    <div class="wf-artifact-list-info">
                        <div class="wf-artifact-list-name">${this._esc(d.name)}</div>
                        <div class="wf-artifact-list-meta">${this._esc(d.type || 'Datasource')}</div>
                    </div>
                </div>`;
            });

            html += `
            <div class="wf-artifact-list-item" data-action="dashboard" data-ws-id="${wsData.guid}">
                <div class="wf-artifact-icon dashboard"><i class="bi bi-bar-chart-fill"></i></div>
                <div class="wf-artifact-list-info">
                    <div class="wf-artifact-list-name">Dashboard</div>
                    <div class="wf-artifact-list-meta">Visual report designer</div>
                </div>
            </div>`;

            reports.forEach(rpt => {
                html += `
                <div class="wf-artifact-list-item" data-action="report-view" data-report-guid="${rpt.guid}" data-ws-id="${wsData.guid}">
                    <div class="wf-artifact-icon report" style="background:rgba(25,135,84,0.12);color:#198754"><i class="bi bi-file-earmark-bar-graph"></i></div>
                    <div class="wf-artifact-list-info">
                        <div class="wf-artifact-list-name">${this._esc(rpt.name)}</div>
                        <div class="wf-artifact-list-meta">${this._esc(rpt.status || 'Draft')} &bull; ${new Date(rpt.createdAt).toLocaleDateString()}</div>
                    </div>
                </div>`;
            });

            html += '</div>';
            return html;
        },

        // ── View toggle wiring ──────────────────────────────
        _wireViewToggle() {
            const toggle = document.getElementById('wfViewToggle');
            if (!toggle) return;
            toggle.querySelectorAll('.wf-view-btn').forEach(btn => {
                btn.addEventListener('click', () => {
                    this._viewMode = btn.dataset.view;
                    if (this._wsData) this._renderHome(this._wsData);
                    if (window.workspaceRoles) window.workspaceRoles._applyRoleUI();
                });
            });
        },

        // ── Wire AI Insights button in home panel ─────────
        _wireNewArtifactDropdown(data) {
            const btn = document.getElementById('newArtifactInsightsBtn');
            if (!btn) return;
            btn.addEventListener('click', () => {
                const panel = document.getElementById('workspaceHomePanel');
                if (panel) this._renderSetupWizard(panel, data);
            });
        },

        // ── Wire artifact card actions ──────────────────────
        _wireArtifactActions() {
            document.querySelectorAll('[data-action="agent-chat"]').forEach(el => {
                el.addEventListener('click', () => {
                    const wsGuid = el.dataset.wsId;
                    const agentGuid = el.dataset.agentId;
                    this._startChat(wsGuid, agentGuid);
                });
            });
            document.querySelectorAll('[data-action="dashboard"]').forEach(el => {
                el.addEventListener('click', () => {
                    const wsId = el.dataset.wsId || this._selectedWsId || '';
                    let url = '/dashboard?workspace=' + encodeURIComponent(wsId);
                    // Prefer agent from the clicked element (lineage), then current context, then first agent
                    const agentGuid = el.dataset.agentId
                        || window.currentAgentGuid
                        || ((this._wsData?.agents || [])[0]?.guid);
                    if (agentGuid) url += '&agent=' + encodeURIComponent(agentGuid);
                    window.location.href = url;
                });
            });
            document.querySelectorAll('[data-action="ds-detail"]').forEach(el => {
                el.addEventListener('click', () => {
                    const dsGuid = el.dataset.dsId;
                    if (dsGuid) this._showDatasourceDetailPopup(dsGuid);
                });
            });
        },

        _showDatasourceDetailPopup(dsGuid) {
            const ds = (this._wsData?.datasources || []).find(d => d.guid === dsGuid);
            if (!ds) return;
            // Remove any existing modal
            let modal = document.getElementById('dsDetailModal');
            if (modal) modal.remove();
            const esc = this._esc;
            modal = document.createElement('div');
            modal.id = 'dsDetailModal';
            modal.className = 'modal fade';
            modal.tabIndex = -1;
            const isRest = /rest\s*api/i.test(ds.type || '');
            const isPbi = /power\s*bi/i.test(ds.type || '');
            modal.innerHTML = `
                <div class="modal-dialog modal-dialog-centered">
                    <div class="modal-content" style="background:white;color:var(--cp-text)">
                        <div class="modal-header border-bottom" style="border-color:var(--cp-border)!important">
                            <h5 class="modal-title"><i class="bi bi-database me-2"></i>${esc(ds.name)}</h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body">
                            <div class="mb-3">
                                <label class="form-label fw-bold" style="font-size:0.8rem">Type</label>
                                <input type="text" class="form-control form-control-sm" readonly value="${esc(ds.type || 'Unknown')}" />
                            </div>
                            ${isRest ? `
                            <div class="mb-3">
                                <label class="form-label fw-bold" style="font-size:0.8rem">API URL</label>
                                <input type="text" class="form-control form-control-sm" readonly value="${esc(ds.apiUrl || '')}" style="font-family:monospace;font-size:0.78rem" />
                            </div>
                            <div class="mb-3">
                                <label class="form-label fw-bold" style="font-size:0.8rem">API Key</label>
                                <input type="text" class="form-control form-control-sm" readonly value="${esc(ds.apiKey || '(not set)')}" />
                            </div>
                            <div class="mb-3">
                                <label class="form-label fw-bold" style="font-size:0.8rem">HTTP Method</label>
                                <input type="text" class="form-control form-control-sm" readonly value="${esc(ds.apiMethod || 'GET')}" />
                            </div>
                            ` : isPbi ? `
                            <div class="mb-3">
                                <label class="form-label fw-bold" style="font-size:0.8rem">XMLA Endpoint</label>
                                <input type="text" class="form-control form-control-sm" id="dsPbiXmla" placeholder="powerbi://api.powerbi.com/v1.0/myorg/<workspace>" value="" style="font-family:monospace;font-size:0.78rem" />
                                <div class="form-text" style="font-size:0.7rem">Leave blank to keep the current value.</div>
                            </div>
                            <div class="mb-3">
                                <label class="form-label fw-bold" style="font-size:0.8rem">Semantic Model (Catalog)</label>
                                <input type="text" class="form-control form-control-sm" id="dsPbiCatalog" placeholder="Dataset / semantic model name" value="" />
                                <div class="form-text" style="font-size:0.7rem">Leave blank to keep the current value.</div>
                            </div>
                            <div class="mb-3">
                                <label class="form-label fw-bold" style="font-size:0.8rem">Tenant ID</label>
                                <input type="text" class="form-control form-control-sm" id="dsPbiTenant" placeholder="••••••" value="" autocomplete="off" />
                                <div class="form-text" style="font-size:0.7rem">Leave blank to keep the current value.</div>
                            </div>
                            <div class="mb-3">
                                <label class="form-label fw-bold" style="font-size:0.8rem">Client ID</label>
                                <input type="text" class="form-control form-control-sm" id="dsPbiClientId" placeholder="••••••" value="" autocomplete="off" />
                                <div class="form-text" style="font-size:0.7rem">Leave blank to keep the current value.</div>
                            </div>
                            <div class="mb-3">
                                <label class="form-label fw-bold" style="font-size:0.8rem">Client Secret</label>
                                <input type="password" class="form-control form-control-sm" id="dsPbiClientSecret" placeholder="••••••" value="" autocomplete="new-password" />
                                <div class="form-text" style="font-size:0.7rem">Leave blank to keep the current value.</div>
                            </div>
                            <div id="dsPbiSaveStatus" class="small" style="min-height:1rem"></div>
                            ` : `
                            <div class="mb-3">
                                <label class="form-label fw-bold" style="font-size:0.8rem">Connection String</label>
                                <textarea class="form-control form-control-sm" readonly rows="3" style="resize:none;font-family:monospace;font-size:0.78rem">${esc(ds.connectionString || '')}</textarea>
                            </div>
                            `}
                        </div>
                        <div class="modal-footer border-top d-flex justify-content-between" style="border-color:var(--cp-border)!important">
                            <button type="button" class="btn btn-sm btn-outline-primary" id="dsRefreshCacheBtn" title="Flush cached query results for this datasource so the next request hits the live database.">
                                <i class="bi bi-arrow-clockwise me-1"></i>Refresh Cache
                            </button>
                            <div class="d-flex gap-2">
                                ${isPbi ? `<button type="button" class="btn btn-sm btn-primary" id="dsPbiSaveBtn"><i class="bi bi-save me-1"></i>Save changes</button>` : ``}
                                <button type="button" class="btn btn-sm btn-secondary" data-bs-dismiss="modal">Close</button>
                            </div>
                        </div>
                    </div>
                </div>`;
            document.body.appendChild(modal);
            const bsModal = new bootstrap.Modal(modal);
            modal.addEventListener('hidden.bs.modal', () => { modal.remove(); });
            const refreshBtn = modal.querySelector('#dsRefreshCacheBtn');
            if (refreshBtn) {
                refreshBtn.addEventListener('click', async () => {
                    const original = refreshBtn.innerHTML;
                    refreshBtn.disabled = true;
                    refreshBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Refreshing...';
                    try {
                        const resp = await fetch(`/api/datasources/${encodeURIComponent(dsGuid)}/refresh-cache`, {
                            method: 'POST',
                            credentials: 'same-origin'
                        });
                        if (resp.ok) {
                            refreshBtn.classList.remove('btn-outline-primary');
                            refreshBtn.classList.add('btn-success');
                            refreshBtn.innerHTML = '<i class="bi bi-check2 me-1"></i>Cache cleared';
                        } else {
                            refreshBtn.classList.remove('btn-outline-primary');
                            refreshBtn.classList.add('btn-danger');
                            refreshBtn.innerHTML = '<i class="bi bi-x-circle me-1"></i>Failed';
                        }
                    } catch {
                        refreshBtn.classList.remove('btn-outline-primary');
                        refreshBtn.classList.add('btn-danger');
                        refreshBtn.innerHTML = '<i class="bi bi-x-circle me-1"></i>Failed';
                    } finally {
                        setTimeout(() => {
                            refreshBtn.disabled = false;
                            refreshBtn.classList.remove('btn-success', 'btn-danger');
                            refreshBtn.classList.add('btn-outline-primary');
                            refreshBtn.innerHTML = original;
                        }, 1800);
                    }
                });
            }
            const pbiSaveBtn = modal.querySelector('#dsPbiSaveBtn');
            if (pbiSaveBtn) {
                pbiSaveBtn.addEventListener('click', async () => {
                    const status = modal.querySelector('#dsPbiSaveStatus');
                    const xmla = (modal.querySelector('#dsPbiXmla')?.value || '').trim();
                    const catalog = (modal.querySelector('#dsPbiCatalog')?.value || '').trim();
                    const tenant = (modal.querySelector('#dsPbiTenant')?.value || '').trim();
                    const clientId = (modal.querySelector('#dsPbiClientId')?.value || '').trim();
                    const clientSecret = (modal.querySelector('#dsPbiClientSecret')?.value || '').trim();
                    const body = {};
                    if (xmla) body.xmlaEndpoint = xmla;
                    if (catalog) body.connectionString = catalog;
                    if (tenant) body.microsoftAccountTenantId = tenant;
                    if (clientId) body.dbUser = clientId;
                    if (clientSecret) body.dbPassword = clientSecret;
                    if (Object.keys(body).length === 0) {
                        if (status) status.innerHTML = '<span class="text-muted"><i class="bi bi-info-circle me-1"></i>Nothing to update — all fields are blank.</span>';
                        return;
                    }
                    const original = pbiSaveBtn.innerHTML;
                    pbiSaveBtn.disabled = true;
                    pbiSaveBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Saving...';
                    if (status) status.innerHTML = '';
                    try {
                        const resp = await fetch('/api/datasources/' + encodeURIComponent(dsGuid), {
                            method: 'PUT',
                            headers: { 'Content-Type': 'application/json' },
                            credentials: 'same-origin',
                            body: JSON.stringify(body)
                        });
                        if (resp.ok) {
                            if (status) status.innerHTML = '<span class="text-success"><i class="bi bi-check-circle me-1"></i>Saved.</span>';
                            // Clear secret fields after a successful save
                            ['#dsPbiXmla','#dsPbiCatalog','#dsPbiTenant','#dsPbiClientId','#dsPbiClientSecret'].forEach(sel => {
                                const el = modal.querySelector(sel); if (el) el.value = '';
                            });
                        } else {
                            let msg = 'Save failed.';
                            try { const j = await resp.json(); if (j && j.error) msg = j.error; } catch {}
                            if (status) status.innerHTML = '<span class="text-danger"><i class="bi bi-x-circle me-1"></i>' + this._esc(msg) + '</span>';
                        }
                    } catch (e) {
                        if (status) status.innerHTML = '<span class="text-danger"><i class="bi bi-x-circle me-1"></i>' + this._esc(e?.message || 'Save failed.') + '</span>';
                    } finally {
                        pbiSaveBtn.disabled = false;
                        pbiSaveBtn.innerHTML = original;
                    }
                });
            }
            bsModal.show();
        },

        _wireReportActions() {
            document.querySelectorAll('[data-action="report-view"]').forEach(el => {
                el.addEventListener('click', () => {
                    const guid = el.dataset.reportGuid;
                    if (guid) {
                        window.location.href = '/report/view/' + encodeURIComponent(guid);
                    }
                });
            });
        },

        // ── Setup wizard (new workspace, no datasource yet) ─
        _renderSetupWizard(panel, wsData) {
            this._setupStep = 1;
            panel.innerHTML = `
                <div class="wf-home-header">
                    <div class="wf-home-icon"><i class="bi bi-magic"></i></div>
                    <div class="wf-home-meta">
                        <h2 class="wf-home-title">Set up ${this._esc(wsData.name)}</h2>
                        <p class="wf-home-desc">Connect a datasource, select tables, and create an AI agent</p>
                    </div>
                </div>
                <div class="wf-steps" id="wfSteps">
                    <div class="wf-step active" data-step="1">
                        <span class="wf-step-num">1</span>Datasource
                    </div>
                    <div class="wf-step-line"></div>
                    <div class="wf-step" data-step="2">
                        <span class="wf-step-num">2</span>Tables
                    </div>
                    <div class="wf-step-line"></div>
                    <div class="wf-step" data-step="3">
                        <span class="wf-step-num">3</span>AI Prompt
                    </div>
                    <div class="wf-step-line"></div>
                    <div class="wf-step" data-step="4">
                        <span class="wf-step-num">4</span>Agent
                    </div>
                </div>
                <div id="wfSetupContent"></div>
            `;
            this._renderDatasourceConnectionStep(wsData);
        },

        // ── Step 1: Agent setup ─────────────────────────────
        _renderAgentSetup(wsData) {
            const content = document.getElementById('wfSetupContent');
            if (!content) return;
            content.innerHTML = `
                <div class="wf-setup-section">
                    <div class="wf-setup-section-head">
                        <div class="wf-setup-section-icon" style="background:var(--cp-purple-light);color:var(--cp-purple)"><i class="bi bi-robot"></i></div>
                        <div>
                            <div class="wf-setup-section-title">Create Agent</div>
                            <div class="wf-setup-section-desc">Give your AI agent a name, icon, and description</div>
                        </div>
                    </div>
                    <div class="wf-setup-alert" id="wfAgentAlert"></div>
                    <div class="wf-setup-field">
                        <label>Agent Name</label>
                        <input type="text" id="wfAgentName" placeholder="e.g. Sales Assistant" />
                    </div>
                    <div class="wf-setup-field">
                        <label>Icon (Bootstrap Icon class)</label>
                        <input type="text" id="wfAgentIcon" placeholder="bi-robot" value="bi-robot" />
                    </div>
                    <div class="wf-setup-field">
                        <label>Description / System Prompt</label>
                        <textarea id="wfAgentPrompt" rows="3" placeholder="You are a helpful data assistant..."></textarea>
                    </div>
                    <div class="wf-setup-actions">
                        <button class="btn cp-btn-gradient btn-sm" id="wfAgentSaveBtn">
                            <i class="bi bi-check-lg me-1"></i>Save & Continue
                        </button>
                    </div>
                </div>
            `;
            document.getElementById('wfAgentSaveBtn').addEventListener('click', () => this._saveAgent(wsData));
        },

        async _saveAgent(wsData) {
            const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
            const name = document.getElementById('wfAgentName')?.value.trim();
            const prompt = document.getElementById('wfAgentPrompt')?.value.trim();
            const alertEl = document.getElementById('wfAgentAlert');

            if (!name) {
                if (alertEl) { alertEl.className = 'wf-setup-alert error'; alertEl.textContent = 'Please enter an agent name.'; }
                return;
            }
            try {
                const r = await fetch('/api/agents', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        name,
                        systemPrompt: prompt || '',
                        datasourceId: null,
                        workspaceId: wsData.id,
                        organizationId: user?.organizationId || 0,
                        userId: user?.id || ''
                    })
                });
                if (!r.ok) throw new Error();
                const agent = await r.json();

                // Update step indicator
                this._setupStep = 2;
                this._updateSteps();

                // Store agent for binding in step 2
                this._pendingAgent = agent;
                this._renderDatasourceSetup(wsData, agent);
            } catch {
                if (alertEl) { alertEl.className = 'wf-setup-alert error'; alertEl.textContent = 'Failed to create agent.'; }
            }
        },

        // ── Step 2: Datasource setup ────────────────────────
        _renderDatasourceSetup(wsData, agent) {
            const content = document.getElementById('wfSetupContent');
            if (!content) return;
            content.innerHTML = `
                <div class="wf-setup-section">
                    <div class="wf-setup-section-head">
                        <div class="wf-setup-section-icon" style="background:var(--cp-primary-light);color:var(--cp-primary)"><i class="bi bi-database"></i></div>
                        <div>
                            <div class="wf-setup-section-title">Connect Datasource</div>
                            <div class="wf-setup-section-desc">Bind a datasource to <strong>${this._esc(agent.name)}</strong></div>
                        </div>
                    </div>
                    <div class="wf-setup-alert" id="wfDsAlert"></div>
                    <div id="wfDsTypeSelector">
                        <div class="wf-setup-field">
                            <label>Search datasource type</label>
                            <input type="text" id="wfDsTypeSearch" placeholder="Search..." />
                        </div>
                        <div class="wf-ds-type-grid" id="wfDsTypeGrid">
                            <div style="color:var(--cp-text-muted);font-size:0.8rem;">Loading types...</div>
                        </div>
                    </div>
                    <div id="wfDsConfigForm" style="display:none">
                        <div style="margin-bottom:10px">
                            <button class="btn btn-sm btn-outline-secondary" id="wfDsBackBtn"><i class="bi bi-arrow-left me-1"></i>Back</button>
                            <span class="ms-2 fw-bold" id="wfDsSelectedType"></span>
                        </div>
                        <div class="wf-setup-field">
                            <label>Datasource Name</label>
                            <input type="text" id="wfDsName" placeholder="e.g. Sales DB" />
                        </div>
                        <div class="wf-setup-field">
                            <label>Connection String / URL</label>
                            <input type="text" id="wfDsConnStr" placeholder="Server=...;Database=..." />
                        </div>
                        <div id="wfDsPbiFields" style="display:none">
                            <div class="wf-setup-field">
                                <label>XMLA Endpoint</label>
                                <input type="text" id="wfDsXmlaEndpoint" placeholder="powerbi://api.powerbi.com/v1.0/myorg/WorkspaceName" />
                            </div>
                            <div class="wf-setup-field">
                                <label>Semantic Model (Catalog)</label>
                                <input type="text" id="wfDsCatalog" placeholder="e.g. SalesModel" />
                            </div>
                            <div class="wf-setup-field">
                                <label>Azure AD Tenant ID</label>
                                <input type="text" id="wfDsTenantId" placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" />
                            </div>
                            <div class="wf-setup-field">
                                <label>Client ID (App Registration)</label>
                                <input type="text" id="wfDsClientId" placeholder="App (client) ID" />
                            </div>
                            <div class="wf-setup-field">
                                <label>Client Secret</label>
                                <input type="password" id="wfDsClientSecret" placeholder="••••••••" />
                            </div>
                        </div>
                        <div id="wfDsRestApiFields" style="display:none">
                            <div class="wf-setup-field">
                                <label>API URL</label>
                                <input type="text" id="wfDsApiUrl" placeholder="https://api.example.com/data" />
                            </div>
                            <div class="wf-setup-field">
                                <label>API Key <span class="wfe-opt">(optional)</span></label>
                                <input type="password" id="wfDsApiKey" placeholder="Bearer token or API key" />
                            </div>
                        </div>
                        <div id="wfDsFieldsPreview" style="display:none">
                            <label style="font-size:0.75rem;font-weight:600;margin-bottom:4px;display:block;">Available Fields</label>
                            <div class="wf-fields-preview" id="wfDsFieldsList"></div>
                        </div>
                        <div class="wf-setup-actions">
                            <button class="btn btn-outline-primary btn-sm" id="wfDsTestBtn">
                                <i class="bi bi-wifi me-1"></i>Test & Load Fields
                            </button>
                            <button class="btn cp-btn-gradient btn-sm" id="wfDsSaveBtn" style="display:none">
                                <i class="bi bi-check-lg me-1"></i>Connect & Finish
                            </button>
                        </div>
                    </div>
                </div>
            `;

            this._loadDsTypes();
            this._wireDsSetup(wsData, agent);
        },

        async _loadDsTypes() {
            try {
                const r = await fetch('/api/datasources/types');
                const types = await r.json();
                const grid = document.getElementById('wfDsTypeGrid');
                if (!grid) return;
                grid.innerHTML = types.map(t =>
                    `<button class="wf-ds-type-btn" data-type="${t}">${t}</button>`
                ).join('');
            } catch {
                const grid = document.getElementById('wfDsTypeGrid');
                if (grid) grid.innerHTML = '<div style="color:var(--cp-danger);font-size:0.8rem;">Failed to load types</div>';
            }
        },

        _selectedDsType: '',

        _wireDsSetup(wsData, agent) {
            const grid = document.getElementById('wfDsTypeGrid');
            const search = document.getElementById('wfDsTypeSearch');
            const backBtn = document.getElementById('wfDsBackBtn');
            const testBtn = document.getElementById('wfDsTestBtn');
            const saveBtn = document.getElementById('wfDsSaveBtn');

            if (grid) {
                grid.addEventListener('click', (e) => {
                    const btn = e.target.closest('.wf-ds-type-btn');
                    if (!btn) return;
                    this._selectedDsType = btn.dataset.type;
                    document.getElementById('wfDsSelectedType').textContent = this._selectedDsType;
                    document.getElementById('wfDsTypeSelector').style.display = 'none';
                    document.getElementById('wfDsConfigForm').style.display = 'block';

                    // Toggle Power BI vs REST API vs standard fields
                    const isPbi = /power\s*bi/i.test(this._selectedDsType);
                    const isRestApi = /rest\s*api/i.test(this._selectedDsType);
                    const connStrField = document.getElementById('wfDsConnStr')?.closest('.wf-setup-field');
                    const pbiFields = document.getElementById('wfDsPbiFields');
                    const restApiFields = document.getElementById('wfDsRestApiFields');
                    if (connStrField) connStrField.style.display = (isPbi || isRestApi) ? 'none' : '';
                    if (pbiFields) pbiFields.style.display = isPbi ? '' : 'none';
                    if (restApiFields) restApiFields.style.display = isRestApi ? '' : 'none';
                });
            }
            if (search) {
                search.addEventListener('input', function () {
                    const q = this.value.toLowerCase();
                    document.querySelectorAll('#wfDsTypeGrid .wf-ds-type-btn').forEach(b => {
                        b.style.display = b.textContent.toLowerCase().includes(q) ? '' : 'none';
                    });
                });
            }
            if (backBtn) {
                backBtn.addEventListener('click', () => {
                    document.getElementById('wfDsTypeSelector').style.display = '';
                    document.getElementById('wfDsConfigForm').style.display = 'none';
                });
            }
            if (testBtn) {
                testBtn.addEventListener('click', () => this._testDatasource(agent));
            }
            if (saveBtn) {
                saveBtn.addEventListener('click', () => this._finishSetup(wsData));
            }
        },

        _createdDsId: null,

        async _testDatasource(agent) {
            const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
            const name = document.getElementById('wfDsName')?.value.trim() || this._selectedDsType + ' DS';
            const alertEl = document.getElementById('wfDsAlert');
            const testBtn = document.getElementById('wfDsTestBtn');
            const isPbi = /power\s*bi/i.test(this._selectedDsType);
            const isRestApi = /rest\s*api/i.test(this._selectedDsType);

            // Build payload based on datasource type
            const payload = {
                name,
                type: this._selectedDsType,
                organizationId: user?.organizationId || 0,
                userId: user?.id || ''
            };

            if (isRestApi) {
                payload.apiUrl = document.getElementById('wfDsApiUrl')?.value.trim() || '';
                payload.apiKey = document.getElementById('wfDsApiKey')?.value.trim() || '';
            } else if (isPbi) {
                payload.xmlaEndpoint = document.getElementById('wfDsXmlaEndpoint')?.value.trim() || '';
                payload.connectionString = document.getElementById('wfDsCatalog')?.value.trim() || '';
                payload.microsoftAccountTenantId = document.getElementById('wfDsTenantId')?.value.trim() || '';
                payload.dbUser = document.getElementById('wfDsClientId')?.value.trim() || '';
                payload.dbPassword = document.getElementById('wfDsClientSecret')?.value.trim() || '';
            } else {
                payload.connectionString = document.getElementById('wfDsConnStr')?.value.trim() || '';
            }

            // Disable button & show testing state
            if (testBtn) { testBtn.disabled = true; testBtn.innerHTML = '<i class="bi bi-hourglass-split me-1"></i>Testing...'; }
            if (alertEl) { alertEl.className = 'wf-setup-alert'; alertEl.textContent = 'Testing connection...'; }

            try {
                // Step 1: Test the connection first
                const testR = await fetch('/api/datasources/test-connection', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });
                const testResult = await testR.json();
                if (!testR.ok || !testResult.connected) {
                    if (alertEl) { alertEl.className = 'wf-setup-alert error'; alertEl.textContent = testResult.error || 'Connection failed. Please check your credentials and try again.'; }
                    return;
                }

                // Step 2: Connection verified — create the datasource
                const r = await fetch('/api/datasources', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });
                if (!r.ok) throw new Error();
                const ds = await r.json();
                this._createdDsId = ds.id;

                const fr = await fetch(`/api/datasources/${ds.id}/fields`);
                const fields = await fr.json();
                const fieldsDiv = document.getElementById('wfDsFieldsList');
                if (fieldsDiv) {
                    fieldsDiv.innerHTML = fields.map(f => `<span class="badge bg-secondary">${f}</span>`).join('');
                }
                document.getElementById('wfDsFieldsPreview').style.display = 'block';
                document.getElementById('wfDsSaveBtn').style.display = '';

                if (alertEl) { alertEl.className = 'wf-setup-alert success'; alertEl.textContent = `Connected. ${fields.length} fields loaded.`; }
            } catch {
                if (alertEl) { alertEl.className = 'wf-setup-alert error'; alertEl.textContent = 'Connection test failed. Please verify your connection string.'; }
            } finally {
                if (testBtn) { testBtn.disabled = false; testBtn.innerHTML = '<i class="bi bi-wifi me-1"></i>Test & Load Fields'; }
            }
        },

        async _finishSetup(wsData) {
            // Bind datasource to agent
            if (this._pendingAgent && this._createdDsId) {
                try {
                    await fetch(`/api/agents/${this._pendingAgent.guid}`, {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ datasourceId: this._createdDsId })
                    });
                } catch (e) {
                    console.error('Failed to bind datasource to agent:', e);
                }
            }

            this._setupStep = 3;
            this._updateSteps();

            // Reload workspace data
            await this._selectWorkspace(wsData.guid);
        },

        _updateSteps() {
            const steps = document.querySelectorAll('#wfSteps .wf-step');
            steps.forEach(s => {
                const n = parseInt(s.dataset.step);
                s.classList.remove('active', 'done');
                if (n < this._setupStep) s.classList.add('done');
                else if (n === this._setupStep) s.classList.add('active');
            });
        },

        // ── Start chat (agent click) ────────────────────────
        _startChat(wsGuid, agentGuid) {
            const panel = document.getElementById('workspaceHomePanel');
            const chatWs = document.getElementById('chatWorkspace');
            if (panel) panel.classList.add('hidden');
            if (chatWs) chatWs.style.display = '';

            // Persist chat context in URL so a page refresh restores the same agent chat
            // instead of dropping the user back at the workspace home/landing.
            try {
                var url = new URL(window.location.href);
                url.searchParams.set('workspace', wsGuid);
                url.searchParams.set('agent', agentGuid);
                window.history.replaceState({ wsGuid: wsGuid, agentGuid: agentGuid }, '', url.toString());
                localStorage.setItem('cp_last_chat', JSON.stringify({ wsGuid: wsGuid, agentGuid: agentGuid }));
            } catch { /* best-effort */ }

            // Set the chat module's workspace context
            window.currentWorkspaceGuid = wsGuid;
            window.currentAgentGuid = agentGuid;

            // Resolve datasource from workspace data for the selected agent
            window.currentDatasourceId = null;
            window.currentDatasourceName = null;
            window.currentDatasourceType = null;
            let agentName = '';
            if (this._wsData) {
                const agents = this._wsData.agents || [];
                const agent = agents.find(a => a.guid === agentGuid);
                if (agent) {
                    agentName = agent.name || '';
                    if (agent.datasourceId) {
                        window.currentDatasourceId = agent.datasourceId;
                        window.currentDatasourceName = agent.datasourceName || null;
                        window.currentDatasourceType = agent.datasourceType || null;
                    }
                }
            }

            // Update subnav datasource badge
            const dsBadge = document.getElementById('chatSubnavDs');
            const dsNameEl = document.getElementById('chatSubnavDsName');
            if (dsBadge && dsNameEl) {
                if (window.currentDatasourceName) {
                    dsNameEl.textContent = window.currentDatasourceName;
                    dsBadge.style.display = '';
                } else {
                    dsBadge.style.display = 'none';
                }
            }

            // Load schema explorer if datasource is connected
            if (window.currentDatasourceId && typeof this._loadSchemaExplorer === 'function') {
                this._loadSchemaExplorer(window.currentDatasourceId);
            }

            // Activate chat subnav tab
            document.querySelectorAll('.chat-subnav-tab').forEach(t => t.classList.remove('active'));
            const chatTab = document.querySelector('.chat-subnav-tab[data-tab="chat"]');
            if (chatTab) chatTab.classList.add('active');

            // Dispatch event for chat-agent-context module
            document.dispatchEvent(new CustomEvent('chatAgentReady', {
                detail: {
                    wsGuid: wsGuid,
                    agentGuid: agentGuid,
                    agentName: agentName,
                    datasourceId: window.currentDatasourceId,
                    datasourceName: window.currentDatasourceName,
                    datasourceType: window.currentDatasourceType
                }
            }));
        },

        // ── Show landing (no workspace selected) ────────────
        _showLanding() {
            const panel = document.getElementById('workspaceHomePanel');
            if (!panel) return;
            panel.classList.remove('hidden');
            const chatWs = document.getElementById('chatWorkspace');
            if (chatWs) chatWs.style.display = 'none';

            panel.innerHTML = `
                <div class="wf-empty-state">
                    <div class="wf-empty-icon"><i class="bi bi-folder-plus"></i></div>
                    <h4>Welcome to AIInsights</h4>
                    <p>Select a workspace from the left panel or create a new one to get started.</p>
                    <button class="btn cp-btn-gradient" id="wfLandingCreateBtn">
                        <i class="bi bi-plus-lg me-1"></i>New Workspace
                    </button>
                </div>
            `;
            const btn = document.getElementById('wfLandingCreateBtn');
            if (btn) {
                btn.addEventListener('click', () => {
                    const addBtn = document.getElementById('addWorkspaceBtn');
                    if (addBtn) addBtn.click();
                });
            }
        },

        // ── Utility ─────────────────────────────────────────
        _esc(str) {
            const div = document.createElement('div');
            div.appendChild(document.createTextNode(str || ''));
            return div.innerHTML;
        }
    };

    window.workspaceFlow = WF;
})();
