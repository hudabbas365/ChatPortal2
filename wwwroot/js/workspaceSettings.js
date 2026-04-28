// Workspace Settings — Tab-based settings popup
class WorkspaceSettings {
    constructor() {
        this._overlay = null;
        this._workspaceId = null;
        this._workspaceData = null;
        this._activeTab = 'general';
    }

    init() {
        this._buildPopup();
        this._bindEvents();
    }

    // ── Build the popup DOM ──────────────────────────────────────

    _buildPopup() {
        const overlay = document.createElement('div');
        overlay.className = 'ws-settings-overlay';
        overlay.id = 'wsSettingsOverlay';
        overlay.innerHTML = `
        <div class="ws-settings-popup">
            <div class="ws-settings-sidebar">
                <div class="ws-settings-sidebar-title">Settings</div>
                <button class="ws-settings-tab active" data-tab="general">
                    <i class="bi bi-gear"></i>General
                </button>
                <button class="ws-settings-tab" data-tab="users">
                    <i class="bi bi-people"></i>Users
                </button>
            </div>
            <div class="ws-settings-content">
                <div class="ws-settings-header">
                    <h5 class="ws-settings-header-title" id="wsSettingsTitle">Workspace Settings</h5>
                    <button class="ws-settings-close-btn" id="wsSettingsCloseBtn" title="Close">
                        <i class="bi bi-x-lg"></i>
                    </button>
                </div>
                <div class="ws-settings-body">
                    <div class="ws-settings-alert" id="wsSettingsAlert"></div>

                    <!-- General Tab -->
                    <div class="ws-settings-panel active" data-panel="general">
                        <div class="ws-field">
                            <label class="ws-field-label">Workspace Logo</label>
                            <div class="ws-logo-area">
                                <div class="ws-logo-preview" id="wsLogoPreview">
                                    <i class="bi bi-image ws-logo-placeholder" id="wsLogoPlaceholder"></i>
                                    <img id="wsLogoImg" src="" alt="Logo" style="display:none">
                                </div>
                                <div class="ws-logo-actions">
                                    <label class="btn btn-sm btn-outline-primary">
                                        <i class="bi bi-upload me-1"></i>Upload
                                        <input type="file" accept="image/*" id="wsLogoInput" style="display:none">
                                    </label>
                                    <button class="btn btn-sm btn-outline-secondary" id="wsLogoRemoveBtn">
                                        <i class="bi bi-trash me-1"></i>Remove
                                    </button>
                                </div>
                            </div>
                            <div class="ws-field-hint">Recommended: 256×256px, PNG or SVG</div>
                        </div>

                        <div class="ws-field">
                            <label class="ws-field-label" for="wsSettingsName">Workspace Name</label>
                            <input type="text" class="form-control" id="wsSettingsName" placeholder="e.g. Sales Analytics" autocomplete="off">
                        </div>

                        <div class="ws-field">
                            <label class="ws-field-label" for="wsSettingsDesc">Description</label>
                            <textarea class="form-control" id="wsSettingsDesc" rows="3" placeholder="Describe the purpose of this workspace..." autocomplete="off"></textarea>
                        </div>

                        <div class="ws-field">
                            <label class="ws-field-label">Owner</label>
                            <div class="ws-owner-display" id="wsOwnerDisplay">
                                <div class="ws-owner-avatar" id="wsOwnerAvatar">?</div>
                                <div>
                                    <div class="ws-owner-name" id="wsOwnerName">Not assigned</div>
                                    <div class="ws-owner-email" id="wsOwnerEmail"></div>
                                </div>
                            </div>
                        </div>

                        <div class="ws-field">
                            <label class="ws-field-label">Created</label>
                            <div class="ws-field-hint" id="wsCreatedAt" style="margin-top:0;font-size:0.85rem"></div>
                        </div>

                        <div class="ws-field ws-danger-zone">
                            <label class="ws-field-label" style="color:#dc3545">Danger Zone</label>
                            <button class="btn btn-sm btn-outline-danger w-100" id="wsDeleteWorkspaceBtn">
                                <i class="bi bi-trash3 me-1"></i>Delete Workspace
                            </button>
                            <div class="ws-field-hint">Permanently delete this workspace and all its artifacts.</div>
                        </div>
                    </div>

                    <!-- Users Tab -->
                    <div class="ws-settings-panel" data-panel="users">
                        <div class="ws-add-user-row">
                            <input type="email" class="form-control" id="wsAddUserEmail" placeholder="Add user by email…" autocomplete="off">
                            <select class="form-select ws-role-select" id="wsAddUserRole">
                                <option value="Viewer" selected>Viewer</option>
                                <option value="Editor">Editor</option>
                                <option value="Admin">Admin</option>
                            </select>
                            <button class="btn btn-sm cp-btn-gradient" id="wsAddUserBtn" type="button">
                                <i class="bi bi-plus-lg me-1"></i>Add
                            </button>
                        </div>
                        <div class="ws-users-list" id="wsUsersList"></div>
                    </div>
                </div>
                <div class="ws-settings-footer" id="wsSettingsFooter">
                    <button class="btn btn-sm btn-outline-secondary" id="wsSettingsCancelBtn">Cancel</button>
                    <button class="btn btn-sm cp-btn-gradient" id="wsSettingsSaveBtn">
                        <i class="bi bi-check-lg me-1"></i>Save Changes
                    </button>
                </div>
            </div>
        </div>`;
        document.body.appendChild(overlay);
        this._overlay = overlay;
    }

    // ── Bind all events ──────────────────────────────────────────

    _bindEvents() {
        const ov = this._overlay;

        // Close
        ov.querySelector('#wsSettingsCloseBtn').addEventListener('click', () => this.close());
        ov.querySelector('#wsSettingsCancelBtn').addEventListener('click', () => this.close());
        ov.addEventListener('click', (e) => { if (e.target === ov) this.close(); });

        // Escape key
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && ov.classList.contains('open')) this.close();
        });

        // Tab switching
        ov.querySelectorAll('.ws-settings-tab').forEach(tab => {
            tab.addEventListener('click', () => this._switchTab(tab.dataset.tab));
        });

        // Save
        ov.querySelector('#wsSettingsSaveBtn').addEventListener('click', () => this._save());

        // Logo upload
        ov.querySelector('#wsLogoInput').addEventListener('change', (e) => this._handleLogoUpload(e));
        ov.querySelector('#wsLogoRemoveBtn').addEventListener('click', () => this._removeLogo());

        // Delete workspace
        ov.querySelector('#wsDeleteWorkspaceBtn').addEventListener('click', () => this._deleteWorkspace());
    }

    // ── Public open / close ──────────────────────────────────────

    async open(workspaceId) {
        // Org Admin / Super Admin can access settings; workspace Admin can also access.
        try {
            var u = JSON.parse(localStorage.getItem('cp_user') || 'null');
            if (!u || (u.role !== 'OrgAdmin' && u.role !== 'SuperAdmin')) {
                if (window.workspaceRoles && !window.workspaceRoles.canAdmin()) {
                    console.warn('Settings access denied — Admin role required.');
                    return;
                }
            }
        } catch (e) { return; }

        this._workspaceId = workspaceId;
        this._activeTab = 'general';
        this._switchTab('general');
        this._clearAlert();
        this._overlay.classList.add('open');

        // Load data
        try {
            const r = await fetch(`/api/workspaces/${workspaceId}`);
            if (!r.ok) throw new Error('Not found');
            this._workspaceData = await r.json();
            this._populate();
            // If the user already navigated to the Users tab while data was loading,
            // populate the members list now (otherwise it would stay empty until
            // they manually re-click the tab).
            if (this._activeTab === 'users') {
                this._populateUsers();
            }
        } catch {
            this._showAlert('Could not load workspace.', 'error');
        }
    }

    close() {
        this._overlay.classList.remove('open');
        this._workspaceId = null;
        this._workspaceData = null;
    }

    // ── Tab switching ────────────────────────────────────────────

    _switchTab(tab) {
        this._activeTab = tab;
        const ov = this._overlay;
        ov.querySelectorAll('.ws-settings-tab').forEach(t => {
            t.classList.toggle('active', t.dataset.tab === tab);
        });
        ov.querySelectorAll('.ws-settings-panel').forEach(p => {
            p.classList.toggle('active', p.dataset.panel === tab);
        });
        // Show/hide footer save button (only for general tab)
        const footer = ov.querySelector('#wsSettingsFooter');
        footer.style.display = (tab === 'general') ? '' : 'none';

        // Load users tab content when switching to it
        if (tab === 'users' && this._workspaceData) {
            this._populateUsers();
        }
    }

    // ── Populate form fields ─────────────────────────────────────

    _populate() {
        const d = this._workspaceData;
        if (!d) return;
        const ov = this._overlay;

        ov.querySelector('#wsSettingsTitle').textContent = d.name || 'Workspace Settings';
        ov.querySelector('#wsSettingsName').value = d.name || '';
        ov.querySelector('#wsSettingsDesc').value = d.description || '';

        // Logo
        const img = ov.querySelector('#wsLogoImg');
        const ph = ov.querySelector('#wsLogoPlaceholder');
        if (d.logoUrl) {
            img.src = d.logoUrl;
            img.style.display = '';
            ph.style.display = 'none';
        } else {
            img.style.display = 'none';
            ph.style.display = '';
        }

        // Owner
        if (d.ownerName) {
            ov.querySelector('#wsOwnerAvatar').textContent = d.ownerName.charAt(0).toUpperCase();
            ov.querySelector('#wsOwnerName').textContent = d.ownerName;
            ov.querySelector('#wsOwnerEmail').textContent = d.ownerEmail || '';
        } else {
            const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
            ov.querySelector('#wsOwnerAvatar').textContent = user?.fullName?.charAt(0)?.toUpperCase() || '?';
            ov.querySelector('#wsOwnerName').textContent = user?.fullName || 'Current User';
            ov.querySelector('#wsOwnerEmail').textContent = user?.email || '';
        }

        // Created
        if (d.createdAt) {
            const dt = new Date(d.createdAt);
            ov.querySelector('#wsCreatedAt').textContent = dt.toLocaleDateString('en-US', {
                year: 'numeric', month: 'long', day: 'numeric', hour: '2-digit', minute: '2-digit'
            });
        }

        // Keep list tooltip current
        const item = document.querySelector(`.panel-list-item[data-workspace-id="${this._workspaceId}"]`);
        if (item) item.title = d.description || '';
    }

    // ── Users / Roles ────────────────────────────────────────────

    async _populateUsers() {
        const ov = this._overlay;
        const list = ov.querySelector('#wsUsersList');
        if (!list) return;

        // Workspace data may still be loading. Show a placeholder; open() will
        // re-invoke _populateUsers once the GET /api/workspaces/{guid} resolves.
        const wsGuid = this._workspaceData?.guid;
        if (!wsGuid) {
            list.innerHTML = '<div class="ws-users-empty"><i class="bi bi-hourglass-split"></i><p>Loading workspace…</p></div>';
            return;
        }

        // Wire the add-user form once
        const addBtn = ov.querySelector('#wsAddUserBtn');
        const emailInput = ov.querySelector('#wsAddUserEmail');
        const roleSelect = ov.querySelector('#wsAddUserRole');
        if (addBtn && !addBtn.dataset.wired) {
            addBtn.dataset.wired = '1';
            const submit = () => {
                const email = (emailInput.value || '').trim();
                const role = roleSelect.value || 'Viewer';
                if (!email) {
                    this._showAlert('Please enter an email address.', 'error');
                    return;
                }
                this._addUserToWorkspace(wsGuid, email, role);
                emailInput.value = '';
            };
            addBtn.addEventListener('click', submit);
            emailInput.addEventListener('keydown', (e) => {
                if (e.key === 'Enter') { e.preventDefault(); submit(); }
            });
        }

        // Load existing workspace users from the dedicated endpoint.
        // The GET /api/workspaces/{guid} response does NOT include members, so
        // we must call /api/workspaces/{guid}/users separately.
        list.innerHTML = '<div class="ws-users-empty"><i class="bi bi-hourglass-split"></i><p>Loading members…</p></div>';
        let users = [];
        try {
            const r = await fetch(`/api/workspaces/${encodeURIComponent(wsGuid)}/users`);
            if (!r.ok) {
                let msg = `Could not load members (HTTP ${r.status}).`;
                try { const j = await r.json(); if (j?.error) msg = j.error; } catch { }
                console.warn('[workspaceSettings] GET /users failed:', r.status, msg);
                list.innerHTML = `<div class="ws-users-empty"><i class="bi bi-exclamation-triangle text-danger"></i><p>${this._esc(msg)}</p></div>`;
                this._showAlert(msg, 'error');
                return;
            }
            users = await r.json();
        } catch (e) {
            console.error('[workspaceSettings] GET /users threw:', e);
            list.innerHTML = '<div class="ws-users-empty"><i class="bi bi-exclamation-triangle text-danger"></i><p>Network error loading members.</p></div>';
            this._showAlert('Network error loading members.', 'error');
            return;
        }

        if (!users || !users.length) {
            list.innerHTML = '<div class="ws-users-empty"><i class="bi bi-people"></i><p>No additional users yet. Add members above.</p></div>';
            return;
        }

        const roleClass = (r) => {
            if (r === 'Admin') return 'ws-role-admin';
            if (r === 'Editor') return 'ws-role-editor';
            return 'ws-role-viewer';
        };

        list.innerHTML = users.map(u => `
            <div class="ws-user-row" data-user-id="${this._esc(u.userId)}">
                <div class="ws-user-avatar">${this._esc((u.fullName || u.email || '?').substring(0, 2).toUpperCase())}</div>
                <div class="ws-user-info">
                    <div class="ws-user-name">${this._esc(u.fullName || u.email)}</div>
                    <div class="ws-user-email">${this._esc(u.email || '')}</div>
                </div>
                <select class="form-select form-select-sm ws-user-role-select" data-user-id="${this._esc(u.userId)}" style="width:auto;flex-shrink:0;">
                    <option value="Viewer" ${u.role === 'Viewer' ? 'selected' : ''}>Viewer</option>
                    <option value="Editor" ${u.role === 'Editor' ? 'selected' : ''}>Editor</option>
                    <option value="Admin" ${u.role === 'Admin' ? 'selected' : ''}>Admin</option>
                </select>
                <span class="ws-user-role-badge ${roleClass(u.role)}">${this._esc(u.role || 'Viewer')}</span>
                <button class="ws-user-remove-btn" type="button" data-user-id="${this._esc(u.userId)}" title="Remove user">
                    <i class="bi bi-trash"></i>
                </button>
            </div>
        `).join('');

        // Wire role change handlers
        list.querySelectorAll('.ws-user-role-select').forEach(sel => {
            sel.addEventListener('change', () => {
                this._updateUserRole(wsGuid, sel.dataset.userId, sel.value);
            });
        });
        // Wire remove buttons
        list.querySelectorAll('.ws-user-remove-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                this._removeUserFromWorkspace(wsGuid, btn.dataset.userId);
            });
        });
    }

    async _addUserToWorkspace(wsGuid, email, role) {
        try {
            const r = await fetch(`/api/workspaces/${encodeURIComponent(wsGuid)}/users`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email, role: role || 'Viewer' })
            });
            if (r.ok) {
                this._showAlert(`User added as ${role || 'Viewer'}.`, 'success');
                // Refresh the members list directly from the dedicated endpoint.
                this._populateUsers();
                setTimeout(() => this._clearAlert(), 2500);
            } else {
                let msg = 'Failed to add user.';
                try { const j = await r.json(); if (j?.error) msg = j.error; } catch {}
                this._showAlert(msg, 'error');
            }
        } catch {
            this._showAlert('Failed to add user.', 'error');
        }
    }

    async _removeUserFromWorkspace(wsGuid, userId) {
        try {
            const r = await fetch(`/api/workspaces/${encodeURIComponent(wsGuid)}/users/${encodeURIComponent(userId)}`, {
                method: 'DELETE'
            });
            if (r.ok) {
                this._showAlert('User removed.', 'success');
                this._populateUsers();
                setTimeout(() => this._clearAlert(), 2000);
            } else {
                this._showAlert('Failed to remove user.', 'error');
            }
        } catch {
            this._showAlert('Failed to remove user.', 'error');
        }
    }

    async _updateUserRole(wsGuid, userId, role) {
        try {
            const r = await fetch(`/api/workspaces/${encodeURIComponent(wsGuid)}/users/${encodeURIComponent(userId)}/role`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ role })
            });
            if (r.ok) {
                this._showAlert(`Role updated to ${role}.`, 'success');
                setTimeout(() => this._clearAlert(), 2000);
            } else {
                this._showAlert('Failed to update role.', 'error');
            }
        } catch {
            this._showAlert('Failed to update role.', 'error');
        }
    }

    // ── Save workspace settings ──────────────────────────────────

    async _save() {
        const ov = this._overlay;
        const name = ov.querySelector('#wsSettingsName').value.trim();
        const desc = ov.querySelector('#wsSettingsDesc').value.trim();
        const user = JSON.parse(localStorage.getItem('cp_user') || 'null');

        if (!name) {
            this._showAlert('Workspace name is required.', 'error');
            return;
        }

        const btn = ov.querySelector('#wsSettingsSaveBtn');
        btn.disabled = true;

        try {
            const body = {
                name,
                description: desc,
                logoUrl: this._workspaceData?.logoUrl || null,
                ownerId: this._workspaceData?.ownerId || user?.id || null,
                organizationId: this._workspaceData?.organizationId || user?.organizationId || 0,
                userId: user?.id || ''
            };
            const r = await fetch(`/api/workspaces/${this._workspaceId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            });
            if (!r.ok) throw new Error('Save failed');

            this._showAlert('Workspace saved successfully!', 'success');
            ov.querySelector('#wsSettingsTitle').textContent = name;

            // Update left panel workspace name
            const item = document.querySelector(`.panel-list-item[data-workspace-id="${this._workspaceId}"]`);
            if (item) {
                const logoUrl = this._workspaceData?.logoUrl;
                const iconHtml = logoUrl
                    ? `<img src="${this._esc(logoUrl)}" alt="" style="width:18px;height:18px;border-radius:3px;object-fit:cover;margin-right:8px;">`
                    : `<i class="bi bi-folder me-2"></i>`;
                item.innerHTML = `${iconHtml}${this._esc(name)}`;
                item.title = desc || '';
            }

            // Update subnav title if visible
            const subnav = document.getElementById('chatSubnavWorkspaceName');
            if (subnav) subnav.textContent = name;

            setTimeout(() => this._clearAlert(), 2500);
        } catch {
            this._showAlert('Failed to save workspace.', 'error');
        } finally {
            btn.disabled = false;
        }
    }

    // ── Logo handling ────────────────────────────────────────────

    _handleLogoUpload(e) {
        const file = e.target.files?.[0];
        if (!file) return;

        const reader = new FileReader();
        reader.onload = (ev) => {
            const dataUrl = ev.target.result;
            const img = this._overlay.querySelector('#wsLogoImg');
            const ph = this._overlay.querySelector('#wsLogoPlaceholder');
            img.src = dataUrl;
            img.style.display = '';
            ph.style.display = 'none';
            if (this._workspaceData) this._workspaceData.logoUrl = dataUrl;
        };
        reader.readAsDataURL(file);
    }

    _removeLogo() {
        const img = this._overlay.querySelector('#wsLogoImg');
        const ph = this._overlay.querySelector('#wsLogoPlaceholder');
        img.src = '';
        img.style.display = 'none';
        ph.style.display = '';
        if (this._workspaceData) this._workspaceData.logoUrl = '';
    }

    // ── Delete workspace ─────────────────────────────────────

    async _deleteWorkspace() {
        if (!this._workspaceId) return;
        var __ok = await (window.cpConfirm ? window.cpConfirm({ title: 'Delete workspace', message: 'Delete this workspace and all its artifacts?', subMessage: 'This cannot be undone.', confirmText: 'Delete workspace', variant: 'danger', icon: 'bi-folder-x' }) : Promise.resolve(confirm('Delete this workspace and all its artifacts? This cannot be undone.'))); if (!__ok) return;
        const wsId = this._workspaceId;
        try {
            const r = await fetch(`/api/workspaces/${wsId}`, { method: 'DELETE' });
            if (!r.ok) {
                const err = await r.json().catch(() => ({}));
                throw new Error(err.error || 'Delete failed');
            }
            // Remove from left panel
            const item = document.querySelector(`.panel-list-item[data-workspace-id="${wsId}"]`);
            if (item) item.remove();
            history.replaceState(null, '', '/chat');
            // Reset workspace flow if this was the selected workspace
            if (window.workspaceFlow) {
                window.workspaceFlow._selectedWsId = null;
                window.workspaceFlow._wsData = null;
                if (window.workspaceFlow._showLanding) window.workspaceFlow._showLanding();
            }
            this.close();
        } catch (e) {
            this._showAlert(e?.message || 'Failed to delete workspace.', 'error');
        }
    }

    // ── Alerts ───────────────────────────────────────────────────

    _showAlert(msg, type) {
        const el = this._overlay.querySelector('#wsSettingsAlert');
        el.textContent = msg;
        el.className = `ws-settings-alert show ${type}`;
    }

    _clearAlert() {
        const el = this._overlay.querySelector('#wsSettingsAlert');
        el.className = 'ws-settings-alert';
        el.textContent = '';
    }

    // ── Utility ──────────────────────────────────────────────────

    _esc(str) {
        const d = document.createElement('div');
        d.textContent = str || '';
        return d.innerHTML;
    }
}

window.workspaceSettings = new WorkspaceSettings();
