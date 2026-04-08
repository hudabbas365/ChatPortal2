// ChatPortal2 - Main App JS
(function() {
    'use strict';

    // Attach JWT to all AJAX requests
    const token = localStorage.getItem('cp_token');
    if (token && typeof $ !== 'undefined') {
        $(document).ajaxSend(function(event, jqxhr, settings) {
            jqxhr.setRequestHeader('Authorization', 'Bearer ' + token);
        });
    }

    // Update nav auth button based on login state
    async function updateNavAuth() {
        const authBtn = document.getElementById('navAuthBtn');
        const logoutBtn = document.getElementById('navLogoutBtn');
        const dashboardBtn = document.getElementById('navDashboardBtn');
        const chatAuthBtn = document.getElementById('chatAuthBtn');

        let user = JSON.parse(localStorage.getItem('cp_user') || 'null');

        // Verify auth state with server
        try {
            const r = await fetch('/api/auth/me');
            if (r.ok) {
                const data = await r.json();
                user = data;
                localStorage.setItem('cp_user', JSON.stringify(user));
            } else {
                // Server says not authenticated — clear stale local data
                user = null;
                localStorage.removeItem('cp_user');
                localStorage.removeItem('cp_token');
            }
        } catch (e) {
            console.error('Auth verification failed:', e);
        }

        if (user) {
            if (authBtn) authBtn.style.display = 'none';
            if (logoutBtn && !logoutBtn._logoutWired) {
                logoutBtn._logoutWired = true;
                logoutBtn.addEventListener('click', function() {
                    fetch('/api/auth/logout', { method: 'POST' }).finally(function() {
                        localStorage.removeItem('cp_user');
                        localStorage.removeItem('cp_token');
                        localStorage.removeItem('cp_plan');
                        window.location.href = '/';
                    });
                });
            }
            if (logoutBtn) logoutBtn.style.display = '';
            if (dashboardBtn) dashboardBtn.style.display = '';
            if (chatAuthBtn) {
                chatAuthBtn.innerHTML = `<i class="bi bi-person-circle me-1"></i>${user.fullName || user.email}`;
                chatAuthBtn.href = '#';
            }
        } else {
            if (authBtn) authBtn.style.display = '';
            if (logoutBtn) logoutBtn.style.display = 'none';
            if (dashboardBtn) dashboardBtn.style.display = 'none';
        }

        // Update left panel org name
        const orgNameEl = document.getElementById('orgName');
        if (orgNameEl && user) {
            orgNameEl.textContent = user.orgName || user.fullName || 'My Organization';
        }
    }

    // Load subscription info
    async function loadPlan() {
        const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
        if (!user) return;
        try {
            const r = await fetch('/api/subscription/' + user.id);
            const plan = await r.json();
            localStorage.setItem('cp_plan', JSON.stringify(plan));
        } catch {}
    }

    // Load workspaces into left panel
    async function loadWorkspaces() {
        const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
        if (!user || !user.organizationId) return;
        try {
            const r = await fetch('/api/workspaces?organizationId=' + user.organizationId);
            const workspaces = await r.json();
            const list = document.getElementById('workspaceList');
            if (!list) return;
            if (workspaces.length) {
                list.innerHTML = workspaces.map(w =>
                    `<div class="panel-list-item" data-workspace-id="${w.id}">
                        <i class="bi bi-folder me-2"></i>${w.name}
                     </div>`
                ).join('');
            }
        } catch {}
    }

    // Load agents into left panel
    async function loadAgents() {
        const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
        if (!user || !user.organizationId) return;
        try {
            const r = await fetch('/api/agents?organizationId=' + user.organizationId);
            const agents = await r.json();
            const list = document.getElementById('agentList');
            if (!list) return;
            if (agents.length) {
                list.innerHTML = agents.map(a =>
                    `<div class="panel-list-item" data-agent-id="${a.id}">
                        <i class="bi bi-robot me-2"></i>${a.name}
                     </div>`
                ).join('');
            }
        } catch {}
    }

    // Load datasources into agent modal dropdown and left panel
    async function loadDatasources() {
        const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
        if (!user || !user.organizationId) return;
        try {
            const r = await fetch('/api/datasources?organizationId=' + user.organizationId);
            const dsList = await r.json();
            const dsPanelList = document.getElementById('datasourceList');
            if (dsPanelList && dsList.length) {
                dsPanelList.innerHTML = dsList.map(d =>
                    `<div class="panel-list-item" data-ds-id="${d.id}">
                        <i class="bi bi-database me-2"></i>${d.name}
                     </div>`
                ).join('');
            }
            // Populate agent modal datasource dropdown
            const agentDsSelect = document.getElementById('agentDatasource');
            if (agentDsSelect) {
                const opts = dsList.map(d => `<option value="${d.id}">${d.name} (${d.type})</option>`).join('');
                agentDsSelect.innerHTML = '<option value="">-- Select or create datasource --</option>' + opts;
            }
        } catch {}
    }

    // Workspace modal wiring
    function initWorkspaceModal() {
        const addBtn = document.getElementById('addWorkspaceBtn');
        const createBtn = document.getElementById('createWorkspaceBtn');
        if (!addBtn) return;

        addBtn.addEventListener('click', function() {
            const modal = document.getElementById('addWorkspaceModal');
            if (modal) new bootstrap.Modal(modal).show();
        });

        if (createBtn) {
            createBtn.addEventListener('click', async function() {
                const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
                const name = document.getElementById('wsName')?.value.trim();
                const alertEl = document.getElementById('wsModalAlert');
                if (!name) {
                    if (alertEl) { alertEl.className = 'alert alert-danger'; alertEl.textContent = 'Please enter a workspace name.'; }
                    return;
                }
                try {
                    createBtn.disabled = true;
                    const r = await fetch('/api/workspaces', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ name, organizationId: user?.organizationId || 0, userId: user?.id || '' })
                    });
                    const ws = await r.json();
                    const list = document.getElementById('workspaceList');
                    if (list) {
                        const item = document.createElement('div');
                        item.className = 'panel-list-item';
                        item.dataset.workspaceId = ws.id;
                        item.innerHTML = `<i class="bi bi-folder me-2"></i>${ws.name}`;
                        list.appendChild(item);
                    }
                    bootstrap.Modal.getInstance(document.getElementById('addWorkspaceModal'))?.hide();
                    if (document.getElementById('wsName')) document.getElementById('wsName').value = '';
                } catch {
                    if (alertEl) { alertEl.className = 'alert alert-danger'; alertEl.textContent = 'Failed to create workspace.'; }
                } finally { createBtn.disabled = false; }
            });
        }
    }

    // Agent modal wiring
    function initAgentModal() {
        const addBtn = document.getElementById('addAgentBtn');
        const createBtn = document.getElementById('createAgentBtn');
        const openDsBtn = document.getElementById('openDatasourceModalBtn');
        if (!addBtn) return;

        addBtn.addEventListener('click', function() {
            loadDatasources();
            const modal = document.getElementById('addAgentModal');
            if (modal) new bootstrap.Modal(modal).show();
        });

        if (openDsBtn) {
            openDsBtn.addEventListener('click', function() {
                bootstrap.Modal.getInstance(document.getElementById('addAgentModal'))?.hide();
                initDatasourceModal();
                const modal = document.getElementById('addDatasourceModal');
                if (modal) new bootstrap.Modal(modal).show();
            });
        }

        if (createBtn) {
            createBtn.addEventListener('click', async function() {
                const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
                const name = document.getElementById('agentName')?.value.trim();
                const systemPrompt = document.getElementById('agentSystemPrompt')?.value.trim();
                const dsId = document.getElementById('agentDatasource')?.value;
                const alertEl = document.getElementById('agentModalAlert');
                if (!name) {
                    if (alertEl) { alertEl.className = 'alert alert-danger'; alertEl.textContent = 'Please enter an agent name.'; }
                    return;
                }
                try {
                    createBtn.disabled = true;
                    const r = await fetch('/api/agents', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            name, systemPrompt, datasourceId: dsId ? parseInt(dsId) : null,
                            organizationId: user?.organizationId || 0, userId: user?.id || ''
                        })
                    });
                    const agent = await r.json();
                    const list = document.getElementById('agentList');
                    if (list) {
                        const item = document.createElement('div');
                        item.className = 'panel-list-item';
                        item.dataset.agentId = agent.id;
                        item.innerHTML = `<i class="bi bi-robot me-2"></i>${agent.name}`;
                        list.appendChild(item);
                    }
                    bootstrap.Modal.getInstance(document.getElementById('addAgentModal'))?.hide();
                } catch {
                    if (alertEl) { alertEl.className = 'alert alert-danger'; alertEl.textContent = 'Failed to create agent.'; }
                } finally { createBtn.disabled = false; }
            });
        }
    }

    // Datasource modal wiring
    let selectedDsType = '';

    function initDatasourceModal() {
        const grid = document.getElementById('dsTypeGrid');
        if (!grid || grid._initialized) return;
        grid._initialized = true;

        // Load types
        fetch('/api/datasources/types').then(r => r.json()).then(types => {
            grid.innerHTML = types.map(t =>
                `<button class="ds-type-btn" data-type="${t}">${t}</button>`
            ).join('');
            grid.querySelectorAll('.ds-type-btn').forEach(btn => {
                btn.addEventListener('click', function() {
                    selectedDsType = this.dataset.type;
                    document.getElementById('dsSelectedTypeName').textContent = selectedDsType;
                    document.getElementById('dsTypeSelector').style.display = 'none';
                    document.getElementById('dsConfigForm').style.display = 'block';
                    document.getElementById('dsTestBtn').style.display = '';
                    document.getElementById('saveDatasourceBtn').style.display = '';
                });
            });
        }).catch(() => {});

        // Search filter
        const searchEl = document.getElementById('dsTypeSearch');
        if (searchEl) {
            searchEl.addEventListener('input', function() {
                const q = this.value.toLowerCase();
                grid.querySelectorAll('.ds-type-btn').forEach(btn => {
                    btn.style.display = btn.textContent.toLowerCase().includes(q) ? '' : 'none';
                });
            });
        }

        // Back button
        document.getElementById('dsBackBtn')?.addEventListener('click', function() {
            document.getElementById('dsTypeSelector').style.display = 'block';
            document.getElementById('dsConfigForm').style.display = 'none';
            document.getElementById('dsTestBtn').style.display = 'none';
            document.getElementById('saveDatasourceBtn').style.display = 'none';
        });

        // Test & Load Fields
        document.getElementById('dsTestBtn')?.addEventListener('click', async function() {
            const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
            const name = document.getElementById('dsName')?.value.trim() || selectedDsType + ' DS';
            const connStr = document.getElementById('dsConnectionString')?.value.trim() || '';
            const alertEl = document.getElementById('dsModalAlert');

            // Create datasource first then load fields
            try {
                const r = await fetch('/api/datasources', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ name, type: selectedDsType, connectionString: connStr, organizationId: user?.organizationId || 0, userId: user?.id || '' })
                });
                const ds = await r.json();
                // Load fields
                const fr = await fetch(`/api/datasources/${ds.id}/fields`);
                const fields = await fr.json();
                const fieldsDiv = document.getElementById('dsFieldsList');
                if (fieldsDiv) {
                    fieldsDiv.innerHTML = fields.map(f => `<span class="badge bg-secondary me-1 mb-1">${f}</span>`).join('');
                }
                document.getElementById('dsFieldsPreview').style.display = 'block';
                if (alertEl) { alertEl.className = 'alert alert-success'; alertEl.textContent = `Connected successfully. ${fields.length} fields loaded.`; }

                // Populate dropdown in agent modal
                const sel = document.getElementById('agentDatasource');
                if (sel) {
                    const opt = document.createElement('option');
                    opt.value = ds.id; opt.textContent = `${ds.name} (${ds.type})`;
                    sel.appendChild(opt);
                }
            } catch {
                if (alertEl) { alertEl.className = 'alert alert-danger'; alertEl.textContent = 'Connection test failed.'; }
            }
        });

        // Save datasource
        document.getElementById('saveDatasourceBtn')?.addEventListener('click', function() {
            const modal = document.getElementById('addDatasourceModal');
            bootstrap.Modal.getInstance(modal)?.hide();
            loadDatasources();
        });
    }

    document.addEventListener('DOMContentLoaded', function() {
        updateNavAuth();
        loadPlan();
        loadWorkspaces();
        loadAgents();
        loadDatasources();
        initWorkspaceModal();
        initAgentModal();
        initDatasourceModal();

        // Auto-resize textareas
        document.querySelectorAll('textarea[rows="1"]').forEach(function(ta) {
            ta.addEventListener('input', function() {
                this.style.height = 'auto';
                this.style.height = Math.min(this.scrollHeight, 200) + 'px';
            });
        });
    });

    // Global send suggestion helper for chat page
    window.sendSuggestion = function(btn) {
        const input = document.getElementById('chatInput');
        if (input) {
            input.value = btn.textContent;
            input.dispatchEvent(new Event('input'));
            const sendBtn = document.getElementById('chatSendBtn');
            if (sendBtn) sendBtn.click();
        }
    };
})();
