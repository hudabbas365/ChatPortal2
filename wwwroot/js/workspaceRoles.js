// WorkspaceRoles — Role-based UI, people picker, gear icon, report generation
(function () {
    'use strict';

    function esc(s) {
        var d = document.createElement('div');
        d.appendChild(document.createTextNode(String(s || '')));
        return d.innerHTML;
    }

    var _currentRole = null;
    var _isOwner = false;
    var _isOrgAdmin = false;

    var WR = {
        init() {
            this._wireGearIcons();
            this._wirePeoplePicker();
        },

        // ── Fetch current user's role for active workspace ──
        async loadMyRole(wsGuid) {
            _currentRole = null;
            _isOwner = false;
            _isOrgAdmin = false;
            try {
                var r = await fetch('/api/workspaces/' + encodeURIComponent(wsGuid) + '/myrole');
                if (r.status === 403) {
                    // User has no access to this workspace
                    _currentRole = null;
                    this._applyRoleUI();
                    return { role: null, isOwner: false, isOrgAdmin: false, noAccess: true };
                }
                if (r.ok) {
                    var data = await r.json();
                    _currentRole = data.role || 'Viewer';
                    _isOwner = !!data.isOwner;
                    _isOrgAdmin = !!data.isOrgAdmin;
                }
            } catch (e) { /* fallback */ }
            this._applyRoleUI();
            return { role: _currentRole, isOwner: _isOwner, isOrgAdmin: _isOrgAdmin };
        },

        getRole() { return _currentRole; },
        isOwner() { return _isOwner; },
        isOrgAdmin() { return _isOrgAdmin; },
        isAdmin() { return _currentRole === 'Admin' || _isOwner; },
        isEditor() { return _currentRole === 'Editor'; },
        isViewer() { return _currentRole === 'Viewer' && !_isOwner; },
        canEdit() { return _currentRole === 'Admin' || _currentRole === 'Editor' || _isOwner; },
        canAdmin() { return _currentRole === 'Admin' || _isOwner; },
        canViewAgents() { return _currentRole === 'Admin' || _currentRole === 'Editor' || _isOwner; },

        // ── Apply role-based visibility ──────────────────────
        _applyRoleUI() {
            // Show/hide role badge in subnav
            var roleBadge = document.getElementById('chatSubnavRole');
            if (roleBadge) {
                roleBadge.textContent = _currentRole || 'No Access';
                roleBadge.className = 'chat-subnav-role ' + (_currentRole || 'noaccess').toLowerCase();
                roleBadge.style.display = '';
            }

            var hasAccess      = _currentRole !== null;
            var isAdminOrOwner = _currentRole === 'Admin' || _isOwner;
            var isEditorPlus   = _currentRole === 'Admin' || _currentRole === 'Editor' || _isOwner;
            var isRestricted   = !hasAccess || (_currentRole === 'Viewer' && !_isOwner);

            // Toggle edit controls based on role
            var editControls = document.querySelectorAll('[data-role-min="editor"]');
            editControls.forEach(function (el) {
                el.style.display = isEditorPlus ? '' : 'none';
            });

            var adminControls = document.querySelectorAll('[data-role-min="admin"]');
            adminControls.forEach(function (el) {
                el.style.display = isAdminOrOwner ? '' : 'none';
            });

            // CSS permission classes
            document.querySelectorAll('.perm-admin-only').forEach(function (el) {
                el.style.display = isAdminOrOwner ? '' : 'none';
            });
            document.querySelectorAll('.perm-editor-plus').forEach(function (el) {
                el.style.display = isEditorPlus ? '' : 'none';
            });
            document.querySelectorAll('.perm-viewer-hidden').forEach(function (el) {
                el.style.display = isRestricted ? 'none' : '';
            });

            // Hide AI Insights and all mutation controls for Viewers and no-access users
            if (isRestricted) {
                document.querySelectorAll(
                    '#aiInsightsSection, .agent-panel, .btn-create-agent, ' +
                    '#newArtifactInsightsBtn, ' +
                    '.wfe-insights-del, .wfe-artifact-del, ' +
                    '.wfe-header-actions, .wfe-ws-actions, ' +
                    '.wf-flow-node.wf-flow-datasource, .wf-flow-node.wf-flow-agent, ' +
                    '.wf-flow-h-line'
                ).forEach(function (el) {
                    el.style.display = 'none';
                });
                document.body.classList.add('viewer-mode');
            } else {
                document.body.classList.remove('viewer-mode');
            }
        },

        // ── Gear icons on workspace list items (OrgAdmin only) ──
        _wireGearIcons() {
            var list = document.getElementById('workspaceList');
            if (!list) return;

            function _isUserOrgAdmin() {
                try {
                    var u = JSON.parse(localStorage.getItem('cp_user') || 'null');
                    return u && (u.role === 'OrgAdmin' || u.role === 'SuperAdmin');
                } catch (e) { return false; }
            }

            function _attachGear(item) {
                var wsId = item.dataset.workspaceId;
                if (!wsId || wsId === '0') return;
                item.dataset.gearWired = '1';
                item.style.display = 'flex';
                item.style.alignItems = 'center';

                if (!_isUserOrgAdmin()) return;

                var gear = document.createElement('button');
                gear.className = 'wf-ws-gear-btn';
                gear.title = 'Workspace Settings';
                gear.innerHTML = '<i class="bi bi-gear-fill"></i>';
                gear.addEventListener('click', function (e) {
                    e.stopPropagation();
                    if (window.workspaceSettings) {
                        window.workspaceSettings.open(wsId);
                    }
                });
                item.appendChild(gear);
            }

            var obs = new MutationObserver(function () {
                list.querySelectorAll('.panel-list-item:not([data-gear-wired])').forEach(_attachGear);
            });
            obs.observe(list, { childList: true, subtree: true });

            // Wire existing items
            list.querySelectorAll('.panel-list-item').forEach(function (item) {
                if (item.dataset.gearWired) return;
                _attachGear(item);
            });
        },

        // ── People picker for workspace user management ─────
        _wirePeoplePicker() {
            document.addEventListener('click', function (e) {
                // Close any open pickers when clicking outside
                if (!e.target.closest('.ws-people-picker')) {
                    document.querySelectorAll('.ws-people-picker-results.open').forEach(function (el) {
                        el.classList.remove('open');
                    });
                }
            });
        },

        // Build people picker HTML for a container
        buildPeoplePicker(containerId, wsGuid, onSelect) {
            var container = document.getElementById(containerId);
            if (!container) return;

            container.innerHTML = [
                '<div class="ws-people-picker">',
                '  <div class="input-group input-group-sm">',
                '    <span class="input-group-text"><i class="bi bi-search"></i></span>',
                '    <input type="text" class="form-control" placeholder="Search organization users..." id="wsPickerSearch">',
                '  </div>',
                '  <div class="ws-people-picker-results" id="wsPickerResults"></div>',
                '</div>'
            ].join('\n');

            var searchInput = document.getElementById('wsPickerSearch');
            var resultsDiv = document.getElementById('wsPickerResults');
            var _users = [];

            // Load org users
            fetch('/api/workspaces/' + encodeURIComponent(wsGuid) + '/org-users')
                .then(function (r) { return r.json(); })
                .then(function (users) { _users = users || []; })
                .catch(function () { _users = []; });

            searchInput.addEventListener('input', function () {
                var q = this.value.toLowerCase().trim();
                if (!q) {
                    resultsDiv.classList.remove('open');
                    return;
                }
                var filtered = _users.filter(function (u) {
                    return (u.fullName || '').toLowerCase().includes(q) ||
                           (u.email || '').toLowerCase().includes(q);
                });
                if (!filtered.length) {
                    resultsDiv.innerHTML = '<div class="p-3 text-center text-muted" style="font-size:0.78rem">No users found</div>';
                } else {
                    resultsDiv.innerHTML = filtered.map(function (u) {
                        var initials = (u.fullName || u.email || '?').substring(0, 2).toUpperCase();
                        return '<div class="ws-picker-item" data-user-id="' + esc(u.id) + '" data-email="' + esc(u.email) + '">' +
                            '<div class="ws-picker-item-avatar">' + esc(initials) + '</div>' +
                            '<div class="ws-picker-item-info">' +
                            '  <div class="ws-picker-item-name">' + esc(u.fullName) + '</div>' +
                            '  <div class="ws-picker-item-email">' + esc(u.email) + '</div>' +
                            '</div></div>';
                    }).join('');
                }
                resultsDiv.classList.add('open');

                resultsDiv.querySelectorAll('.ws-picker-item').forEach(function (item) {
                    item.addEventListener('click', function () {
                        var email = this.dataset.email;
                        if (onSelect) onSelect(email);
                        resultsDiv.classList.remove('open');
                        searchInput.value = '';
                    });
                });
            });
        },

        // ── Generate Report from selected charts ────────────
        async generateReport(wsGuid, opts) {
            opts = opts || {};
            try {
                var r = await fetch('/api/reports', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        workspaceGuid: wsGuid,
                        name: opts.name || 'Report — ' + new Date().toLocaleDateString(),
                        dashboardId: opts.dashboardId || null,
                        datasourceId: opts.datasourceId || null,
                        agentId: opts.agentId || null,
                        chartIds: opts.chartIds || null,
                        canvasJson: opts.canvasJson || null,
                        createdBy: opts.userId || null
                    })
                });
                if (!r.ok) throw new Error('Failed to create report');
                return await r.json();
            } catch (e) {
                console.error('Report generation failed:', e);
                return null;
            }
        },

        // ── Load reports for workspace ──────────────────────
        async loadReports(wsGuid) {
            try {
                var r = await fetch('/api/reports?workspaceGuid=' + encodeURIComponent(wsGuid));
                if (!r.ok) return [];
                return await r.json();
            } catch (e) {
                return [];
            }
        }
    };

    window.workspaceRoles = WR;

    document.addEventListener('DOMContentLoaded', function () {
        WR.init();
    });
})();
