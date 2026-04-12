// WorkspaceWizard — New 4-step wizard: Datasource → Tables → AI Prompt → Agent
// Extends WorkspaceFlow with lineage view, wizard steps, and context features
(function () {
    'use strict';

    const WF = window.workspaceFlow;
    if (!WF) return;

    // ── Lineage view ────────────────────────────────────────
    WF._renderLineageView = function (agents, datasources, wsData) {
        const reports = wsData.reports || [];
        let html = '';
        datasources.forEach(ds => {
            const boundAgents = agents.filter(a => a.datasourceId === ds.id);
            html += '<div class="wf-lineage-row">';
            html += `
            <div class="wf-lineage-node datasource" data-action="ds-detail" data-ds-id="${ds.guid}">
                <div class="wf-lineage-icon"><i class="bi bi-database"></i></div>
                <div class="wf-lineage-info">
                    <div class="wf-lineage-name">${this._esc(ds.name)}</div>
                    <div class="wf-lineage-type">${this._esc(ds.type || 'Datasource')}</div>
                </div>
                <span class="wf-lineage-badge connected"><i class="bi bi-check-circle-fill"></i> Connected</span>
            </div>`;
            html += '<div class="wf-lineage-connector"><span class="wf-connector-pulse"></span></div>';
            html += '<div class="wf-lineage-targets">';
            boundAgents.forEach(a => {
                const agentReports = reports.filter(rpt => rpt.agentId === a.id);
                html += '<div class="wf-lineage-agent-group">';
                html += `
                <div class="wf-lineage-node agent" data-action="agent-chat" data-agent-id="${a.guid}" data-ws-id="${wsData.guid}">
                    <div class="wf-lineage-icon"><i class="bi bi-robot"></i></div>
                    <div class="wf-lineage-info">
                        <div class="wf-lineage-name">${this._esc(a.name)}</div>
                        <div class="wf-lineage-type">AI Agent</div>
                    </div>
                </div>`;
                // Sub-targets: reports linked to this agent + dashboard
                const hasSubTargets = agentReports.length > 0;
                if (hasSubTargets) {
                    html += '<div class="wf-lineage-sub-targets">';
                    agentReports.forEach(rpt => {
                        html += `
                        <div class="wf-report-node-wrap">
                            <div class="wf-report-arrows">
                                <div class="wf-report-arrow from-agent">
                                    <span class="wf-report-arrow-label">Agent</span>
                                    <div class="wf-report-arrow-line"></div>
                                </div>
                                <div class="wf-report-arrow from-dashboard">
                                    <span class="wf-report-arrow-label">Dashboard</span>
                                    <div class="wf-report-arrow-line"></div>
                                </div>
                            </div>
                            <div class="wf-lineage-node report" data-action="report-view" data-report-guid="${rpt.guid}" data-ws-id="${wsData.guid}" style="border-color:rgba(25,135,84,0.3)">
                                <div class="wf-lineage-icon" style="background:rgba(25,135,84,0.12);color:#198754"><i class="bi bi-file-earmark-bar-graph"></i></div>
                                <div class="wf-lineage-info">
                                    <div class="wf-lineage-name">${this._esc(rpt.name)}</div>
                                    <div class="wf-lineage-type">${this._esc(rpt.status || 'Draft')} · via ${this._esc(a.name)}</div>
                                </div>
                            </div>
                        </div>`;
                    });
                    html += '</div>';
                }
                html += '</div>';
            });
            // Dashboard node — always rendered as a target with proper branch lines
            html += `
            <div class="wf-lineage-agent-group">
                <div class="wf-lineage-node dashboard" data-action="dashboard" data-ws-id="${wsData.guid}">
                    <div class="wf-lineage-icon"><i class="bi bi-bar-chart-fill"></i></div>
                    <div class="wf-lineage-info">
                        <div class="wf-lineage-name">Dashboard</div>
                        <div class="wf-lineage-type">Visual Designer</div>
                    </div>
                </div>
            </div>`;
            // Reports not linked to a specific agent (orphaned or datasource-only)
            const agentLinkedIds = new Set(reports.filter(rpt => rpt.agentId && boundAgents.some(a => a.id === rpt.agentId)).map(rpt => rpt.guid));
            const hasAgents = boundAgents.length > 0;
            reports.filter(rpt => rpt.datasourceId === ds.id && !agentLinkedIds.has(rpt.guid)).forEach(rpt => {
                html += `
                <div class="wf-lineage-agent-group">
                    <div class="wf-report-node-wrap">
                        <div class="wf-report-arrows">
                            ${hasAgents ? `<div class="wf-report-arrow from-agent">
                                <span class="wf-report-arrow-label">Agent</span>
                                <div class="wf-report-arrow-line"></div>
                            </div>` : ''}
                            <div class="wf-report-arrow from-dashboard">
                                <span class="wf-report-arrow-label">Dashboard</span>
                                <div class="wf-report-arrow-line"></div>
                            </div>
                        </div>
                        <div class="wf-lineage-node report" data-action="report-view" data-report-guid="${rpt.guid}" data-ws-id="${wsData.guid}" style="border-color:rgba(25,135,84,0.3)">
                            <div class="wf-lineage-icon" style="background:rgba(25,135,84,0.12);color:#198754"><i class="bi bi-file-earmark-bar-graph"></i></div>
                            <div class="wf-lineage-info">
                                <div class="wf-lineage-name">${this._esc(rpt.name)}</div>
                                <div class="wf-lineage-type">${this._esc(rpt.status || 'Draft')}</div>
                            </div>
                        </div>
                    </div>
                </div>`;
            });
            html += '</div></div>';
        });

        const unboundAgents = agents.filter(a => !a.datasourceId || !datasources.find(d => d.id === a.datasourceId));
        if (unboundAgents.length > 0) {
            html += '<div class="wf-lineage-row wf-lineage-unbound">';
            html += `
            <div class="wf-lineage-node unbound-source">
                <div class="wf-lineage-icon"><i class="bi bi-link-45deg"></i></div>
                <div class="wf-lineage-info">
                    <div class="wf-lineage-name">Unbound</div>
                    <div class="wf-lineage-type">No datasource attached</div>
                </div>
            </div>`;
            html += '<div class="wf-lineage-connector"><span class="wf-connector-pulse"></span></div>';
            html += '<div class="wf-lineage-targets">';
            unboundAgents.forEach(a => {
                html += `
                <div class="wf-lineage-node agent" data-action="agent-chat" data-agent-id="${a.guid}" data-ws-id="${wsData.guid}">
                    <div class="wf-lineage-icon"><i class="bi bi-robot"></i></div>
                    <div class="wf-lineage-info">
                        <div class="wf-lineage-name">${this._esc(a.name)}</div>
                        <div class="wf-lineage-type">AI Agent</div>
                    </div>
                    <span class="wf-lineage-badge unbound"><i class="bi bi-exclamation-circle"></i> Unbound</span>
                </div>`;
            });
            html += '</div></div>';
        }
        return html;
    };

    // ── Show / hide New Artifact dropdown in topbar ──────────
    WF._showNewArtifactMenu = function (visible) {
        var menu = document.getElementById('newArtifactDropdown');
        if (!menu) return;
        menu.classList.toggle('d-none', !visible);
        if (!menu._wired) {
            menu._wired = true;
            document.getElementById('newDashboardBtn')?.addEventListener('click', function (e) {
                e.preventDefault();
                window.location.href = '/dashboard';
            });
        }
    };

    // ── Schema Explorer (loads into Thinking Panel) ─────────
    WF._loadSchemaExplorer = async function (dsId) {
        var explorer = document.getElementById('schemaExplorer');
        if (!explorer) return;
        explorer.innerHTML = '<div style="color:var(--cp-text-muted);font-size:0.8rem;padding:12px;"><i class="bi bi-hourglass-split me-1"></i>Loading schema...</div>';
        try {
            var r = await fetch('/api/datasources/' + dsId + '/schema');
            if (!r.ok) throw new Error();
            var schema = await r.json();
            var html = '';

            // Datasource header
            html += '<div class="schema-ds-header">';
            html += '<div class="schema-ds-icon"><i class="bi bi-database"></i></div>';
            html += '<div><div class="schema-ds-name">' + this._esc(schema.datasourceName || 'Datasource') + '</div>';
            html += '<div class="schema-ds-type">' + this._esc(schema.datasourceType || '') + '</div></div>';
            html += '</div>';

            // Tables
            var tables = schema.tables || [];
            tables.forEach(function (tbl) {
                var ico = tbl.type === 'View' ? 'bi-eye' : 'bi-table';
                html += '<div class="schema-table">';
                html += '<div class="schema-table-header" data-schema-toggle>';
                html += '<i class="bi bi-chevron-right schema-tbl-toggle"></i>';
                html += '<i class="bi ' + ico + ' schema-tbl-icon"></i>';
                html += '<span>' + WF._esc(tbl.name) + '</span>';
                html += '<span class="schema-tbl-type">' + WF._esc(tbl.type) + '</span>';
                html += '</div>';
                html += '<div class="schema-columns">';
                (tbl.columns || []).forEach(function (col) {
                    var typeClass = _schemaTypeClass(col.dataType);
                    var typeIcon  = _schemaTypeIcon(col.dataType);
                    html += '<div class="schema-col">';
                    html += '<span class="schema-type-icon ' + typeClass + '"><i class="bi ' + typeIcon + '"></i></span>';
                    if (col.isPrimaryKey) html += '<i class="bi bi-key-fill schema-pk-icon" title="Primary Key"></i>';
                    html += '<span class="schema-col-name">' + WF._esc(col.name) + '</span>';
                    html += '<span class="schema-col-type">' + WF._esc(col.dataType) + '</span>';
                    html += '</div>';
                });
                html += '</div></div>';
            });

            explorer.innerHTML = html;

            // Wire toggle
            explorer.querySelectorAll('[data-schema-toggle]').forEach(function (hdr) {
                hdr.addEventListener('click', function () {
                    this.classList.toggle('open');
                });
            });
        } catch {
            explorer.innerHTML = '<div style="color:var(--cp-text-muted);font-size:0.8rem;padding:12px;"><i class="bi bi-exclamation-triangle me-1"></i>Failed to load schema.</div>';
        }
    };

    function _schemaTypeClass(dt) {
        if (!dt) return 'type-other';
        var d = dt.toLowerCase();
        if (/int|bigint|smallint|tinyint/.test(d)) return 'type-int';
        if (/char|text|string|varchar|nvarchar/.test(d)) return 'type-string';
        if (/date|time|datetime/.test(d)) return 'type-date';
        if (/decimal|money|float|double|numeric|real/.test(d)) return 'type-decimal';
        if (/bit|bool/.test(d)) return 'type-bool';
        return 'type-other';
    }

    function _schemaTypeIcon(dt) {
        if (!dt) return 'bi-question-circle';
        var d = dt.toLowerCase();
        if (/int|bigint|smallint|tinyint/.test(d)) return 'bi-123';
        if (/char|text|string|varchar|nvarchar/.test(d)) return 'bi-fonts';
        if (/date|time|datetime/.test(d)) return 'bi-calendar-date';
        if (/decimal|money|float|double|numeric|real/.test(d)) return 'bi-currency-dollar';
        if (/bit|bool/.test(d)) return 'bi-toggle-on';
        return 'bi-question-circle';
    }

    // ── Home view switch wiring (Lineage ↔ List) ────────────
    WF._wireHomeViewSwitch = function (data) {
        var self = this;
        document.querySelectorAll('.wf-view-switch-btn').forEach(function (btn) {
            btn.addEventListener('click', function () {
                self._homeViewMode = this.dataset.homeView;
                self._renderHome(data);
            });
        });
    };

    // ── Step 1: Datasource connection ────────────────────────
    WF._renderDatasourceConnectionStep = function (wsData) {
        var content = document.getElementById('wfSetupContent');
        if (!content) return;
        content.innerHTML = `
            <div class="wf-setup-section">
                <div class="wf-setup-section-head">
                    <div class="wf-setup-section-icon" style="background:var(--cp-primary-light);color:var(--cp-primary)"><i class="bi bi-database"></i></div>
                    <div>
                        <div class="wf-setup-section-title">Connect Data Source</div>
                        <div class="wf-setup-section-desc">Select a datasource type and configure the connection</div>
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
                    <div class="wfe-cred-row">
                        <div class="wf-setup-field">
                            <label>Database User <span class="wfe-opt">(optional)</span></label>
                            <input type="text" id="wfDsUser" placeholder="e.g. sa, admin, root" />
                        </div>
                        <div class="wf-setup-field">
                            <label>Database Password <span class="wfe-opt">(optional)</span></label>
                            <input type="password" id="wfDsPassword" placeholder="••••••••" />
                        </div>
                    </div>
                    <div class="wf-setup-actions">
                        <button class="btn btn-outline-primary btn-sm" id="wfDsTestBtn"><i class="bi bi-wifi me-1"></i>Test Connection</button>
                        <button class="btn cp-btn-gradient btn-sm" id="wfDsNextBtn" style="display:none"><i class="bi bi-arrow-right me-1"></i>Next: Select Tables</button>
                    </div>
                </div>
            </div>
        `;
        this._loadDsTypes();
        this._wireDsConnectionStep(wsData);
    };

    WF._wireDsConnectionStep = function (wsData) {
        var self = this;
        var grid = document.getElementById('wfDsTypeGrid');
        var search = document.getElementById('wfDsTypeSearch');
        var backBtn = document.getElementById('wfDsBackBtn');
        var testBtn = document.getElementById('wfDsTestBtn');
        var nextBtn = document.getElementById('wfDsNextBtn');

        if (grid) {
            grid.addEventListener('click', function (e) {
                var btn = e.target.closest('.wf-ds-type-btn');
                if (!btn) return;
                self._selectedDsType = btn.dataset.type;
                document.getElementById('wfDsSelectedType').textContent = self._selectedDsType;
                document.getElementById('wfDsTypeSelector').style.display = 'none';
                document.getElementById('wfDsConfigForm').style.display = 'block';
            });
        }
        if (search) {
            search.addEventListener('input', function () {
                var q = this.value.toLowerCase();
                document.querySelectorAll('#wfDsTypeGrid .wf-ds-type-btn').forEach(function (b) {
                    b.style.display = b.textContent.toLowerCase().includes(q) ? '' : 'none';
                });
            });
        }
        if (backBtn) {
            backBtn.addEventListener('click', function () {
                document.getElementById('wfDsTypeSelector').style.display = '';
                document.getElementById('wfDsConfigForm').style.display = 'none';
            });
        }
        if (testBtn) testBtn.addEventListener('click', function () { self._testDatasourceConnection(wsData); });
        if (nextBtn) nextBtn.addEventListener('click', function () {
            self._setupStep = 2;
            self._updateSteps();
            self._renderTableSelectionStep(wsData);
        });
    };

    WF._testDatasourceConnection = async function (wsData) {
        var user = JSON.parse(localStorage.getItem('cp_user') || 'null');
        var name = document.getElementById('wfDsName')?.value.trim() || this._selectedDsType + ' DS';
        var connStr = document.getElementById('wfDsConnStr')?.value.trim() || '';
        var dbUser = document.getElementById('wfDsUser')?.value.trim() || '';
        var dbPwd = document.getElementById('wfDsPassword')?.value.trim() || '';
        var alertEl = document.getElementById('wfDsAlert');
        try {
            // Skip creation if datasource was already created (e.g. user clicked back)
            if (!this._createdDsId) {
                var r = await fetch('/api/datasources', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        name: name,
                        type: this._selectedDsType,
                        connectionString: connStr,
                        dbUser: dbUser || null,
                        dbPassword: dbPwd || null,
                        organizationId: user?.organizationId || 0,
                        workspaceId: wsData?.id || null,
                        userId: user?.id || ''
                    })
                });
                if (!r.ok) throw new Error();
                var ds = await r.json();
                this._createdDsId = ds.id;
                this._createdDsGuid = ds.guid;
                this._createdDsName = ds.name || name;
                this._createdDsType = ds.type || this._selectedDsType;
                // Sync local user org from server-resolved value
                if (ds.organizationId && user) {
                    user.organizationId = ds.organizationId;
                    localStorage.setItem('cp_user', JSON.stringify(user));
                }
            }
            document.getElementById('wfDsNextBtn').style.display = '';
            if (alertEl) { alertEl.className = 'wf-setup-alert success'; alertEl.textContent = 'Connected successfully.'; }
        } catch {
            if (alertEl) { alertEl.className = 'wf-setup-alert error'; alertEl.textContent = 'Connection test failed.'; }
        }
    };

    // ── Step 2: Table selection ──────────────────────────────
    WF._renderTableSelectionStep = async function (wsData) {
        var content = document.getElementById('wfSetupContent');
        if (!content) return;
        var self = this;
        content.innerHTML = `
            <div class="wf-setup-section">
                <div class="wf-setup-section-head">
                    <div class="wf-setup-section-icon" style="background:var(--cp-primary-light);color:var(--cp-primary)"><i class="bi bi-table"></i></div>
                    <div>
                        <div class="wf-setup-section-title">Select Tables & Views</div>
                        <div class="wf-setup-section-desc">Choose which tables the AI agent can access from <strong>${this._esc(this._createdDsName || 'your datasource')}</strong></div>
                    </div>
                </div>
                <div class="wf-setup-alert" id="wfTableAlert"></div>
                <div id="wfTableContent">
                    <div style="color:var(--cp-text-muted);font-size:0.85rem;padding:12px 0;"><i class="bi bi-hourglass-split me-1"></i>Loading tables...</div>
                </div>
                <div class="wf-setup-actions">
                    <button class="btn btn-outline-secondary btn-sm" id="wfTableBackBtn"><i class="bi bi-arrow-left me-1"></i>Back</button>
                    <button class="btn cp-btn-gradient btn-sm" id="wfTableNextBtn"><i class="bi bi-arrow-right me-1"></i>Next: AI Prompt</button>
                </div>
            </div>
        `;
        try {
            var r = await fetch('/api/datasources/' + this._createdDsId + '/tables');
            if (!r.ok) throw new Error();
            var tables = await r.json();
            var tc = document.getElementById('wfTableContent');
            if (tc) {
                var html = '<div class="wfe-table-list">';
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
                html += '</div><div class="wfe-table-actions">';
                html += '<button class="btn btn-xs btn-outline-secondary" id="wfSelAll">Select All</button>';
                html += '<button class="btn btn-xs btn-outline-secondary" id="wfDeselAll">Deselect All</button>';
                html += '</div>';
                tc.innerHTML = html;
                document.getElementById('wfSelAll')?.addEventListener('click', function () {
                    document.querySelectorAll('#wfTableContent input[type="checkbox"]').forEach(function (cb) { cb.checked = true; });
                });
                document.getElementById('wfDeselAll')?.addEventListener('click', function () {
                    document.querySelectorAll('#wfTableContent input[type="checkbox"]').forEach(function (cb) { cb.checked = false; });
                });
            }
        } catch {
            var a = document.getElementById('wfTableAlert');
            if (a) { a.className = 'wf-setup-alert error'; a.textContent = 'Failed to load tables.'; }
        }
        document.getElementById('wfTableBackBtn')?.addEventListener('click', function () {
            self._setupStep = 1;
            self._updateSteps();
            self._renderDatasourceConnectionStep(wsData);
        });
        document.getElementById('wfTableNextBtn')?.addEventListener('click', async function () {
            var sel = Array.from(document.querySelectorAll('#wfTableContent input[type="checkbox"]:checked')).map(function (cb) { return cb.value; });
            self._selectedTables = sel;
            if (self._createdDsGuid && sel.length > 0) {
                try {
                    await fetch('/api/datasources/' + self._createdDsGuid, {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ selectedTables: sel.join(',') })
                    });
                } catch { /* ignore */ }
            }
            self._setupStep = 3;
            self._updateSteps();
            self._renderAIPromptStep(wsData);
        });
    };

    // ── Step 3: AI Prompt ────────────────────────────────────
    WF._renderAIPromptStep = async function (wsData) {
        var content = document.getElementById('wfSetupContent');
        if (!content) return;
        var self = this;
        content.innerHTML = `
            <div class="wf-setup-section">
                <div class="wf-setup-section-head">
                    <div class="wf-setup-section-icon" style="background:#fff3cd;color:#856404"><i class="bi bi-stars"></i></div>
                    <div>
                        <div class="wf-setup-section-title">AI Build Prompt</div>
                        <div class="wf-setup-section-desc">Auto-generated system prompt based on your datasource and selected tables</div>
                    </div>
                </div>
                <div class="wf-setup-alert" id="wfPromptAlert"></div>
                <div class="wf-setup-field">
                    <label>System Prompt</label>
                    <div class="wfe-ai-btn-row">
                        <button class="btn btn-sm btn-outline-info wfe-ai-gen-btn" type="button" id="wfGenPromptBtn"><i class="bi bi-stars me-1"></i>Regenerate with AI</button>
                    </div>
                    <textarea id="wfSystemPrompt" rows="6" placeholder="Generating prompt..."></textarea>
                </div>
                <div class="wf-setup-actions">
                    <button class="btn btn-outline-secondary btn-sm" id="wfPromptBackBtn"><i class="bi bi-arrow-left me-1"></i>Back</button>
                    <button class="btn cp-btn-gradient btn-sm" id="wfPromptNextBtn"><i class="bi bi-arrow-right me-1"></i>Next: Create Agent</button>
                </div>
            </div>
        `;
        var pf = document.getElementById('wfSystemPrompt');
        try {
            var r = await fetch('/api/agents/generate-prompt', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    agentName: 'Data Assistant',
                    workspaceName: wsData.name || '',
                    datasourceName: self._createdDsName || '',
                    datasourceType: self._createdDsType || '',
                    selectedTables: (self._selectedTables || []).join(', ')
                })
            });
            if (!r.ok) throw new Error();
            var result = await r.json();
            if (pf) pf.value = result.prompt;
        } catch {
            if (pf) pf.value = 'You are a helpful data assistant for the "' + (wsData.name || 'workspace') + '" workspace. You have access to ' + (self._createdDsName || 'the datasource') + ' (' + (self._createdDsType || 'database') + '). Available tables: ' + (self._selectedTables || []).join(', ') + '. Help users query data, generate SQL, analyze results, and create visualizations.';
        }
        document.getElementById('wfGenPromptBtn')?.addEventListener('click', async function (e) {
            var btn = e.currentTarget;
            btn.disabled = true;
            btn.innerHTML = '<i class="bi bi-hourglass-split me-1"></i>Generating...';
            try {
                var r = await fetch('/api/agents/generate-prompt', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        agentName: 'Data Assistant',
                        workspaceName: wsData.name || '',
                        datasourceName: self._createdDsName || '',
                        datasourceType: self._createdDsType || '',
                        selectedTables: (self._selectedTables || []).join(', ')
                    })
                });
                if (!r.ok) throw new Error();
                var result = await r.json();
                if (pf) pf.value = result.prompt;
            } catch { /* keep existing */ }
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-stars me-1"></i>Regenerate with AI';
        });
        document.getElementById('wfPromptBackBtn')?.addEventListener('click', function () {
            self._setupStep = 2;
            self._updateSteps();
            self._renderTableSelectionStep(wsData);
        });
        document.getElementById('wfPromptNextBtn')?.addEventListener('click', function () {
            self._generatedPrompt = pf?.value || '';
            self._setupStep = 4;
            self._updateSteps();
            self._renderAgentCreationStep(wsData);
        });
    };

    // ── Step 4: Agent creation ───────────────────────────────
    WF._renderAgentCreationStep = function (wsData) {
        var content = document.getElementById('wfSetupContent');
        if (!content) return;
        var self = this;
        content.innerHTML = `
            <div class="wf-setup-section">
                <div class="wf-setup-section-head">
                    <div class="wf-setup-section-icon" style="background:var(--cp-purple-light);color:var(--cp-purple)"><i class="bi bi-robot"></i></div>
                    <div>
                        <div class="wf-setup-section-title">Create AI Agent</div>
                        <div class="wf-setup-section-desc">Bound to <strong>${this._esc(this._createdDsName || 'your datasource')}</strong> &mdash; all queries target this datasource only</div>
                    </div>
                </div>
                <div class="wf-setup-alert" id="wfAgentAlert"></div>
                <div class="wf-setup-field">
                    <label>Agent Name</label>
                    <input type="text" id="wfAgentName" placeholder="e.g. Sales Assistant" />
                </div>
                <div class="wf-setup-field">
                    <label>Description</label>
                    <textarea id="wfAgentDesc" rows="2" placeholder="Brief description of what this agent does..."></textarea>
                </div>
                <div class="wf-setup-field">
                    <label>System Prompt</label>
                    <textarea id="wfAgentPrompt" rows="5">${this._esc(this._generatedPrompt || '')}</textarea>
                </div>
                <div class="wf-setup-actions">
                    <button class="btn btn-outline-secondary btn-sm" id="wfAgentBackBtn"><i class="bi bi-arrow-left me-1"></i>Back</button>
                    <button class="btn cp-btn-gradient btn-sm" id="wfAgentSaveBtn"><i class="bi bi-check-lg me-1"></i>Create Agent & Finish</button>
                </div>
            </div>
        `;
        document.getElementById('wfAgentBackBtn')?.addEventListener('click', function () {
            self._setupStep = 3;
            self._updateSteps();
            self._renderAIPromptStep(wsData);
        });
        document.getElementById('wfAgentSaveBtn')?.addEventListener('click', function () { self._saveAgentAndFinish(wsData); });
    };

    WF._saveAgentAndFinish = async function (wsData) {
        var user = JSON.parse(localStorage.getItem('cp_user') || 'null');
        var name = document.getElementById('wfAgentName')?.value.trim();
        var prompt = document.getElementById('wfAgentPrompt')?.value.trim();
        var alertEl = document.getElementById('wfAgentAlert');
        if (!name) {
            if (alertEl) { alertEl.className = 'wf-setup-alert error'; alertEl.textContent = 'Please enter an agent name.'; }
            return;
        }
        try {
            // Skip agent creation if already created (e.g. user clicked back then re-completed)
            if (!this._createdAgentGuid) {
                // Create agent strictly bound to the datasource
                var r = await fetch('/api/agents', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        name: name,
                        systemPrompt: prompt || '',
                        datasourceId: this._createdDsId || null,
                        workspaceId: wsData.id,
                        organizationId: user?.organizationId || 0,
                        userId: user?.id || ''
                    })
                });
                if (!r.ok) throw new Error();
                var agent = await r.json();
                this._createdAgentGuid = agent.guid;
                // Update local user org if server resolved it
                if (agent.organizationId && user) {
                    user.organizationId = agent.organizationId;
                    localStorage.setItem('cp_user', JSON.stringify(user));
                }
                // Ensure strict binding via PUT
                if (this._createdDsId && agent.guid) {
                    try {
                        await fetch('/api/agents/' + agent.guid, {
                            method: 'PUT',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ datasourceId: this._createdDsId })
                        });
                    } catch { /* ignore */ }
                }
            }
            // Reload workspace to show lineage view
            await this._selectWorkspace(wsData.guid);
        } catch {
            if (alertEl) { alertEl.className = 'wf-setup-alert error'; alertEl.textContent = 'Failed to create agent.'; }
        }
    };

})();
