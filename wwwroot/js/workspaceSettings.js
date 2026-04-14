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
                        <div class="ws-users-header">
                            <h6><i class="bi bi-people me-2"></i>Workspace Users</h6>
                        </div>
                        <div id="wsUsersPicker"></div>
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
        const pickerDiv = ov.querySelector('#wsUsersPicker');
        const wsGuid = this._workspaceData?.guid;
        if (!list || !wsGuid) return;

        // Show people picker for adding users
        if (pickerDiv && !pickerDiv.dataset.wired) {
            pickerDiv.dataset.wired = '1';
            if (window.workspaceRoles) {
                window.workspaceRoles.buildPeoplePicker('wsUsersPicker', wsGuid, (email) => {
                    this._addUserToWorkspace(wsGuid, email);
                });
            }
        }

        // Load existing workspace users
        const users = this._workspaceData?.users || [];
        if (!users.length) {
            list.innerHTML = '<div style="text-align:center;padding:20px;color:var(--cp-text-muted);font-size:0.82rem;"><i class="bi bi-people" style="font-size:1.5rem;display:block;margin-bottom:8px;"></i>No additional users. Use the search above to add members.</div>';
            return;
        }

        list.innerHTML = users.map(u => `
            <div class="ws-user-item" data-user-id="${this._esc(u.userId)}">
                <div class="ws-user-avatar">${this._esc((u.fullName || u.email || '?').substring(0, 2).toUpperCase())}</div>
                <div class="ws-user-info">
                    <div class="ws-user-name">${this._esc(u.fullName || u.email)}</div>
                    <div class="ws-user-email">${this._esc(u.email || '')}</div>
                </div>
                <select class="form-select form-select-sm ws-user-role-select" data-user-id="${this._esc(u.userId)}">
                    <option value="Admin" ${u.role === 'Admin' ? 'selected' : ''}>Admin</option>
                    <option value="Editor" ${u.role === 'Editor' ? 'selected' : ''}>Editor</option>
                    <option value="Viewer" ${u.role === 'Viewer' ? 'selected' : ''}>Viewer</option>
                </select>
            </div>
        `).join('');

        // Wire role change handlers
        list.querySelectorAll('.ws-user-role-select').forEach(sel => {
            sel.addEventListener('change', () => {
                this._updateUserRole(wsGuid, sel.dataset.userId, sel.value);
            });
        });
    }

    async _addUserToWorkspace(wsGuid, email) {
        try {
            const r = await fetch(`/api/workspaces/${encodeURIComponent(wsGuid)}/users`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email, role: 'Viewer' })
            });
            if (r.ok) {
                this._showAlert(`User added as Viewer.`, 'success');
                // Reload workspace data to refresh users
                const wr = await fetch(`/api/workspaces/${encodeURIComponent(wsGuid)}`);
                if (wr.ok) {
                    this._workspaceData = await wr.json();
                    this._populateUsers();
                }
                setTimeout(() => this._clearAlert(), 2500);
            } else {
                this._showAlert('Failed to add user.', 'error');
            }
        } catch {
            this._showAlert('Failed to add user.', 'error');
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
        if (!confirm('Delete this workspace and all its artifacts? This cannot be undone.')) return;
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
