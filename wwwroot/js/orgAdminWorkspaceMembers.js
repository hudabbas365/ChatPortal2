// orgAdminWorkspaceMembers.js — Workspace Member Management for OrgAdmin
(function () {
    'use strict';

    function esc(s) {
        var d = document.createElement('div');
        d.appendChild(document.createTextNode(String(s || '')));
        return d.innerHTML;
    }

    var _orgId = null;
    var _activeWsGuid = null;

    var WM = {
        init: function (orgId) {
            _orgId = orgId;
            this._loadWorkspaces();
        },

        _loadWorkspaces: function () {
            var container = document.getElementById('wsMemberWorkspaceList');
            if (!container) return;
            container.innerHTML = '<div class="text-center py-3"><span class="spinner-border spinner-border-sm me-2"></span>Loading...</div>';

            fetch('/api/workspaces?organizationId=' + encodeURIComponent(_orgId || 0))
                .then(function (r) { return r.json(); })
                .then(function (workspaces) {
                    if (!workspaces || workspaces.length === 0) {
                        container.innerHTML = '<p class="text-muted">No workspaces found.</p>';
                        return;
                    }
                    var html = workspaces.map(function (ws) {
                        return '<div class="ws-list-item d-flex align-items-center justify-content-between p-2 mb-2 rounded border" style="cursor:pointer" data-ws-guid="' + esc(ws.guid) + '" data-ws-name="' + esc(ws.name) + '">' +
                            '<div>' +
                            '  <strong>' + esc(ws.name) + '</strong>' +
                            '  <span class="badge bg-secondary ms-2" id="ws-member-count-' + esc(ws.guid) + '">...</span>' +
                            '</div>' +
                            '<button class="btn btn-sm btn-outline-primary" onclick="orgAdminWM.openWorkspace(\'' + esc(ws.guid) + '\',\'' + esc(ws.name) + '\')">Manage</button>' +
                            '</div>';
                    }).join('');
                    container.innerHTML = html;

                    workspaces.forEach(function (ws) {
                        WM._loadMemberCount(ws.guid);
                    });
                })
                .catch(function () {
                    container.innerHTML = '<p class="text-danger">Failed to load workspaces.</p>';
                });
        },

        _loadMemberCount: function (wsGuid) {
            fetch('/api/workspaces/' + encodeURIComponent(wsGuid) + '/users')
                .then(function (r) { return r.json(); })
                .then(function (members) {
                    var badge = document.getElementById('ws-member-count-' + wsGuid);
                    if (badge) badge.textContent = (members.length || 0) + ' member' + (members.length === 1 ? '' : 's');
                })
                .catch(function () { });
        },

        openWorkspace: function (wsGuid, wsName) {
            _activeWsGuid = wsGuid;
            var label = document.getElementById('wsMemberModalLabel');
            if (label) label.textContent = 'Members — ' + (wsName || wsGuid);

            this._loadMembers(wsGuid);

            var modal = document.getElementById('wsMembersModal');
            if (modal && window.bootstrap) {
                new bootstrap.Modal(modal).show();
            }
        },

        _loadMembers: function (wsGuid) {
            var tbody = document.getElementById('wsMembersTableBody');
            if (!tbody) return;
            tbody.innerHTML = '<tr><td colspan="4" class="text-center py-3"><span class="spinner-border spinner-border-sm me-2"></span>Loading...</td></tr>';

            fetch('/api/workspaces/' + encodeURIComponent(wsGuid) + '/users')
                .then(function (r) { return r.json(); })
                .then(function (members) {
                    if (!members || members.length === 0) {
                        tbody.innerHTML = '<tr><td colspan="4" class="text-center text-muted py-3">No members yet</td></tr>';
                        return;
                    }
                    tbody.innerHTML = members.map(function (m) {
                        return '<tr>' +
                            '<td>' + esc(m.fullName || m.email || m.userId) + '</td>' +
                            '<td class="text-muted small">' + esc(m.email || '') + '</td>' +
                            '<td>' +
                            '  <select class="form-select form-select-sm ws-role-select" data-user-id="' + esc(m.userId) + '" style="width:auto">' +
                            '    <option value="Admin"'  + (m.role === 'Admin'  ? ' selected' : '') + '>Admin</option>' +
                            '    <option value="Editor"' + (m.role === 'Editor' ? ' selected' : '') + '>Editor</option>' +
                            '    <option value="Viewer"' + (m.role === 'Viewer' ? ' selected' : '') + '>Viewer</option>' +
                            '  </select>' +
                            '</td>' +
                            '<td>' +
                            '  <button class="btn btn-sm btn-outline-danger" onclick="orgAdminWM.removeMember(\'' + esc(wsGuid) + '\',\'' + esc(m.userId) + '\')">' +
                            '    <i class="bi bi-person-x"></i>' +
                            '  </button>' +
                            '</td>' +
                            '</tr>';
                    }).join('');

                    // Wire role change selects
                    tbody.querySelectorAll('.ws-role-select').forEach(function (sel) {
                        sel.addEventListener('change', function () {
                            WM.changeRole(wsGuid, this.dataset.userId, this.value);
                        });
                    });
                })
                .catch(function () {
                    tbody.innerHTML = '<tr><td colspan="4" class="text-center text-danger py-3">Failed to load members.</td></tr>';
                });
        },

        addMember: function () {
            var wsGuid = _activeWsGuid;
            if (!wsGuid) return;
            var emailInput = document.getElementById('addMemberEmail');
            var roleSelect = document.getElementById('addMemberRole');
            var alertDiv   = document.getElementById('addMemberAlert');

            var email = (emailInput && emailInput.value.trim()) || '';
            var role  = (roleSelect && roleSelect.value) || 'Viewer';

            if (!email) {
                if (alertDiv) { alertDiv.className = 'alert alert-warning'; alertDiv.textContent = 'Email is required.'; alertDiv.classList.remove('d-none'); }
                return;
            }

            fetch('/api/workspaces/' + encodeURIComponent(wsGuid) + '/users', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email: email, role: role })
            })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (data.error) {
                    if (alertDiv) { alertDiv.className = 'alert alert-danger'; alertDiv.textContent = data.error; alertDiv.classList.remove('d-none'); }
                } else {
                    if (alertDiv) alertDiv.classList.add('d-none');
                    if (emailInput) emailInput.value = '';
                    WM._loadMembers(wsGuid);
                    WM._loadMemberCount(wsGuid);
                }
            })
            .catch(function () {
                if (alertDiv) { alertDiv.className = 'alert alert-danger'; alertDiv.textContent = 'Failed to add member.'; alertDiv.classList.remove('d-none'); }
            });
        },

        removeMember: function (wsGuid, userId) {
            if (!confirm('Remove this member from the workspace?')) return;
            fetch('/api/workspaces/' + encodeURIComponent(wsGuid) + '/users/' + encodeURIComponent(userId), {
                method: 'DELETE'
            })
            .then(function (r) { return r.json(); })
            .then(function () {
                WM._loadMembers(wsGuid);
                WM._loadMemberCount(wsGuid);
            })
            .catch(function () { alert('Failed to remove member.'); });
        },

        changeRole: function (wsGuid, userId, newRole) {
            fetch('/api/workspaces/' + encodeURIComponent(wsGuid) + '/users/' + encodeURIComponent(userId) + '/role', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ role: newRole })
            }).catch(function () { alert('Failed to update role.'); });
        }
    };

    window.orgAdminWM = WM;
})();
